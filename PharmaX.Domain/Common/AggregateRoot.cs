namespace PharmaX.Domain.Common;

/// <summary>
/// Marks the one entity in a cluster that the outside world is allowed to
/// hold a reference to. A repository loads and saves aggregate roots; it
/// never hands out a child on its own, because a child changed outside its
/// root is a child whose invariants nobody checked.
/// </summary>
public abstract class AggregateRoot : AuditableEntity
{
}
