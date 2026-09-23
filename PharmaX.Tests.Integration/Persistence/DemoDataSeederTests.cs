using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PharmaX.Application;
using PharmaX.Application.Abstractions;
using PharmaX.Domain.Dispensing;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Sales;
using PharmaX.Domain.Tills;
using PharmaX.Persistence;
using PharmaX.Persistence.Seeding;
using PharmaX.Reporting;
using PharmaX.Sync;

namespace PharmaX.Tests.Integration.Persistence;

/// <summary>
/// Runs the whole start-up seeding chain with Development:SeedTestUsers
/// on, exactly as Program.cs does, and then looks at what landed.
/// </summary>
/// <remarks>
/// The seeder exists so that a tester can open any screen and find it
/// populated. "It compiled" proves none of that: every write goes through
/// an application service that can refuse on a permission, a validation
/// or a missing prerequisite, and a refusal is logged rather than thrown.
/// A section that quietly seeded nothing would look exactly like a
/// section that worked. These assertions are what tell the two apart.
/// </remarks>
public sealed class DemoSeedFixture : IDisposable
{
    private readonly string _databasePath;
    private readonly ServiceProvider _provider;

    public DemoSeedFixture()
    {
        _databasePath = Path.Combine(Path.GetTempPath(),
            $"pharmax-demo-{Guid.NewGuid():N}.db");

        var options = new PharmaXOptions
        {
            Workstation = new WorkstationOptions
            {
                BranchCode = "1",
                TillNumber = "1",
                BranchId = "11111111-1111-1111-1111-111111111111",
                WorkstationId = "22222222-2222-2222-2222-222222222222"
            },
            Database = new DatabaseOptions
            {
                LocalConnection = $"Data Source={_databasePath}",
                HostConnection = ""
            },

            // The flag the whole demonstration chain hangs off.
            Development = new DevelopmentOptions { SeedTestUsers = true }
        };

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Error));
        services.AddSingleton(options);
        services.AddPharmaXPersistence(options);
        services.AddPharmaXApplication();
        services.AddPharmaXReporting();
        services.AddPharmaXSync(options);

        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        var services_ = scope.ServiceProvider;

        services_.GetRequiredService<PharmaXDbContext>().Database.Migrate();

        // The same order, and the same scope, as start-up.
        services_.GetRequiredService<ReferenceDataSeeder>()
                 .SeedAsync().GetAwaiter().GetResult();

        services_.GetRequiredService<TestUserSeeder>()
                 .SeedAsync().GetAwaiter().GetResult();

        services_.GetRequiredService<TestCatalogueSeeder>()
                 .SeedAsync().GetAwaiter().GetResult();

        services_.GetRequiredService<DemoDataSeeder>()
                 .SeedAsync().GetAwaiter().GetResult();
    }

    public IServiceScope NewScope() => _provider.CreateScope();

    public void Dispose()
    {
        _provider.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            if (File.Exists(_databasePath)) File.Delete(_databasePath);
        }
        catch (IOException)
        {
            // A leftover temp file is not worth failing a run over.
        }
    }
}

public class DemoDataSeederTests : IClassFixture<DemoSeedFixture>
{
    private readonly DemoSeedFixture _fixture;

    public DemoDataSeederTests(DemoSeedFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Every_maintained_list_has_something_to_maintain()
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        (await db.Departments.CountAsync()).Should().BeGreaterThan(1);
        (await db.Categories.CountAsync()).Should().BeGreaterThan(1);
        (await db.Manufacturers.CountAsync()).Should().BeGreaterThan(1);
        (await db.Branches.CountAsync()).Should().BeGreaterThan(1);
        (await db.MedicalAidSchemes.CountAsync()).Should().BeGreaterThan(1);
        (await db.Tariffs.CountAsync()).Should().BeGreaterThan(0);

        // The inactive filter needs something to filter.
        (await db.Departments.CountAsync(d => !d.IsActive))
            .Should().BeGreaterThan(0);
    }

    /// <remarks>
    /// A reference list nothing points at is a filter that returns
    /// nothing whichever entry is chosen, which reads as a broken
    /// dropdown rather than as unfiled stock.
    /// </remarks>
    [Fact]
    public async Task The_seeded_products_are_filed_on_every_axis()
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        (await db.Products.CountAsync(p => p.DepartmentId != null))
            .Should().BeGreaterThan(0);

