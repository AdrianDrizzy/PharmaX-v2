namespace PharmaX.Domain.Security;

/// <summary>
/// One line of the audit trail. Append-only and written inside the same
/// transaction as the change it records (SEC-031), which is why it has no
/// mutating methods at all: nothing in the system may revise history.
/// </summary>
public sealed class AuditEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime OccurredAtUtc { get; private set; }
    public Guid UserId { get; private set; }
    public string Workstation { get; private set; } = "";
    public string Action { get; private set; } = "";
    public string EntityType { get; private set; } = "";
    public Guid EntityId { get; private set; }
    public string? BeforeJson { get; private set; }
    public string? AfterJson { get; private set; }

    /// Applied or Refused. A refusal is as informative as a change
    /// (SEC-035): a pattern of attempted overrides is something the
    /// manager should be able to see.
    public string Outcome { get; private set; } = "Applied";

    public string? Reason { get; private set; }

    private AuditEntry() { }

    public static AuditEntry Create(DateTime occurredAtUtc, Guid userId,
        string workstation, string action, string entityType, Guid entityId,
        string? beforeJson = null, string? afterJson = null,
        string outcome = "Applied", string? reason = null) =>
        new()
        {
            OccurredAtUtc = occurredAtUtc,
            UserId = userId,
            Workstation = workstation,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            BeforeJson = beforeJson,
            AfterJson = afterJson,
            Outcome = outcome,
            Reason = reason
        };
}
