namespace PharmaX.Domain.Common;

public abstract class Entity
{
    // Workstation-generated so records can be created offline without
    // collision (NET-020).
    public Guid Id { get; protected set; } = Guid.NewGuid();

    public override bool Equals(object? obj) =>
        obj is Entity other && other.GetType() == GetType() && other.Id == Id;

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}

public interface IAuditable
{
    Guid Id { get; }
    DateTime CreatedAtUtc { get; set; }
    Guid CreatedBy { get; set; }
    DateTime UpdatedAtUtc { get; set; }
    Guid UpdatedBy { get; set; }
}

public abstract class AuditableEntity : Entity, IAuditable
{
    public DateTime CreatedAtUtc { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Guid UpdatedBy { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
