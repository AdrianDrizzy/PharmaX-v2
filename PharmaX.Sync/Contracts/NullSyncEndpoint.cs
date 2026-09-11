namespace PharmaX.Sync.Contracts;

public enum SendOutcome { Accepted, AlreadyApplied, Conflict, Unavailable }

public interface ISyncEndpoint
{
    Task<SendOutcome> SendAsync(string entityType, Guid entityId,
        string payloadJson, CancellationToken ct = default);
}

/// <summary>
/// Release 1.0 default. Reports that nothing can be sent, which keeps
/// outbox entries pending and the local database authoritative.
/// </summary>
public sealed class NullSyncEndpoint : ISyncEndpoint
{
    public Task<SendOutcome> SendAsync(string entityType, Guid entityId,
        string payloadJson, CancellationToken ct = default) =>
        Task.FromResult(SendOutcome.Unavailable);
}
