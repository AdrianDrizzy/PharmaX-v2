namespace PharmaX.Application.Abstractions;

/// <summary>
/// The writable side of ICurrentUser. Authentication sets it; everything
/// else only reads through ICurrentUser, which is why the two interfaces
/// are separate rather than one with setters nobody should call.
/// </summary>
public interface ISessionContext
{
    void SignIn(Guid userId, string username, IReadOnlyList<string> roles,
                decimal discountLimitPercent);

    void SignOut();

    void AttachTillSession(Guid? tillSessionId);
}
