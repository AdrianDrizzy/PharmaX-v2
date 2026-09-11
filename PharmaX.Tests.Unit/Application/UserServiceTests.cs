using FluentAssertions;
using PharmaX.Application.Security;
using PharmaX.Application.Security.Dtos;
using PharmaX.Domain.Security;
using PharmaX.Tests.Unit.Fakes;

namespace PharmaX.Tests.Unit.Application;

public class UserServiceTests
{
    private static readonly string[] CashierOnly = { Role.Standard.Cashier };

    [Fact]
    public async Task Creating_an_account_forces_a_password_change()
    {
        var builder = new UserServiceBuilder();
        var sut = builder.Build();

        var result = await sut.CreateAsync(
            Edit("newcashier", CashierOnly), "Starting#1");

        result.IsSuccess.Should().BeTrue(result.Error.Message);

        var created = await builder.Users.GetAsync(result.Value);

        // A password an administrator typed is a password two people
        // know, so it lasts exactly until the first login.
        created!.MustChangePassword.Should().BeTrue();
        created.Credential.Should().NotBe("Starting#1");
    }

    [Fact]
    public async Task A_duplicate_username_is_refused()
    {
        var builder = new UserServiceBuilder();
        var sut = builder.Build();

        await sut.CreateAsync(Edit("taken", CashierOnly), "Starting#1");
        var second = await sut.CreateAsync(Edit("taken", CashierOnly),
                                           "Starting#1");

        second.IsFailure.Should().BeTrue();
        second.Error.Code.Should().Be("SEC-021");
    }

    /// <summary>
    /// An account with no role can sign in and do nothing, which reads
    /// as a broken system rather than as a configuration mistake.
    /// </summary>
    [Fact]
    public async Task An_account_with_no_role_is_refused()
    {
        var sut = new UserServiceBuilder().Build();

        var result = await sut.CreateAsync(
            Edit("nobody", Array.Empty<string>()), "Starting#1");

        result.Error.Code.Should().Be("SEC-026");
    }

    [Fact]
    public async Task An_unknown_role_is_refused()
    {
        var sut = new UserServiceBuilder().Build();

        var result = await sut.CreateAsync(
            Edit("odd", new[] { "Chief Wizard" }), "Starting#1");

        result.Error.Code.Should().Be("SEC-027");
    }

