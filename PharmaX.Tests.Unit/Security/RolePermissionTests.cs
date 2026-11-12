using FluentAssertions;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Repositories;
using PharmaX.Application.Security;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Security;
using PharmaX.Tests.Unit.Fakes;

namespace PharmaX.Tests.Unit.Security;

public class RolePermissionTests
{
    [Fact]
    public void Every_permission_has_a_label() =>
        PermissionCode.All.Should().OnlyContain(code =>
            PermissionCode.Label(code) != code);

    [Fact]
    public void Each_administration_screen_can_be_handed_out_on_its_own()
    {
        var backupOnly = new PermissionService(new FakeCurrentUser(),
            new FakePermissionRepository(new[] { PermissionCode.Backup }),
            new FakeAuditWriter());

        ScreenAccess.CanOpen("backup", backupOnly).Should().BeTrue();
        foreach (var other in new[] { "system-setup", "branches", "receipt-template",
                                      "data-import", "system-console", "control-variables" })
            ScreenAccess.CanOpen(other, backupOnly).Should().BeFalse(other);
    }

    [Fact]
    public void An_administrator_can_narrow_a_system_role_and_startup_will_leave_it()
    {
        var role = Role.Create(Role.Standard.Cashier, isSystemRole: true);

        foreach (var code in StandardRoleGrants.For(role.Name))
            role.Grant(code);

        role.SetGrants(new[] { PermissionCode.SaleCreate });

        role.Has(PermissionCode.SaleCreate).Should().BeTrue();
        role.Has(PermissionCode.ProductRead).Should().BeFalse();
        role.Has(Role.EditedMark).Should().BeTrue();

        role.SetGrants(StandardRoleGrants.For(Role.Standard.Cashier));

        role.Has(Role.EditedMark).Should().BeFalse();
        role.PermissionCodes.Should().BeEquivalentTo(
            StandardRoleGrants.For(Role.Standard.Cashier));
    }

    [Fact]
    public void An_unknown_permission_is_refused()
    {
        var role = Role.Create(Role.Standard.Cashier, isSystemRole: true);

        var act = () => role.SetGrants(new[] { "not.a.permission" });

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public async Task The_signed_in_person_cannot_take_role_permissions_off_themselves()
    {
        var administrator = Role.Create(Role.Standard.Administrator, isSystemRole: true);

        foreach (var code in StandardRoleGrants.For(administrator.Name))
            administrator.Grant(code);

        var cashier = Role.Create(Role.Standard.Cashier, isSystemRole: true);

        foreach (var code in StandardRoleGrants.For(cashier.Name))
            cashier.Grant(code);

        var user = new FakeCurrentUser { Roles = new[] { Role.Standard.Administrator } };
        var service = new RolePermissionService(
            new RoleStore(administrator, cashier),
            new PermissionService(user,
                new FakePermissionRepository(PermissionCode.All),
                new FakeAuditWriter()),
            user, new FakeAuditWriter(), new FakeUnitOfWork());

        var without = StandardRoleGrants.For(Role.Standard.Administrator)
            .Where(code => code != PermissionCode.RolePermissions)
            .ToList();

        var refused = await service.SaveAsync(new[]
        {
            new RoleGrants(Role.Standard.Administrator, without),
            new RoleGrants(Role.Standard.Cashier,
                StandardRoleGrants.For(Role.Standard.Cashier).ToList())
        });

        refused.IsFailure.Should().BeTrue();
        refused.Error.Code.Should().Be("SEC-042");
        administrator.Has(PermissionCode.RolePermissions).Should().BeTrue();

        var narrowed = StandardRoleGrants.For(Role.Standard.Cashier)
            .Where(code => code != PermissionCode.ProductRead)
            .ToList();

        var saved = await service.SaveAsync(new[]
        {
            new RoleGrants(Role.Standard.Administrator,
                StandardRoleGrants.For(Role.Standard.Administrator).ToList()),
            new RoleGrants(Role.Standard.Cashier, narrowed)
        });

        saved.IsSuccess.Should().BeTrue(saved.Error.Message);
        cashier.Has(PermissionCode.ProductRead).Should().BeFalse();
        cashier.Has(Role.EditedMark).Should().BeTrue();
    }

    private sealed class RoleStore : IRoleRepository
    {
        private readonly List<Role> _roles;

        public RoleStore(params Role[] roles) => _roles = roles.ToList();

        public Task<IReadOnlyList<Role>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Role>>(_roles);

        public Task<IReadOnlyList<string>?> CodesAsync(string role,
            CancellationToken ct = default)
        {
            var found = _roles.Find(r => r.Name == role);

            if (found is null) return Task.FromResult<IReadOnlyList<string>?>(null);

            IReadOnlyList<string> codes = found.PermissionCodes
                .Where(code => code != Role.EditedMark)
                .ToList();

            return Task.FromResult<IReadOnlyList<string>?>(codes);
        }
    }
}
