using PharmaX.Domain.Common;
using PharmaX.Domain.Exceptions;

namespace PharmaX.Domain.Security;

/// <summary>
/// A person who can log in. Credentials are local so that authentication
/// works with the host switched off and no internet (SEC-002).
/// </summary>
public sealed class User : AuditableEntity
{
    private readonly List<string> _roles = new();

    public string Username { get; private set; } = "";
    public string DisplayName { get; private set; } = "";

    /// The hash, never the password. Format is owned by IPasswordHasher.
    public string Credential { get; private set; } = "";

    public bool MustChangePassword { get; private set; }
    public bool IsActive { get; private set; } = true;
    public bool IsLockedOut { get; private set; }
    public int FailedAttempts { get; private set; }
    public DateTime? LastLoginAtUtc { get; private set; }
    public DateTime? PasswordChangedAtUtc { get; private set; }
    public decimal DiscountLimitPercent { get; private set; }

    /// Set for pharmacists: the registration number printed on a label.
    public string? RegistrationNumber { get; private set; }

    public IReadOnlyList<string> Roles => _roles;

    private User() { }

    public static User Create(string username, string displayName,
        string credential, bool mustChangePassword = false)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new DomainException("A user requires a username.");

        if (string.IsNullOrWhiteSpace(credential))
            throw new DomainException("A user requires a credential.");

        return new User
        {
            Username = username.Trim().ToLowerInvariant(),
            DisplayName = displayName,
            Credential = credential,
            MustChangePassword = mustChangePassword
        };
    }

    public void Rename(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new DomainException("A user requires a display name.");

        DisplayName = displayName.Trim();
    }

    public void AssignRole(string role)
    {
        if (!_roles.Contains(role, StringComparer.OrdinalIgnoreCase))
            _roles.Add(role);
    }

    public void RemoveRole(string role) => _roles.Remove(role);

    public bool IsInRole(string role) =>
        _roles.Contains(role, StringComparer.OrdinalIgnoreCase);

    public void SetDiscountLimit(decimal percent)
    {
        if (percent is < 0m or > 100m)
            throw new DomainException(
                "A discount limit must be between 0 and 100 percent.");

        DiscountLimitPercent = percent;
    }

    public void SetRegistrationNumber(string? registrationNumber) =>
        RegistrationNumber = registrationNumber;

    /// <summary>
    /// Records a good login and clears the failure count. Called only after
    /// the hasher has verified the credential.
    /// </summary>
    public void RecordSuccessfulLogin(DateTime nowUtc)
    {
        if (!IsActive)
            throw new DomainException("This account is inactive.");

        if (IsLockedOut)
            throw new DomainException(
                "This account is locked. Ask an administrator to unlock it.");

        FailedAttempts = 0;
        LastLoginAtUtc = nowUtc;
    }

    /// <summary>
    /// Locks out after five consecutive failures (SEC-008). The count is
    /// held on the user rather than in memory so that the lock survives a
    /// restart of the workstation.
    /// </summary>
    public void RecordFailedLogin()
    {
        FailedAttempts++;

        if (FailedAttempts >= 5) IsLockedOut = true;
    }

    public void Unlock()
    {
        IsLockedOut = false;
        FailedAttempts = 0;
    }

    /// <summary>
    /// The user chose a new password. Clears the must-change flag,
    /// because choosing one is exactly what that flag was asking for.
    /// </summary>
    public void ChangeCredential(string credential, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(credential))
            throw new DomainException("A credential cannot be blank.");

        Credential = credential;
        MustChangePassword = false;
        PasswordChangedAtUtc = nowUtc;
    }

    /// <summary>
    /// Re-hashes the same password under stronger parameters. Distinct
    /// from ChangeCredential on purpose: the password did not change, so
    /// this must not clear a pending must-change flag and must not move
    /// the password age. Treating an internal upgrade as a user choice
    /// would quietly release exactly the stale accounts that Section
    /// 30.3 exists to catch.
    /// </summary>
    public void UpgradeCredential(string credential)
    {
        if (string.IsNullOrWhiteSpace(credential))
            throw new DomainException("A credential cannot be blank.");

        Credential = credential;
    }

    public void RequirePasswordChange() => MustChangePassword = true;

    public void Deactivate() => IsActive = false;
    public void Reactivate() => IsActive = true;
}
