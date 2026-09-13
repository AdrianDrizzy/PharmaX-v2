using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Offline;
using PharmaX.Persistence;

namespace PharmaX.Sync;

/// <summary>
/// Every screen shows connectivity state, so exactly one object should
/// determine it.
/// </summary>
public sealed class ConnectivityMonitor : IConnectivityMonitor, IDisposable
{
    /// Slower than this and the host is answering, but not usefully.
    private static readonly TimeSpan DegradedAbove =
        TimeSpan.FromMilliseconds(750);

    private readonly IDbContextFactory<HostDbContext> _hosts;
    private readonly PharmaXOptions _options;
    private readonly Timer _timer;

    public ConnectivityState State { get; private set; } =
        ConnectivityState.LocalOnly;

    public DateTime? LastContactUtc { get; private set; }

    /// <inheritdoc />
    public string DescribeState(ConnectivityState state)
    {
        if (_options.Database.IsShared)
        {
            return state switch
            {
                ConnectivityState.Connected => "",
                ConnectivityState.Degraded =>
                    "The pharmacy database is slow to answer",
                _ => "Cannot reach the pharmacy database - work cannot be "
                     + "saved until it is back"
            };
        }

        return state switch
        {
            ConnectivityState.Connected => "",
            ConnectivityState.Degraded =>
                "Host slow to respond - working locally",
            _ => "Working locally - transactions will sync when the host "
                 + "returns"
        };
    }

    public event EventHandler<ConnectivityState>? StateChanged;

    public ConnectivityMonitor(IDbContextFactory<HostDbContext> hosts,
                               PharmaXOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _hosts = hosts;
        _options = options;

        _timer = new Timer(
            async _ => await ProbeAsync().ConfigureAwait(false),
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(options.Connectivity.ReconcileIntervalSeconds));
    }

    public async Task<ConnectivityState> ProbeAsync(
        CancellationToken ct = default)
    {
        var previous = State;

        // Which database this installation would lose. On a shared one
        // that is the pharmacy's own server, and it is the thing worth
        // watching: losing it stops the till. Reading HostConnection
        // regardless meant a shared site never probed at all, because
        // that setting is empty there - so every screen carried a banner
        // saying the database was unreachable while the till was writing
        // to it perfectly happily.
        var watched = _options.Database.IsShared
            ? _options.Database.SharedConnection
            : _options.Database.HostConnection;

        if (string.IsNullOrWhiteSpace(watched))
        {
            // A standalone workstation has no host to reach, and saying so
            // once is better than a failed probe every thirty seconds.
            State = ConnectivityState.LocalOnly;
        }
        else
        {
            State = await MeasureAsync(ct).ConfigureAwait(false);
        }

        if (State != previous) StateChanged?.Invoke(this, State);

        return State;
    }

    private async Task<ConnectivityState> MeasureAsync(CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource
                .CreateLinkedTokenSource(ct);

            cts.CancelAfter(TimeSpan.FromSeconds(
                _options.Connectivity.HostTimeoutSeconds));

            await using var db = await _hosts
                .CreateDbContextAsync(cts.Token).ConfigureAwait(false);

            var started = DateTime.UtcNow;
            var reachable = await db.Database.CanConnectAsync(cts.Token)
                                             .ConfigureAwait(false);
            var elapsed = DateTime.UtcNow - started;

            if (!reachable) return ConnectivityState.LocalOnly;

            LastContactUtc = DateTime.UtcNow;

            return elapsed > DegradedAbove
                ? ConnectivityState.Degraded
                : ConnectivityState.Connected;
        }
#pragma warning disable CA1031 // Deliberately broad: see the comment below.
        catch (Exception)
#pragma warning restore CA1031
        {
            // Expected, routine, and not an error condition (NET-001).
            // A provider can throw anything from a socket error to a
            // configuration exception, and all of them mean the same
            // thing here: carry on locally.
            return ConnectivityState.LocalOnly;
        }
    }

    public void Dispose() => _timer.Dispose();
}
