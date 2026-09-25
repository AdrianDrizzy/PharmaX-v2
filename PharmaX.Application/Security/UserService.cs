using PharmaX.Application.Abstractions;
using PharmaX.Application.Repositories;
using PharmaX.Application.Results;
using PharmaX.Application.Security.Dtos;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Security;

public interface IUserService
{
    Task<Result<IReadOnlyList<UserListItemDto>>> ListAsync(
        bool includeInactive = false, CancellationToken ct = default);

    Task<Result<UserDto>> GetAsync(Guid id, CancellationToken ct = default);

    IReadOnlyList<string> AvailableRoles();

    Task<Result<Guid>> CreateAsync(UserEditDto edit, string initialPassword,
                                   CancellationToken ct = default);

    Task<Result> UpdateAsync(Guid id, UserEditDto edit,
                             CancellationToken ct = default);

    /// An administrator setting someone else's password. Always leaves
    /// the account needing a change at next login, so the administrator
    /// never knows a credential the staff member keeps using.
    Task<Result> ResetPasswordAsync(Guid id, string newPassword,
                                    CancellationToken ct = default);

    Task<Result> UnlockAsync(Guid id, CancellationToken ct = default);

    Task<Result> SetActiveAsync(Guid id, bool active,
                                CancellationToken ct = default);
}

public sealed class UserService : IUserService
{
    private readonly IUserRepository _users;
    private readonly IPermissionService _permissions;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public UserService(IUserRepository users, IPermissionService permissions,
        IPasswordHasher hasher, IAuditWriter audit, IUnitOfWork uow,
        ICurrentUser currentUser, IClock clock)
    {
        _users = users;
        _permissions = permissions;
        _hasher = hasher;
        _audit = audit;
        _uow = uow;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<UserListItemDto>>> ListAsync(
        bool includeInactive = false, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.UserRoleAssign);

        var users = await _users.ListAsync(includeInactive, ct);

        IReadOnlyList<UserListItemDto> items =
            users.Select(UserListItemDto.From).ToList();

        return Result<IReadOnlyList<UserListItemDto>>.Ok(items);
    }

    public async Task<Result<UserDto>> GetAsync(Guid id,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.UserRoleAssign);

        var user = await _users.GetAsync(id, ct);