    /// <summary>
    /// A pharmacist's registration number is printed on every label they
    /// sign, so the record cannot be missing it.
    /// </summary>
    [Fact]
    public async Task A_pharmacist_needs_a_registration_number()
    {
        var sut = new UserServiceBuilder().Build();

        var withoutNumber = await sut.CreateAsync(
            Edit("pharm", new[] { Role.Standard.Pharmacist }), "Starting#1");

        withoutNumber.Error.Code.Should().Be("SEC-029");

        var withNumber = await sut.CreateAsync(
            Edit("pharm", new[] { Role.Standard.Pharmacist },
                 registration: "P12345"), "Starting#1");

        withNumber.IsSuccess.Should().BeTrue(withNumber.Error.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task A_discount_limit_outside_nought_to_a_hundred_is_refused(
        int limit)
    {
        var sut = new UserServiceBuilder().Build();

        var result = await sut.CreateAsync(
            Edit("odd", CashierOnly, limit: limit), "Starting#1");

        result.Error.Code.Should().Be("SEC-028");
    }

    [Fact]
    public async Task A_short_starting_password_is_refused()
    {
        var sut = new UserServiceBuilder().Build();

        var result = await sut.CreateAsync(Edit("short", CashierOnly),
                                           "abc");

        result.Error.Code.Should().Be("SEC-004");
    }

    /// <summary>
    /// Removing your own Administrator role locks you out of the screen
    /// you are standing on, and possibly out of the system.
    /// </summary>
    [Fact]
    public async Task You_cannot_remove_your_own_administrator_role()
    {
        var builder = new UserServiceBuilder();

        var me = User.Create("me", "Me", "hash");
        me.AssignRole(Role.Standard.Administrator);
        await builder.Users.AddAsync(me);
        builder.SignedInAs(me.Id);

        var result = await builder.Build().UpdateAsync(me.Id,
            Edit("me", new[] { Role.Standard.Manager }));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SEC-022");
        me.IsInRole(Role.Standard.Administrator).Should().BeTrue();
    }

    [Fact]
    public async Task You_cannot_deactivate_the_account_you_are_using()
    {
        var builder = new UserServiceBuilder();

        var me = User.Create("me", "Me", "hash");
        me.AssignRole(Role.Standard.Administrator);
        await builder.Users.AddAsync(me);
        builder.SignedInAs(me.Id);

        var result = await builder.Build().SetActiveAsync(me.Id, false);

        result.Error.Code.Should().Be("SEC-023");
        me.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Updating_roles_removes_the_ones_that_were_unticked()
    {
        var builder = new UserServiceBuilder();

        var user = User.Create("multi", "Multi", "hash");
        user.AssignRole(Role.Standard.Cashier);
        user.AssignRole(Role.Standard.Manager);
        await builder.Users.AddAsync(user);

        var result = await builder.Build().UpdateAsync(user.Id,
            Edit("multi", CashierOnly));

        result.IsSuccess.Should().BeTrue(result.Error.Message);
        user.Roles.Should().BeEquivalentTo(CashierOnly);
    }

    /// <summary>
    /// A reset leaves the account needing a change, and unlocks it,
    /// because "locked out" is usually why the reset was asked for.
    /// </summary>
    [Fact]
    public async Task A_reset_forces_a_change_unlocks_and_is_audited()
    {
        var builder = new UserServiceBuilder();

        var user = User.Create("locked", "Locked", "hash");
        user.AssignRole(Role.Standard.Cashier);

        for (var i = 0; i < 5; i++) user.RecordFailedLogin();
        user.IsLockedOut.Should().BeTrue();

        await builder.Users.AddAsync(user);

        var result = await builder.Build()
            .ResetPasswordAsync(user.Id, "Replaced#1");

        result.IsSuccess.Should().BeTrue(result.Error.Message);
        user.MustChangePassword.Should().BeTrue();
        user.IsLockedOut.Should().BeFalse();
        user.FailedAttempts.Should().Be(0);

        builder.Audit.Recorded("adm.user.password.reset", "Applied")
               .Should().BeTrue();
    }

    [Fact]
    public async Task Listing_users_needs_the_role_assignment_permission()
    {
        var sut = new UserServiceBuilder()
            .WithPermissions(Array.Empty<string>())
            .Build();

        var act = () => sut.ListAsync();

        await act.Should().ThrowAsync<PermissionDeniedException>();
    }

    [Fact]
    public async Task Deactivated_accounts_are_hidden_unless_asked_for()
    {
        var builder = new UserServiceBuilder();

        var gone = User.Create("gone", "Gone", "hash");
        gone.AssignRole(Role.Standard.Cashier);
        gone.Deactivate();
        await builder.Users.AddAsync(gone);

        var sut = builder.Build();

        (await sut.ListAsync()).Value.Should().BeEmpty();
        (await sut.ListAsync(includeInactive: true)).Value
            .Should().ContainSingle(u => u.Username == "gone");
    }

    private static UserEditDto Edit(string username,
        IReadOnlyList<string> roles, decimal limit = 5m,
        string? registration = null) =>
        new(username, $"Test {username}", roles, limit, registration);
}

internal sealed class UserServiceBuilder
{
    public InMemoryUserRepository Users { get; } = new();
    public FakePasswordHasher Hasher { get; } = new();
    public FakeAuditWriter Audit { get; } = new();
    public FakeUnitOfWork UnitOfWork { get; } = new();
    public FakeClock Clock { get; } = new();

    private FakeCurrentUser _currentUser = new();
    private FakePermissionRepository _permissions = new();

    public UserServiceBuilder SignedInAs(Guid userId)
    {
        _currentUser = new FakeCurrentUser { Id = userId };
        return this;
    }

    public UserServiceBuilder WithPermissions(IEnumerable<string> granted)
    {
        _permissions = new FakePermissionRepository(granted);
        return this;
    }

    public UserService Build() =>
        new(Users, new PermissionService(_currentUser, _permissions, Audit),
            Hasher, Audit, UnitOfWork, _currentUser, Clock);
}
