using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PharmaX.Domain.Common;

namespace PharmaX.Persistence.Interceptors;

/// <summary>
/// SQLite has no rowversion of its own, so the concurrency token has to be
/// generated here. MariaDB and SQL Server would do it in the database, but
/// the workstation database is always SQLite (NET-002), so this runs on
/// every save that matters.
/// </summary>
public sealed class RowVersionInterceptor : SaveChangesInterceptor
{
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

        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private static void Stamp(DbContext? context)
    {
        if (context is null) return;

        foreach (var entry in context.ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
                continue;

            // A new value on every save is what makes the token detect a
            // concurrent write: two tills editing one product row cannot
            // both succeed.
            entry.Entity.RowVersion = Guid.NewGuid().ToByteArray();
        }
    }
}
