namespace PharmaX.Domain.Exceptions;

/// <summary>
/// A business rule was broken. Expected traffic, not a fault: presenters
/// catch this and show the message (see PresenterBase.GuardAsync).
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }

    public DomainException(string message, Exception inner)
        : base(message, inner) { }

    public DomainException() { }
}
