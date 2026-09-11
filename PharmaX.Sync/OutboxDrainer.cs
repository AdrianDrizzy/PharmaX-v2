using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Offline;
using PharmaX.Persistence;
using PharmaX.Sync.Contracts;

namespace PharmaX.Sync;

/// <summary>
/// Registered as a hosted service, so it runs on its own without a timer
/// on a form. In Release 1.0 its endpoint is a null implementation, which
/// means the loop runs, finds work, and does nothing with it: deliberately,
/// so that the code path is exercised and tested long before an endpoint
/// exists.
/// </summary>
public sealed class OutboxDrainer : BackgroundService
{
    private const int BatchSize = 200;

    private readonly IDbContextFactory<PharmaXDbContext> _local;
    private readonly ISyncEndpoint _endpoint;
    private readonly IConnectivityMonitor _connectivity;
    private readonly PharmaXOptions _options;
    private readonly ILogger<OutboxDrainer> _log;

    public OutboxDrainer(IDbContextFactory<PharmaXDbContext> local,
        ISyncEndpoint endpoint, IConnectivityMonitor connectivity,
        PharmaXOptions options, ILogger<OutboxDrainer> log)
    {
        _local = local; _endpoint = endpoint;
        _connectivity = connectivity; _options = options; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(
            _options.Connectivity.ReconcileIntervalSeconds);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_connectivity.State != ConnectivityState.LocalOnly)
                    await DrainAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;     // shutting down, not a failure
            }
#pragma warning disable CA1031 // Deliberately broad: see the comment below.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                // Never let a reconciliation failure stop the loop, and
                // never surface it at a till (BR-BRN-05).
                _log.LogWarning(ex, "Reconciliation attempt failed.");
            }

            try
            {
                await Task.Delay(interval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task DrainAsync(CancellationToken ct)
    {
        await using var db = await _local.CreateDbContextAsync(ct)
                                         .ConfigureAwait(false);

        // Occurrence order, always (NET-007).
        var pending = await db.OutboxEntries
            .Where(e => e.Status == "Pending")
            .OrderBy(e => e.CreatedAtUtc)
            .Take(BatchSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (pending.Count == 0) return;

        foreach (var entry in pending)
        {
            var outcome = await _endpoint.SendAsync(
                entry.EntityType, entry.EntityId, entry.PayloadJson, ct)
                .ConfigureAwait(false);

            entry.Attempts++;

            switch (outcome)
            {
                case SendOutcome.Accepted:
                case SendOutcome.AlreadyApplied:   // idempotent (NET-024)
                    entry.Status = "Sent";
                    entry.SentAtUtc = DateTime.UtcNow;
                    break;

                case SendOutcome.Conflict:
                    entry.Status = "Conflict";     // to the review queue
                    break;

                case SendOutcome.Unavailable:
                default:
                    // Stop here rather than skipping ahead: the attempts
                    // already made are worth keeping, and the order of
                    // what is left must not change.
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                    return;
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
