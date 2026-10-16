using FluentAssertions;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Security;
using PharmaX.Domain.Security;
using PharmaX.Tests.Unit.Fakes;

namespace PharmaX.Tests.Unit.Application;

public class AuthenticationServiceTests
{
    [Fact]
    public async Task A_good_credential_signs_the_user_in()
    {
        var builder = new AuthServiceBuilder();
        var sut = builder.Build();

        var result = await sut.AuthenticateAsync("cashier", "Correct#1");

        result.IsSuccess.Should().BeTrue();
        result.Value.Username.Should().Be("cashier");
        builder.Session.Id.Should().Be(result.Value.Id);
        builder.Audit.Recorded("auth.login", "Applied").Should().BeTrue();
    }

    /// <summary>
    /// SEC-009: the same message either way, so the form cannot be used
    /// to work out which usernames exist.
    /// </summary>
    [Theory]
    [InlineData("cashier", "Wrong#1")]
    [InlineData("nobody", "Correct#1")]
    public async Task A_bad_username_and_a_bad_password_read_the_same(
        string username, string password)
    {
        var sut = new AuthServiceBuilder().Build();

        var result = await sut.AuthenticateAsync(username, password);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SEC-001");
        result.Error.Message.Should().Be("Username or password is incorrect.");
    }

    [Fact]
    public async Task A_failed_login_is_audited_and_counted()
    {
        var builder = new AuthServiceBuilder();
        var sut = builder.Build();

        await sut.AuthenticateAsync("cashier", "Wrong#1");

        builder.User.FailedAttempts.Should().Be(1);
        builder.Audit.Recorded("auth.login", "Refused").Should().BeTrue();
    }

    /// <summary>
    /// SEC-008: five consecutive failures lock the account, and the lock
    /// is on the user rather than in memory so it survives a restart.
    /// </summary>
    [Fact]
    public async Task Five_failures_lock_the_account()
    {
        var builder = new AuthServiceBuilder();
        var sut = builder.Build();

        for (var i = 0; i < 5; i++)
            await sut.AuthenticateAsync("cashier", "Wrong#1");

        builder.User.IsLockedOut.Should().BeTrue();

        var result = await sut.AuthenticateAsync("cashier", "Correct#1");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SEC-008");
    }

    [Fact]
    public async Task A_good_login_clears_the_failure_count()
    {
        var builder = new AuthServiceBuilder();
        var sut = builder.Build();

        await sut.AuthenticateAsync("cashier", "Wrong#1");
        await sut.AuthenticateAsync("cashier", "Correct#1");

        builder.User.FailedAttempts.Should().Be(0);
    }

    [Fact]
    public async Task An_inactive_account_cannot_sign_in()
    {
        var builder = new AuthServiceBuilder();
        builder.User.Deactivate();

        var result = await builder.Build()
            .AuthenticateAsync("cashier", "Correct#1");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SEC-003");
    }

    /// <summary>
    /// The regression this file exists for. A rehash under stronger
    /// parameters is not the user choosing a new password, so a pending
    /// must-change flag has to survive it - otherwise raising the
    /// iteration count in a future release would silently release every
    /// stale seeded account into live trading (Section 30.3).
    /// </summary>
    [Fact]
    public async Task An_internal_rehash_does_not_clear_the_must_change_flag()
    {
        var builder = new AuthServiceBuilder(mustChangePassword: true);
        builder.Hasher.RehashEverything = true;

        var before = builder.User.Credential;

        var result = await builder.Build()
            .AuthenticateAsync("cashier", "Correct#1");

        result.IsSuccess.Should().BeTrue();
        result.Value.MustChangePassword.Should().BeTrue();

        builder.User.MustChangePassword.Should().BeTrue();
        builder.User.Credential.Should().NotBe(before,
            "the hash should have been upgraded");
        builder.User.PasswordChangedAtUtc.Should().BeNull(
            "the password itself did not change, so its age has not reset");
    }

    [Fact]
    public async Task Choosing_a_new_password_does_clear_the_flag()
    {
        var builder = new AuthServiceBuilder(mustChangePassword: true);
        var sut = builder.Build();

        var result = await sut.ChangePasswordAsync(
            "cashier", "Correct#1", "BrandNew#2");

        result.IsSuccess.Should().BeTrue();
        builder.User.MustChangePassword.Should().BeFalse();
        builder.User.PasswordChangedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task A_new_password_must_differ_and_be_long_enough()
    {
        var sut = new AuthServiceBuilder().Build();

        (await sut.ChangePasswordAsync("cashier", "Correct#1", "abc"))
            .Error.Code.Should().Be("SEC-004");

        (await sut.ChangePasswordAsync("cashier", "Correct#1", "Correct#1"))
            .Error.Code.Should().Be("SEC-005");
    }

    /// <summary>
    /// Verifying a second person at the keyboard must not sign the first
    /// one out of their own session.
    /// </summary>
    [Fact]
    public async Task Authorising_an_override_leaves_the_session_alone()
    {
        var builder = new AuthServiceBuilder();
        var sut = builder.Build();

        await sut.AuthenticateAsync("cashier", "Correct#1");
        var signedIn = builder.Session.Id;

        var result = await sut.AuthoriseAsync("cashier", "Correct#1",
            PermissionCode.SaleDiscountAboveLimit);

        result.IsSuccess.Should().BeTrue();
        builder.Session.Id.Should().Be(signedIn);
    }

    /// <summary>
    /// SEC-035: an attempted override the authoriser could not grant is
    /// exactly the pattern a manager needs to see.
    /// </summary>
    [Fact]
    public async Task An_authoriser_without_the_permission_is_refused_and_audited()
    {
        var builder = new AuthServiceBuilder()
            .WithPermissions(Array.Empty<string>());

        var result = await builder.Build().AuthoriseAsync(
            "cashier", "Correct#1", PermissionCode.SaleDiscountAboveLimit);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SEC-010");

        builder.Audit
            .Recorded(PermissionCode.SaleDiscountAboveLimit, "Refused")
            .Should().BeTrue();
    }
}

/// <summary>
/// Assembles an AuthenticationService around one known user.
/// </summary>
internal sealed class AuthServiceBuilder
{
    public FakePasswordHasher Hasher { get; } = new();
    public FakeSessionContext Session { get; } = new();
    public FakeAuditWriter Audit { get; } = new();
    public FakeUnitOfWork UnitOfWork { get; } = new();
    public FakeClock Clock { get; } = new();
    public User User { get; }

    private FakePermissionRepository _permissions = new();

    public AuthServiceBuilder(bool mustChangePassword = false)
    {
        // The same hasher instance the service will use, so an upgrade
        // produces a visibly different hash rather than the same string.
        User = PharmaX.Domain.Security.User.Create("cashier", "Test Cashier",
            Hasher.Hash("Correct#1"), mustChangePassword);

        User.SetDiscountLimit(5m);
    }

    public AuthServiceBuilder WithPermissions(IEnumerable<string> granted)
    {
        _permissions = new FakePermissionRepository(granted);
        return this;
    }

    public AuthenticationService Build() =>
        new(new InMemoryUserRepository(User), _permissions, Hasher, Session,
            Audit, UnitOfWork, Clock);
}
