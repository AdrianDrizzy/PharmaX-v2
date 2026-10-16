using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PharmaX.Application.Abstractions;
using PharmaX.Domain.Common;
using PharmaX.Domain.Security;

namespace PharmaX.Persistence.Interceptors;

public sealed class AuditInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly PharmaXOptions _options;

    public AuditInterceptor(ICurrentUser user, IClock clock,
                            PharmaXOptions options)
    {
        _user = user;
        _clock = clock;
        _options = options;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        // The synchronous path exists because the seeder and the migrator
        // both save synchronously, and an audit trail with holes in it is
        // worse than none.
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null) return;

        var now = _clock.UtcNow;

        // Materialised first: adding audit rows inside the loop would
        // modify the collection being enumerated.
        var entries = context.ChangeTracker.Entries<IAuditable>().ToList();

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                // Kept where the record already says when it was made: an
                // imported sale was created when it was rung up, not on the
                // day it was brought across.
                if (entry.Entity.CreatedAtUtc == default)
                    entry.Entity.CreatedAtUtc = now;

                entry.Entity.CreatedBy = _user.Id;
            }

            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = now;
                entry.Entity.UpdatedBy = _user.Id;
            }

            if (entry.State == EntityState.Modified)
                context.Add(BuildAuditEntry(entry, now));
        }
    }

    private AuditEntry BuildAuditEntry(EntityEntry<IAuditable> entry,
                                       DateTime now)
    {
        var changed = entry.Properties
            .Where(p => p.IsModified)
            .ToList();

        return AuditEntry.Create(
            occurredAtUtc: now,
            userId: _user.Id,
            workstation: _options.Workstation.TillNumber,
            action: entry.Entity.GetType().Name + ".update",
            entityType: entry.Entity.GetType().Name,
            entityId: entry.Entity.Id,
            beforeJson: Serialise(changed, original: true),
            afterJson: Serialise(changed, original: false),
            outcome: "Applied");
    }

    private static string Serialise(
        IEnumerable<PropertyEntry> props, bool original) =>
        System.Text.Json.JsonSerializer.Serialize(
            props.ToDictionary(
                p => p.Metadata.Name,
                p => original ? p.OriginalValue : p.CurrentValue));
}
