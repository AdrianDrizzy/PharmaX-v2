namespace PharmaX.Domain.Exceptions;

/// <summary>
/// An aggregate reached a state its own rules say is impossible. Unlike
/// DomainException this is a defect, not a refusal: it means code, not a
/// user, got something wrong.
/// </summary>
public sealed class InvariantViolationException : DomainException
{
    public InvariantViolationException(string message) : base(message) { }

    public InvariantViolationException(string message, Exception inner)
        : base(message, inner) { }

    public InvariantViolationException() { }
}