        return user is null
            ? Result<UserDto>.Fail("SEC-020", "User not found.")
            : Result<UserDto>.Ok(UserDto.From(user));
    }

    public IReadOnlyList<string> AvailableRoles() => Role.Standard.All;

    public async Task<Result<Guid>> CreateAsync(UserEditDto edit,
        string initialPassword, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(edit);

        _permissions.Demand(PermissionCode.UserRoleAssign);

        if (Validate(edit, initialPassword) is Error problem)
            return Result<Guid>.Fail(problem.Code, problem.Message);

        if (BeyondOwnAuthority(edit.Roles) is Error wider)
            return Result<Guid>.Fail(wider.Code, wider.Message);

        if (await _users.ByUsernameAsync(edit.Username, ct) is not null)
            return Result<Guid>.Fail("SEC-021",
                $"Username {edit.Username} is already in use.");

        var user = User.Create(
            username: edit.Username,
            displayName: edit.DisplayName,
            credential: _hasher.Hash(initialPassword),

            // A password an administrator typed is a password two people
            // know, so it lasts exactly until the first login.
            mustChangePassword: true);

        foreach (var role in edit.Roles) user.AssignRole(role);

        user.SetDiscountLimit(edit.DiscountLimitPercent);
        user.SetRegistrationNumber(edit.RegistrationNumber);

        await _users.AddAsync(user, ct);

        _audit.Applied("adm.user.create", "User", user.Id,
            $"Roles: {string.Join(", ", edit.Roles)}");

        await _uow.SaveChangesAsync(ct);
        return Result<Guid>.Ok(user.Id);
    }

    public async Task<Result> UpdateAsync(Guid id, UserEditDto edit,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(edit);

        _permissions.Demand(PermissionCode.UserRoleAssign);

        if (Validate(edit, initialPassword: null) is Error problem)
            return Result.Fail(problem.Code, problem.Message);

        var user = await _users.GetAsync(id, ct);
        if (user is null) return Result.Fail("SEC-020", "User not found.");

        // The roles the user has now count as well as the ones asked for:
        // changing an administrator's record is as much an administrator's
        // job as creating one.
        if (BeyondOwnAuthority(edit.Roles.Concat(user.Roles)) is Error wider)
            return Result.Fail(wider.Code, wider.Message);

        // Losing your own administrator role locks you out of the screen
        // you are standing on, and possibly out of the system.
        if (user.Id == _currentUser.Id
            && user.IsInRole(Role.Standard.Administrator)
            && !edit.Roles.Contains(Role.Standard.Administrator,
                                    StringComparer.Ordinal))
            return Result.Fail("SEC-022",
                "You cannot remove your own Administrator role. Ask "
                + "another administrator to do it.");

        var removed = user.Roles
            .Where(r => !edit.Roles.Contains(r, StringComparer.Ordinal))
            .ToList();

        foreach (var role in removed) user.RemoveRole(role);
        foreach (var role in edit.Roles) user.AssignRole(role);

        user.Rename(edit.DisplayName);
        user.SetDiscountLimit(edit.DiscountLimitPercent);
        user.SetRegistrationNumber(edit.RegistrationNumber);

        _audit.Applied("adm.user.role.assign", "User", user.Id,
            $"Roles: {string.Join(", ", edit.Roles)}");

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result> ResetPasswordAsync(Guid id, string newPassword,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.UserRoleAssign);

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            return Result.Fail("SEC-004",
                "A password must be at least eight characters.");

        var user = await _users.GetAsync(id, ct);
        if (user is null) return Result.Fail("SEC-020", "User not found.");

        user.ChangeCredential(_hasher.Hash(newPassword), _clock.UtcNow);
        user.RequirePasswordChange();
        user.Unlock();

        // The reason matters more than the fact: a password reset is the
        // first step of most account takeovers.
        _audit.Applied("adm.user.password.reset", "User", user.Id,
            $"Reset by {_currentUser.Username}");

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result> UnlockAsync(Guid id,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.UserRoleAssign);

        var user = await _users.GetAsync(id, ct);
        if (user is null) return Result.Fail("SEC-020", "User not found.");

        user.Unlock();
        _audit.Applied("adm.user.unlock", "User", user.Id,
            $"Unlocked by {_currentUser.Username}");

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result> SetActiveAsync(Guid id, bool active,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.UserRoleAssign);

        var user = await _users.GetAsync(id, ct);
        if (user is null) return Result.Fail("SEC-020", "User not found.");

        if (!active && user.Id == _currentUser.Id)
            return Result.Fail("SEC-023",
                "You cannot deactivate the account you are signed in with.");

        if (active) user.Reactivate(); else user.Deactivate();

        _audit.Applied(active ? "adm.user.activate" : "adm.user.deactivate",
            "User", user.Id);

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    /// <summary>
    /// A role the signed-in user could not have handed out, because it
    /// carries a permission they do not hold themselves - or null.
    /// </summary>
    /// <remarks>
    /// A branch manager maintains the staff list, and without this could
    /// make themselves, or a friend, an administrator or a pharmacist.
    /// Measured against permissions rather than role names, so it holds
    /// for a site that has renamed or widened a role.
    /// </remarks>
    private Error? BeyondOwnAuthority(IEnumerable<string> roles)
    {
        foreach (var role in roles.Distinct(StringComparer.Ordinal))
        {
            var missing = StandardRoleGrants.For(role)
                .FirstOrDefault(code => !_permissions.Has(code));

            if (missing is null) continue;

            _audit.Refused("adm.user.role.assign", "Role", _currentUser.Id,
                $"{role} carries {missing}, which the assigner does not hold.");

            return new Error("SEC-030",
                $"Only someone who holds everything the {role} role can do "
                + "may give or take away that role. Ask an administrator.");
        }

        return null;
    }

    /// <summary>
    /// One validator for create and update, so the two cannot disagree
    /// about what a valid user looks like.
    /// </summary>
    private static Error? Validate(UserEditDto edit, string? initialPassword)
    {
        if (string.IsNullOrWhiteSpace(edit.Username))
            return new Error("SEC-024", "A user needs a username.");

        if (string.IsNullOrWhiteSpace(edit.DisplayName))
            return new Error("SEC-025", "A user needs a display name.");

        if (edit.Roles.Count == 0)
            return new Error("SEC-026",
                "A user with no role can sign in and do nothing. Give "
                + "them at least one.");

        var unknown = edit.Roles
            .Where(r => !Role.Standard.All.Contains(r, StringComparer.Ordinal))
            .ToList();

        if (unknown.Count > 0)
            return new Error("SEC-027",
                $"Unknown role: {string.Join(", ", unknown)}.");

        if (edit.DiscountLimitPercent is < 0m or > 100m)
            return new Error("SEC-028",
                "A discount limit must be between 0 and 100 percent.");

        // A pharmacist's registration number is printed on every label
        // they sign, so the record cannot be missing it.
        if (edit.Roles.Contains(Role.Standard.Pharmacist,
                                StringComparer.Ordinal)
            && string.IsNullOrWhiteSpace(edit.RegistrationNumber))
            return new Error("SEC-029",
                "A pharmacist needs a registration number: it is printed "
                + "on every label they dispense.");

        if (initialPassword is not null
            && (string.IsNullOrWhiteSpace(initialPassword)
                || initialPassword.Length < 8))
            return new Error("SEC-004",
                "A password must be at least eight characters.");

        return null;
    }
}
