namespace PharmaX.Application.Abstractions;

public interface IClock
{
    DateTime UtcNow { get; }
    DateOnly Today { get; }
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
