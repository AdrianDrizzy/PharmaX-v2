using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Abstractions;
using PharmaX.Domain.Security;
using PharmaX.Domain.Primitives;
using PharmaX.Persistence;

namespace PharmaX.Tests.Integration.Persistence;

/// <summary>
/// What Section 10.2 promises at the end of Part II: an application that
/// starts, authenticates locally, and has a database it can trade
/// against. Checked here rather than by pressing F5, so a regression is
/// caught by the build.
/// </summary>
public class StartUpTests : IClassFixture<PharmaXFixture>
{
    private static readonly string[] SeededSeries = { "SALE", "DISP" };

    private readonly PharmaXFixture _fixture;

    public StartUpTests(PharmaXFixture fixture) => _fixture = fixture;

    [Fact]
    public void The_migration_leaves_no_pending_changes()
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        db.Database.GetPendingMigrations().Should().BeEmpty();
    }

    [Fact]
    public async Task Every_permission_and_role_is_seeded()
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        var permissions = await db.Permissions.Select(p => p.Code)
                                              .ToListAsync();

        permissions.Should().BeEquivalentTo(PermissionCode.All);

        var roles = await db.Roles.Select(r => r.Name).ToListAsync();
        roles.Should().BeEquivalentTo(Role.Standard.All);
    }

    /// <summary>
    /// The mapping bug this catches is not theoretical: a role whose
    /// permission codes do not persist grants nothing after a restart,
    /// and every screen would quietly refuse.
    /// </summary>
    [Fact]
    public async Task A_seeded_role_keeps_its_permissions_across_a_reload()
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        var administrator = await db.Roles.AsNoTracking()
            .SingleAsync(r => r.Name == Role.Standard.Administrator);

        administrator.PermissionCodes.Should()
            .BeEquivalentTo(PermissionCode.All);

        var cashier = await db.Roles.AsNoTracking()
            .SingleAsync(r => r.Name == Role.Standard.Cashier);

        cashier.PermissionCodes.Should()
            .Contain(PermissionCode.SaleCreate)
            .And.NotContain(PermissionCode.SettingsChange);
    }

    [Fact]
    public async Task The_administrator_is_seeded_and_must_change_its_password()
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        var admin = await db.Users.AsNoTracking()
            .SingleAsync(u => u.Username == "admin");

        admin.MustChangePassword.Should().BeTrue();
        admin.Roles.Should().Contain(Role.Standard.Administrator);

        // The credential is a hash, never the password.
        admin.Credential.Should().NotBe("ChangeMe#1")
             .And.StartWith("pbkdf2-sha256$");
    }

    [Fact]
    public async Task The_seeded_credential_verifies_and_nothing_else_does()
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);
        var hasher = PharmaXFixture.Resolve<IPasswordHasher>(scope);

        var admin = await db.Users.AsNoTracking()
            .SingleAsync(u => u.Username == "admin");

        hasher.Verify("ChangeMe#1", admin.Credential).Should().BeTrue();
        hasher.Verify("changeme#1", admin.Credential).Should().BeFalse();
        hasher.Verify("", admin.Credential).Should().BeFalse();
    }

    /// <remarks>
    /// Asserted against the currency this installation trades in rather
    /// than against a literal "R200": the drawer is seeded from the
    /// currency, so a literal here would only record which currency the
    /// test run happened to be configured for.
    /// </remarks>
    [Fact]
    public async Task The_denomination_list_is_seeded_largest_first()
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        var expected = Currencies.ByCode(Money.DefaultCurrency);

        var denominations = await db.Denominations.AsNoTracking()
            .Where(d => d.Currency == expected.Code)
            .OrderBy(d => d.SortOrder)
            .ToListAsync();

        denominations.Should().HaveCountGreaterThan(5);

        denominations[0].Key.Should()
            .Be(expected.Denominations[0].Key);

        denominations.Should().BeInDescendingOrder(d => d.Value);
    }

    /// <summary>
    /// NET-021: a brand-new workstation can trade before it has ever
    /// reached the host, which needs a document number block on disk.
    /// </summary>
    [Fact]
    public async Task The_first_number_block_is_seeded_for_this_till()
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        var blocks = await db.DocumentNumberBlocks.AsNoTracking()
            .Where(b => b.WorkstationCode == "1")
            .ToListAsync();

        blocks.Select(b => b.Series).Should().Contain(SeededSeries);
    }

    [Fact]
    public async Task Seeding_twice_adds_nothing_the_second_time()
    {
        int Count(PharmaXDbContext db) =>
            db.Permissions.Count() + db.Roles.Count() + db.Users.Count()
            + db.Denominations.Count() + db.DocumentNumberBlocks.Count();

        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);
        var before = Count(db);

        await PharmaXFixture
            .Resolve<PharmaX.Persistence.Seeding.ReferenceDataSeeder>(scope)
            .SeedAsync();

        Count(db).Should().Be(before);
    }
}
