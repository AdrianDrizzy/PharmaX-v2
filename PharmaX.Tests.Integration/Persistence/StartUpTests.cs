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
    private readonly PharmaXFixture _fixture;

    public StartUpTests(PharmaXFixture fixture) => _fixture = fixture;

    [Fact]
    public void The_migration_leaves_no_pending_changes()
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        db.Database.GetPendingMigrations().Should().BeEmpty();
    }

    /// <remarks>
    /// Seeded by code and only where missing, so running the seeder at
    /// every start-up adds nothing the second time.
    /// </remarks>
    [Fact]
    public async Task The_Zimbabwean_medical_aid_societies_are_seeded_once()
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        var codes = await db.MedicalAidSchemes.Select(s => s.Code)
                                              .ToListAsync();

        codes.Should().Contain(PharmaX.Persistence.Seeding
            .ZimbabweMedicalAidSocieties.All.Select(s => s.Code));

        codes.Should().Contain("CIMAS").And.Contain("PSMAS")
            .And.Contain("FMH");
        codes.Should().OnlyHaveUniqueItems();

        await PharmaXFixture
            .Resolve<PharmaX.Persistence.Seeding.ReferenceDataSeeder>(scope)
            .SeedAsync();

        (await db.MedicalAidSchemes.CountAsync()).Should().Be(codes.Count);
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
    /// <remarks>
    /// Every series, not a sample of them. This asserted two - SALE and
    /// DISP - while the seeder carried its own hardcoded list that had
    /// fallen two behind DocumentSeries.All. CLM and ACC had no block, so
    /// opening a patient account and raising a medical aid claim both
    /// died at "No document numbers left for ACC", naming a series the
    /// operator has never heard of, in the middle of something else. A
    /// test that checks a sample of a list cannot catch the list going
    /// stale, which is the only way this ever goes wrong.
    /// </remarks>
    [Fact]
    public async Task Every_document_series_is_seeded_a_block_for_this_till()
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        var blocks = await db.DocumentNumberBlocks.AsNoTracking()
            .Where(b => b.WorkstationCode == "1")
            .Select(b => b.Series)
            .ToListAsync();

        blocks.Should().BeEquivalentTo(DocumentSeries.All);
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

    /// <remarks>
    /// The owner forgot the password they chose, and the account then
    /// locked: --reset-admin puts it back as installed.
    /// </remarks>
    [Fact]
    public async Task A_lost_administrator_sign_in_resets_to_the_starting_one()
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);
        var hasher = PharmaXFixture.Resolve<IPasswordHasher>(scope);

        var admin = await db.Users.SingleAsync(u => u.Username == "admin");
        admin.ChangeCredential(hasher.Hash("forgotten"), DateTime.UtcNow);
        for (var i = 0; i < 5; i++) admin.RecordFailedLogin();
        admin.Deactivate();
        await db.SaveChangesAsync();

        await PharmaXFixture
            .Resolve<PharmaX.Persistence.Seeding.ReferenceDataSeeder>(scope)
            .ResetAdministratorAsync();

        db.ChangeTracker.Clear();
        admin = await db.Users.AsNoTracking().SingleAsync(u => u.Username == "admin");

        hasher.Verify("ChangeMe#1", admin.Credential).Should().BeTrue();
        admin.MustChangePassword.Should().BeTrue();
        admin.IsLockedOut.Should().BeFalse();
        admin.IsActive.Should().BeTrue();
        admin.Roles.Should().Contain(Role.Standard.Administrator);
    }
}
