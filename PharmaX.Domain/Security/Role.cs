using PharmaX.Domain.Common;
using PharmaX.Domain.Exceptions;

namespace PharmaX.Domain.Security;

/// <summary>
/// A named bundle of permissions. Permissions are granted to roles and
/// roles to users, never permissions directly to users: a per-user grant
/// is invisible at review time and is how access quietly accumulates.
/// </summary>
public sealed class Role : Entity
{
    private readonly List<string> _permissionCodes = new();

    public string Name { get; private set; } = "";
    public string? Description { get; private set; }
    public bool IsSystemRole { get; private set; }

    public IReadOnlyList<string> PermissionCodes => _permissionCodes;

    /// <summary>
    /// In the grant list once an administrator has saved a set that is
    /// not the standard one. Startup then leaves the role alone.
    /// </summary>
    public const string EditedMark = "adm.role.grants.edited";

    private Role() { }

    public static Role Create(string name, string? description = null,
                              bool isSystemRole = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("A role requires a name.");

        return new Role
        {
            Name = name.Trim(),
            Description = description,
            IsSystemRole = isSystemRole
        };
    }

    public void Grant(string permissionCode)
    {
        if (!_permissionCodes.Contains(permissionCode, StringComparer.Ordinal))
            _permissionCodes.Add(permissionCode);
    }

    public void Revoke(string permissionCode)
    {
        if (IsSystemRole)
            throw new DomainException(
                $"The {Name} role is a system role and cannot be narrowed.");

        _permissionCodes.Remove(permissionCode);
    }

    /// <summary>
    /// Replaces the grants, including on a system role. The administration
    /// screen is the one place a system role may be narrowed.
    /// </summary>
    public void SetGrants(IEnumerable<string> codes)
    {
        ArgumentNullException.ThrowIfNull(codes);

        var wanted = new List<string>();

        foreach (var code in codes)
        {
            if (code == EditedMark) continue;

            if (!PermissionCode.All.Contains(code, StringComparer.Ordinal))
                throw new DomainException($"Unknown permission {code}.");

            if (!wanted.Contains(code, StringComparer.Ordinal))
                wanted.Add(code);
        }

        var standard = new HashSet<string>(
            StandardRoleGrants.For(Name), StringComparer.Ordinal);

        var same = wanted.Count == standard.Count
                   && wanted.TrueForAll(standard.Contains);

        _permissionCodes.Clear();

        foreach (var code in wanted) _permissionCodes.Add(code);

        if (!same) _permissionCodes.Add(EditedMark);
    }

    public bool Has(string permissionCode) =>
        _permissionCodes.Contains(permissionCode, StringComparer.Ordinal);

    /// The six standard roles seeded on a fresh installation.
    public static class Standard
    {
        public const string Administrator = "Administrator";
        public const string Pharmacist = "Pharmacist";
        public const string PharmacistAssistant = "Pharmacist Assistant";
        public const string Cashier = "Cashier";
        public const string StockController = "Stock Controller";
        public const string Manager = "Manager";

        public static IReadOnlyList<string> All { get; } = new[]
        {
            Administrator, Pharmacist, PharmacistAssistant,
            Cashier, StockController, Manager
        };
    }
}
