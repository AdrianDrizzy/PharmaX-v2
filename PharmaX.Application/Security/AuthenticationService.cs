using PharmaX.Application.Abstractions;
using PharmaX.Application.Repositories;
using PharmaX.Application.Results;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Security;

public sealed record AuthenticatedUserDto(
    Guid Id, string Username, string DisplayName,
    IReadOnlyList<string> Roles, bool MustChangePassword)
{
    /// Asked by the supervisor and pharmacist prompt, which needs to know
    /// whether the second person at the keyboard holds the right role.
    public bool IsInRole(string role) =>
        Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
}

public interface IAuthenticationService
{
    Task<Result<AuthenticatedUserDto>> AuthenticateAsync(
        string username, string password, CancellationToken ct = default);

    /// Verifies a second person at the keyboard without changing who is
    /// signed in. Used for supervisor overrides and pharmacist sign-off.
    Task<Result<Guid>> AuthoriseAsync(string username, string password,
        string permissionCode, CancellationToken ct = default);

    Task<Result> ChangePasswordAsync(string username, string currentPassword,
        string newPassword, CancellationToken ct = default);

    void SignOut();
}

public sealed class AuthenticationService : IAuthenticationService
{
    private readonly IUserRepository _users;
    private readonly IPermissionRepository _permissions;
    private readonly IPasswordHasher _hasher;
    private readonly ISessionContext _session;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public AuthenticationService(IUserRepository users,
        IPermissionRepository permissions, IPasswordHasher hasher,
        ISessionContext session, IAuditWriter audit, IUnitOfWork uow,
        IClock clock)
    {
        _users = users;
        _permissions = permissions;
        _hasher = hasher;
        _session = session;
        _audit = audit;
        _uow = uow;
        _clock = clock;
    }

    public async Task<Result<AuthenticatedUserDto>> AuthenticateAsync(
        string username, string password, CancellationToken ct = default)
    {
        var user = await _users.ByUsernameAsync(username, ct);

        // Same message whether the username or the password was wrong, so
        // that the form cannot be used to enumerate accounts (SEC-009).
        if (user is null || !_hasher.Verify(password, user.Credential))
        {
            if (user is not null)
            {
                user.RecordFailedLogin();
                _audit.Refused("auth.login", "User", user.Id,
                    "Incorrect credentials");
                await _uow.SaveChangesAsync(ct);
            }

            return Result<AuthenticatedUserDto>.Fail("SEC-001",
                "Username or password is incorrect.");
        }

        if (!user.IsActive)
            return Result<AuthenticatedUserDto>.Fail("SEC-003",
                "This account is inactive.");

        if (user.IsLockedOut)
            return Result<AuthenticatedUserDto>.Fail("SEC-008",
                "This account is locked. Ask an administrator to unlock it.");

        user.RecordSuccessfulLogin(_clock.UtcNow);

        // A good login on a weak hash is the only moment the password is
        // in memory, so it is the only moment it can be upgraded. Upgrade,
        // not change: the password is the same one, so a pending
        // must-change flag has to survive it.
        if (_hasher.NeedsRehash(user.Credential))
            user.UpgradeCredential(_hasher.Hash(password));

        _session.SignIn(user.Id, user.Username, user.Roles,
                        user.DiscountLimitPercent);

        _audit.Applied("auth.login", "User", user.Id);
        await _uow.SaveChangesAsync(ct);

        return Result<AuthenticatedUserDto>.Ok(new AuthenticatedUserDto(
            user.Id, user.Username, user.DisplayName, user.Roles,
            user.MustChangePassword));
    }

    public async Task<Result<Guid>> AuthoriseAsync(string username,
        string password, string permissionCode, CancellationToken ct = default)
    {
        var user = await _users.ByUsernameAsync(username, ct);

        if (user is null || !_hasher.Verify(password, user.Credential))
            return Result<Guid>.Fail("SEC-001",
                "Username or password is incorrect.");

        if (!user.IsActive || user.IsLockedOut)
            return Result<Guid>.Fail("SEC-003",
                "That account cannot authorise anything.");

        if (!_permissions.UserHas(user.Id, permissionCode))
        {
            // An attempted override that the authoriser could not grant is
            // exactly the pattern a manager needs to see (SEC-035).
            _audit.Refused(permissionCode, "User", user.Id,
                "Authoriser lacks the permission");

            await _uow.SaveChangesAsync(ct);

            return Result<Guid>.Fail("SEC-010",
                $"{user.DisplayName} cannot authorise this action.");
        }

        _audit.Applied(permissionCode, "User", user.Id, "Override authorised");
        await _uow.SaveChangesAsync(ct);

        return Result<Guid>.Ok(user.Id);
    }

    public async Task<Result> ChangePasswordAsync(string username,
        string currentPassword, string newPassword,
        CancellationToken ct = default)
    {
        var user = await _users.ByUsernameAsync(username, ct);

        if (user is null || !_hasher.Verify(currentPassword, user.Credential))
            return Result.Fail("SEC-001", "Your current password is incorrect.");

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            return Result.Fail("SEC-004",
                "A password must be at least eight characters.");

        if (_hasher.Verify(newPassword, user.Credential))
            return Result.Fail("SEC-005",
                "The new password must differ from the current one.");

        user.ChangeCredential(_hasher.Hash(newPassword), _clock.UtcNow);
        _audit.Applied("auth.password.change", "User", user.Id);

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public void SignOut() => _session.SignOut();
}
