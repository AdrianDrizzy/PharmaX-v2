namespace PharmaX.Domain.Security;

/// <summary>
/// A user identifier that cannot be passed where a batch or product id was
/// wanted. Guid parameters all look alike at a call site; this one does not.
/// </summary>
public readonly record struct UserId(Guid Value)
{
    public static readonly UserId System = new(Guid.Empty);

    public static UserId New() => new(Guid.NewGuid());

    public bool IsSystem => Value == Guid.Empty;

    public override string ToString() => Value.ToString();

    public static implicit operator Guid(UserId id) => id.Value;
    public static explicit operator UserId(Guid value) => new(value);

    public Guid ToGuid() => Value;
    public static UserId FromGuid(Guid value) => new(value);
}
