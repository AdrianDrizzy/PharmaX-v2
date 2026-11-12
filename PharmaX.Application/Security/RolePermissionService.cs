using PharmaX.Application.Abstractions;
using PharmaX.Application.Repositories;
using PharmaX.Application.Results;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Security;

/// <summary>One role and the permissions ticked for it.</summary>
public sealed record RoleGrants(string Role, IReadOnlyList<string> Permissions);

public interface IRolePermissionService
{
    Task<Result<IReadOnlyList<RoleGrants>>> ListAsync(
        CancellationToken ct = default);

    Task<Result> SaveAsync(IReadOnlyList<RoleGrants> grants,
                           CancellationToken ct = default);
}

public sealed class RolePermissionService : IRolePermissionService
{
    private readonly IRoleRepository _roles;
    private readonly IPermissionService _permissions;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;

    public RolePermissionService(IRoleRepository roles,
        IPermissionService permissions, ICurrentUser currentUser,
        IAuditWriter audit, IUnitOfWork uow)
    {
        _roles = roles;
        _permissions = permissions;
        _currentUser = currentUser;
        _audit = audit;
        _uow = uow;
    }

    public async Task<Result<IReadOnlyList<RoleGrants>>> ListAsync(
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.RolePermissions);

        var roles = await _roles.ListAsync(ct);

        IReadOnlyList<RoleGrants> rows = Role.Standard.All
            .Select(name => new RoleGrants(name, Codes(roles, name)))
            .ToList();

        return Result<IReadOnlyList<RoleGrants>>.Ok(rows);
    }

    public async Task<Result> SaveAsync(IReadOnlyList<RoleGrants> grants,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(grants);

        _permissions.Demand(PermissionCode.RolePermissions);

        if (grants.Select(g => g.Role).Distinct(StringComparer.Ordinal).Count()
            != grants.Count)
            return Result.Fail("SEC-040", "Each role can only be saved once.");

        foreach (var grant in grants)
        {
            if (!Role.Standard.All.Contains(grant.Role, StringComparer.Ordinal))
                return Result.Fail("SEC-040", $"Unknown role {grant.Role}.");

            if (grant.Permissions is null)
                return Result.Fail("SEC-041", $"No permissions were sent for {grant.Role}.");

            foreach (var code in grant.Permissions)
            {
                if (!PermissionCode.All.Contains(code, StringComparer.Ordinal))
                    return Result.Fail("SEC-041", $"Unknown permission {code}.");
            }
        }

        var roles = await _roles.ListAsync(ct);

        if (!KeepsOwnAccess(grants, roles))
            return Result.Fail("SEC-042",
                "Leave role permissions ticked for a role you hold, or "
                + "you cannot open this screen again.");

        foreach (var grant in grants)
        {
            var role = roles.FirstOrDefault(r =>
                string.Equals(r.Name, grant.Role, StringComparison.Ordinal));

            if (role is null)
                return Result.Fail("SEC-040", $"Unknown role {grant.Role}.");

            role.SetGrants(grant.Permissions);

            _audit.Applied(PermissionCode.RolePermissions, "Role", role.Id,
                grant.Role);
        }

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    /// <summary>
    /// The signed-in person must still hold role permissions through a
    /// role they are in, after this save.
    /// </summary>
    private bool KeepsOwnAccess(IReadOnlyList<RoleGrants> grants,
                                IReadOnlyList<Role> roles)
    {
        var inAny = false;
        var held = false;

        foreach (var name in Role.Standard.All)
        {
            if (!_currentUser.IsInRole(name)) continue;

            inAny = true;

            var submitted = grants.FirstOrDefault(g =>
                string.Equals(g.Role, name, StringComparison.Ordinal));

            var codes = submitted is null
                ? Codes(roles, name)
                : submitted.Permissions;

            if (codes.Contains(PermissionCode.RolePermissions,
                               StringComparer.Ordinal))
                held = true;
        }

        return inAny && held;
    }

    private static IReadOnlyList<string> Codes(IReadOnlyList<Role> roles,
                                               string name)
    {
        var role = roles.FirstOrDefault(r =>
            string.Equals(r.Name, name, StringComparison.Ordinal));

        if (role is null) return Array.Empty<string>();

        return role.PermissionCodes
            .Where(code => code != Role.EditedMark)
            .ToList();
    }
}
