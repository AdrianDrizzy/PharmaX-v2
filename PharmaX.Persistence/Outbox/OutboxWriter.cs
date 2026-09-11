using PharmaX.Application.Abstractions;

namespace PharmaX.Persistence.Outbox;

public sealed class OutboxEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string EntityType { get; init; } = "";
    public Guid EntityId { get; init; }
    public string PayloadJson { get; init; } = "";
    public DateTime CreatedAtUtc { get; init; }
    public string Status { get; set; } = "Pending";
    public int Attempts { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public string? LastError { get; set; }
}

public sealed class OutboxWriter : IOutbox
{
    private readonly PharmaXDbContext _db;
    private readonly IClock _clock;

    public OutboxWriter(PharmaXDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    // No SaveChanges here on purpose: the caller's transaction commits
    // the outbox row together with the change it describes (NET-023).
    public void Append(string entityType, Guid entityId, string payloadJson) =>
        _db.OutboxEntries.Add(new OutboxEntry
        {
            EntityType = entityType,
            EntityId = entityId,
            PayloadJson = payloadJson,
            CreatedAtUtc = _clock.UtcNow
        });
}