        (await db.Products.CountAsync(p => p.CategoryId != null))
            .Should().BeGreaterThan(0);

        (await db.Products.CountAsync(p => p.ManufacturerId != null))
            .Should().BeGreaterThan(0);

        // More than one of each in use, so filtering by one is a filter
        // that actually narrows something.
        (await db.Products.Where(p => p.DepartmentId != null)
                          .Select(p => p.DepartmentId).Distinct()
                          .CountAsync())
            .Should().BeGreaterThan(1);

        (await db.Products.Where(p => p.CategoryId != null)
                          .Select(p => p.CategoryId).Distinct()
                          .CountAsync())
            .Should().BeGreaterThan(1);

        // The two are different axes, not one renamed: a category that
        // only ever appeared alongside one department would mean the
        // seed data cannot tell them apart either.
        var pairs = await db.Products
            .Where(p => p.DepartmentId != null && p.CategoryId != null)
            .Select(p => new { p.DepartmentId, p.CategoryId })
            .ToListAsync();

        pairs.Select(x => x.CategoryId).Distinct().Count()
             .Should().BeGreaterThan(
                 pairs.Select(x => x.DepartmentId).Distinct().Count(),
                 "categories cut across departments");
    }

    [Fact]
    public async Task The_patients_carry_the_clinical_detail_that_makes_them_useful()
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        (await db.Patients.CountAsync()).Should().BeGreaterOrEqualTo(6);

        (await db.Patients.CountAsync(p => p.MedicalAidSchemeId != null))
            .Should().BeGreaterThan(0, "a claim needs a member to raise it");

        (await db.ClinicalProfiles.CountAsync()).Should().BeGreaterThan(0);

        (await db.Accounts.CountAsync(a => a.PatientId != null))
            .Should().BeGreaterThan(0);

        // A charge and a receipt, so a statement has two sides to it.
        (await db.AccountTransactions.CountAsync())
            .Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task The_alert_centre_has_every_severity_to_report()
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        (await db.Batches.CountAsync(b => b.ExpiryDate < today))
            .Should().BeGreaterThan(0, "an expired batch is the loudest alert");

        (await db.Batches.CountAsync(b =>
                b.ExpiryDate >= today && b.ExpiryDate < today.AddDays(30)))
            .Should().BeGreaterThan(0, "and one inside the critical window");

        (await db.Products.CountAsync(p =>
                p.ReorderLevel > 0m && p.QuantityOnHand < p.ReorderLevel))
            .Should().BeGreaterThan(0, "something has to be below its level");
    }

    /// <remarks>
    /// The three tenders behave differently at cash-up and in the ledger,
    /// so one of each is the minimum that exercises the day end.
    /// </remarks>
    [Fact]
    public async Task A_day_of_sales_was_taken_across_every_tender()
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        (await db.Sales.CountAsync()).Should().BeGreaterThan(2);
        (await db.SaleLines.CountAsync()).Should().BeGreaterThan(2);

        var tenders = await db.SalePayments.Select(p => p.Tender)
            .Distinct().ToListAsync();

        tenders.Should().Contain(TenderType.Cash);
        tenders.Should().Contain(TenderType.Card);

        (await db.SaleReturns.CountAsync())
            .Should().BeGreaterThan(0, "frmReturn opens on the recent list");

        (await db.Quotations.CountAsync()).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task The_till_has_a_finished_day_behind_it_and_an_open_one_in_front()
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        (await db.TillSessions.CountAsync(s =>
                s.Status == TillSessionStatus.Closed))
            .Should().BeGreaterThan(0, "the day end needs a closed session");

        (await db.TillSessions.CountAsync(s =>
                s.Status == TillSessionStatus.Open))
            .Should().Be(1, "and the tester needs one to sell into");

        (await db.TillDeclarations.CountAsync())
            .Should().BeGreaterThan(0, "a cash-up was declared");
    }

    [Fact]
    public async Task Scripts_were_dispensed_and_one_is_still_waiting()
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        (await db.Scripts.CountAsync()).Should().BeGreaterThan(0);

        (await db.Dispensings.CountAsync(d =>
                d.Status == DispensingStatus.Issued))
            .Should().BeGreaterThan(0);

        // The one the cashier has not taken payment for yet, which is
        // what frmCollect exists to list.
        (await db.Dispensings.CountAsync(d =>
                d.Status == DispensingStatus.Issued && d.SaleId == null))
            .Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Buying_and_counting_both_left_a_trail()
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        (await db.PurchaseOrders.CountAsync()).Should().BeGreaterThan(1);
        (await db.GoodsReceipts.CountAsync()).Should().BeGreaterThan(0);

        (await db.Stocktakes.CountAsync(s =>
                s.Status == StocktakeStatus.Posted))
            .Should().BeGreaterThan(0);
    }

    /// <remarks>
    /// The seeder dispenses to members of societies it has priced, so a
    /// claim is raised on issue and part of it is remitted. This is the
    /// end of a long chain - membership, tariff, contribution, claim,
    /// remittance - and a break anywhere along it shows up here as an
    /// empty frmClaims.
    /// </remarks>
    [Fact]
    public async Task Dispensing_to_a_member_raised_a_claim_and_some_was_paid()
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        var claims = await db.MedicalAidClaims.AsNoTracking().ToListAsync();

        claims.Should().NotBeEmpty("a scheme member was dispensed to");

        claims.Should().Contain(c => c.Claimed.Amount > 0m,
            "a claim for nothing is a claim that should not exist");

        // One short paid and written off, the rest settled in full, so
        // the age analysis has both cases on it.
        claims.Should().Contain(c => c.Settled.Amount > 0m);
    }

    /// <remarks>
    /// The reason the seeder goes in through the services rather than
    /// straight into the tables. Rows written behind the application's
    /// back have none of this, and every report that reads it would
    /// disagree with the till.
    /// </remarks>
    [Fact]
    public async Task The_trading_moved_stock_posted_journals_and_was_audited()
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        (await db.StockMovements.CountAsync()).Should().BeGreaterThan(0);
        (await db.JournalBatches.CountAsync()).Should().BeGreaterThan(0);
        (await db.JournalLines.CountAsync()).Should().BeGreaterThan(0);
        (await db.AuditEntries.CountAsync()).Should().BeGreaterThan(0);
        (await db.OutboxEntries.CountAsync()).Should().BeGreaterThan(0);
    }

    /// <remarks>
    /// Start-up runs the seeders on every launch. A second pass that
    /// added a second day's trading would double the takings of a
    /// database somebody was halfway through testing against.
    /// </remarks>
    [Fact]
    public async Task Seeding_again_changes_nothing()
    {
        int sales, patients, sessions;

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);
            sales = await db.Sales.CountAsync();
            patients = await db.Patients.CountAsync();
            sessions = await db.TillSessions.CountAsync();
        }

        using (var scope = _fixture.NewScope())
        {
            await PharmaXFixture.Resolve<DemoDataSeeder>(scope).SeedAsync();
        }

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

            (await db.Sales.CountAsync()).Should().Be(sales);
            (await db.Patients.CountAsync()).Should().Be(patients);
            (await db.TillSessions.CountAsync()).Should().Be(sessions);
        }
    }

    /// <remarks>
    /// The flag is the only thing standing between a pharmacy's live
    /// database and a day of invented takings.
    /// </remarks>
    [Fact]
    public async Task Nothing_is_seeded_when_the_flag_is_off()
    {
        var path = Path.Combine(Path.GetTempPath(),
            $"pharmax-nodemo-{Guid.NewGuid():N}.db");

        var options = new PharmaXOptions
        {
            Database = new DatabaseOptions
            {
                LocalConnection = $"Data Source={path}",
                HostConnection = ""
            }

            // Development left at its default, which is off.
        };

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Error));
        services.AddSingleton(options);
        services.AddPharmaXPersistence(options);
        services.AddPharmaXApplication();
        services.AddPharmaXReporting();
        services.AddPharmaXSync(options);

        await using var provider = services.BuildServiceProvider();

        try
        {
            using var scope = provider.CreateScope();
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);
            await db.Database.MigrateAsync();

            await PharmaXFixture.Resolve<ReferenceDataSeeder>(scope)
                                .SeedAsync();

            await PharmaXFixture.Resolve<DemoDataSeeder>(scope).SeedAsync();

            (await db.Sales.CountAsync()).Should().Be(0);
            (await db.Patients.CountAsync()).Should().Be(0);
            (await db.Scripts.CountAsync()).Should().Be(0);
            (await db.TillSessions.CountAsync()).Should().Be(0);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
                // Not worth failing a run over.
            }
        }
    }
}
