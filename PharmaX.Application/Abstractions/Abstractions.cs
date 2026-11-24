namespace PharmaX.Application.Abstractions;

public interface IClock
{
    DateTime UtcNow { get; }
    DateOnly Today { get; }

    /// <summary>Set when this PC's clock was moved after PharmaX started.</summary>
    string? Changed() => null;
}

public interface ICurrentUser
{
    Guid Id { get; }
    string Username { get; }
    Guid? TillSessionId { get; }
    decimal DiscountLimitPercent { get; }
    bool IsInRole(string role);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Task<IAsyncDisposable> BeginTransactionAsync(CancellationToken ct = default);

    /// <summary>
    /// Forgets every record read so far, and anything not yet saved, so
    /// the next read comes from the database.
    /// </summary>
    /// <remarks>
    /// For a screen that stays open while other screens change what it
    /// shows. The product list keeps one unit of work for as long as it is
    /// open; the product editor saves through its own, and a batch the
    /// list read before that save would otherwise come back as it was then
    /// - and be refused as stale when the list tried to change it.
    /// </remarks>
    void Forget();
}

public interface IOutbox
{
    // Appended inside the same transaction as the change (NET-023).
    void Append(string entityType, Guid entityId, string payloadJson);
}

public interface IDocumentNumbering
{
    Task<string> NextAsync(string series, CancellationToken ct = default);
    Task<int> RemainingAsync(string series, CancellationToken ct = default);
}

public interface IAuditWriter
{
    void Applied(string action, string entityType, Guid entityId,
                 string? reason = null);
    void Refused(string action, string entityType, Guid entityId,
                 string? reason = null);
}

/// <summary>The database server's own time, and its offset from UTC.</summary>
public sealed record ServerTime(DateTime UtcNow, TimeSpan UtcOffset);

/// <summary>
/// The database server's clock. Null where there is no server to ask
/// (a standalone computer) or it cannot be reached.
/// </summary>
public interface IServerClock
{
    Task<ServerTime?> ReadAsync(CancellationToken ct = default);
}

/// <summary>Sets how far this computer's clock is from the server's.</summary>
public interface IClockCorrection
{
    void Correct(TimeSpan offset);
}
