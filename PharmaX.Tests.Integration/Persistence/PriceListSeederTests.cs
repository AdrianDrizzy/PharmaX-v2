using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Reporting;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Security;
using PharmaX.Persistence;
using PharmaX.Persistence.Seeding;
using PharmaX.Tests.Integration.Workflows;

namespace PharmaX.Tests.Integration.Persistence;

/// <summary>
/// The pharmacy's price list going into the catalogue, against a real
/// database.
/// </summary>
/// <remarks>
/// Its own fixture: this puts seven hundred products into the database
/// and every other test in a shared one would be counting them.
/// </remarks>
public sealed class PriceListSeederTests : IDisposable
{
    private readonly PharmaXFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task The_whole_price_list_loads()
    {
        var outcome = await SeedAsync();

        // Seven hundred and fifty-two rows in the sheet. Three cannot be
        // sold and are left out: one has no selling price at all, and two
        // are priced at four tenths of a cent, which rounds to nothing at
        // the two decimals money is kept in.
        outcome.Total.Should().Be(752);
        outcome.Added.Should().Be(749);
        outcome.Skipped.Should().Be(3);

        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        var product = await db.Products.AsNoTracking()
            .SingleAsync(p => p.Code == "1454");

        product.Description.Should().Be("IBUPROFEN [PHARMANOVA] 400MG");
        product.RetailPrice.Amount.Should().Be(50.00m);
        product.CostPrice.Amount.Should().Be(0.04m);
        product.PackSize.Should().Be("TAB 1000");
    }

    /// <remarks>
    /// The cautious way round: refusing to sell a prescription over the
    /// counter is recoverable, and selling one is not.
    /// </remarks>
    [Fact]
    public async Task Dispensary_stock_needs_a_pharmacist_and_the_rest_does_not()
    {
        await SeedAsync();

        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        // 1932 is a suture from the dispensary section.
        var dispensary = await db.Products.AsNoTracking()
            .SingleAsync(p => p.Code == "1932");

        dispensary.RequiresPharmacist.Should().BeTrue();

        // 469 is acetone, off the cosmetics shelf.
        var counter = await db.Products.AsNoTracking()
            .SingleAsync(p => p.Code == "469");

        counter.RequiresPharmacist.Should().BeFalse();
    }

    /// <remarks>
    /// It runs on every start-up, so a second run has to be a no-op
    /// rather than seven hundred duplicates.
    /// </remarks>
    [Fact]
    public async Task Running_it_twice_changes_nothing_the_second_time()
    {
        var first = await SeedAsync();
        var second = await SeedAsync();

        second.Added.Should().Be(0);
        second.Updated.Should().Be(0);
        second.Unchanged.Should().Be(first.Added);

        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        db.Products.Count(p => p.Code == "1454").Should().Be(1);
    }

    /// <remarks>
    /// It runs at every start-up, so a list that repriced existing
    /// products would undo every price the pharmacy set on the product
    /// form the next time the till was switched on.
    /// </remarks>
    [Fact]
    public async Task A_price_set_by_the_pharmacy_survives_a_restart()
    {
        await SeedAsync();

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);
            var product = await db.Products.SingleAsync(p => p.Code == "1454");

            product.ChangeRetailPrice(Domain.Primitives.Money.Of(1m));
            product.ChangeCostPrice(Domain.Primitives.Money.Of(0.5m));
            product.Rename("IBUPROFEN 400MG (house)");
            await db.SaveChangesAsync();
        }

        var outcome = await SeedAsync();

        outcome.Updated.Should().Be(0);

        using var check = _fixture.NewScope();
        var after = PharmaXFixture.Resolve<PharmaXDbContext>(check);

        var ibuprofen = await after.Products.AsNoTracking()
            .SingleAsync(p => p.Code == "1454");

        ibuprofen.RetailPrice.Amount.Should().Be(1m);
        ibuprofen.CostPrice.Amount.Should().Be(0.5m);
        ibuprofen.Description.Should().Be("IBUPROFEN 400MG (house)");
    }

    /// <remarks>
    /// A price list says what things cost, not what is on the shelf. A
    /// seeder that reset quantities every morning would wipe out the
    /// counting the staff had just done.
    /// </remarks>
    [Fact]
    public async Task Reseeding_leaves_stock_alone()
    {
        await SeedAsync();

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);
            var product = await db.Products.SingleAsync(p => p.Code == "1454");

            product.SetQuantityOnHand(250m);
            await db.SaveChangesAsync();
        }

        await SeedAsync();

        using var check = _fixture.NewScope();
        var after = PharmaXFixture.Resolve<PharmaXDbContext>(check);

        (await after.Products.AsNoTracking()
            .SingleAsync(p => p.Code == "1454"))
            .QuantityOnHand.Should().Be(250m);
    }

    /// <remarks>
    /// The list has always said how many tablets a pack holds - "TAB
    /// 100" - and none of it reached the product, so a script for thirty
    /// tablets was read as thirty packs (OCT-DEF-01).
    /// </remarks>
    [Fact]
    public async Task Each_pack_knows_how_many_units_it_holds()
    {
        await SeedAsync();

        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        async Task<Product> ByCode(string code) =>
            await db.Products.AsNoTracking().SingleAsync(p => p.Code == code);

        (await ByCode("1850")).UnitsPerPack.Should().Be(100m, "LOSARTAN, TAB 100");
        (await ByCode("1748")).UnitsPerPack.Should().Be(30m, "ATORVASTATIN, TAB 30");
        (await ByCode("1454")).UnitsPerPack.Should().Be(1000m, "IBUPROFEN, TAB 1000");

        // A single - a bottle of acetone, one suture - is handed over as
        // itself.
        (await ByCode("469")).UnitsPerPack.Should().BeNull();
        (await ByCode("1932")).UnitsPerPack.Should().BeNull();
    }

    /// <remarks>
    /// A dispensing label has to carry the batch and its expiry, and an
    /// untracked line has neither to print (OCT-DEF-02).
    /// </remarks>
    [Fact]
    public async Task Dispensary_lines_are_batch_tracked_and_counter_lines_are_not()
    {
        await SeedAsync();

        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        var products = await db.Products.AsNoTracking().ToListAsync();

        products.Where(p => p.RequiresPharmacist)
            .Should().NotBeEmpty()
            .And.OnlyContain(p => p.IsBatchTracked);

        products.Where(p => !p.RequiresPharmacist)
            .Should().OnlyContain(p => !p.IsBatchTracked,
                "counter stock with no batch has to stay sellable");
    }

    /// <remarks>
    /// The list runs at every start-up, so a database it loaded before it
    /// knew about pack sizes and batch tracking is put right the next time
    /// - except what the pharmacy has already set or traded.
    /// </remarks>
    [Fact]
    public async Task A_catalogue_loaded_earlier_is_brought_up_to_date_but_not_overruled()
    {
        await SeedAsync();

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

            // As the list used to leave them: no units per pack, untracked.
            var untouched = await db.Products.SingleAsync(p => p.Code == "1850");
            untouched.SetUnitsPerPack(null);
            untouched.SetBatchTracked(false);

            // Traded since, with stock on no batch.
            var traded = await db.Products.SingleAsync(p => p.Code == "1748");
            traded.SetUnitsPerPack(null);
            traded.SetBatchTracked(false);
            traded.SetQuantityOnHand(5m);
            db.StockMovements.Add(StockMovement.Record(traded.Id, null,
                MovementType.Adjustment, 5m, 5m, DateTime.UtcNow,
                reasonCode: "COUNT"));

            // Set by hand on the product form.
            var chosen = await db.Products.SingleAsync(p => p.Code == "1454");
            chosen.SetUnitsPerPack(500m);

            await db.SaveChangesAsync();
        }

        var outcome = await SeedAsync();

        outcome.StockWithoutBatch.Should().Be(1,
            "1748 holds stock with no batch to put it on");

        using var check = _fixture.NewScope();
        var after = PharmaXFixture.Resolve<PharmaXDbContext>(check);

        var losartan = await after.Products.AsNoTracking().SingleAsync(p => p.Code == "1850");
        losartan.UnitsPerPack.Should().Be(100m);
        losartan.IsBatchTracked.Should().BeTrue("nothing has ever moved on it");

        var atorvastatin = await after.Products.AsNoTracking().SingleAsync(p => p.Code == "1748");
        atorvastatin.UnitsPerPack.Should().Be(30m, "a pack size is still the list's to give");
        atorvastatin.IsBatchTracked.Should().BeFalse(
            "a batch number and expiry for stock already on the shelf cannot be made up");
        atorvastatin.QuantityOnHand.Should().Be(5m);

        (await after.Products.AsNoTracking().SingleAsync(p => p.Code == "1454"))
            .UnitsPerPack.Should().Be(500m, "the pharmacy's own figure stands");
    }

    /// <remarks>
    /// A cost of one tablet against the price of a thousand reads as a
    /// 99.9% margin on every report (OCT-OBS-07). The import counts them
    /// and the report lists them, by the same rule.
    /// </remarks>
    [Fact]
    public async Task Costs_that_do_not_look_like_a_packs_cost_are_counted_and_listed()
    {
        var outcome = await SeedAsync();

        int expected;

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);
            expected = (await db.Products.AsNoTracking().ToListAsync())
                .Count(p => p.CostNeedsChecking);
        }

        expected.Should().BeGreaterThan(100, "the list has over a hundred of them");
        outcome.CostsToCheck.Should().Be(expected);

        var steps = new WorkflowSteps(_fixture);
        await steps.SignInAsync("pls.mgr1", Role.Standard.Manager);

        using var run = _fixture.NewScope();

        var today = DateOnly.FromDateTime(DateTime.Today);
        var report = await PharmaXFixture.Resolve<IReportRunner>(run)
            .RunAsync(new ReportRequest("inv.cost.check", "1", today, today,
                new Dictionary<string, string?>()));

        report.Title.Should().Be("Cost Prices to Check");
        report.Rows.Should().HaveCount(expected);

        var salbutamol = report.Rows.Single(r => Equals(r["Code"], "204"));
        salbutamol["Cost"].Should().Be(0.01m);
        salbutamol["Price"].Should().Be(41.67m);
        ((string)salbutamol["Why"]!).Should().StartWith("Cost under a tenth");

        ((decimal)report.Rows[0]["Margin"]!).Should()
            .BeGreaterOrEqualTo((decimal)report.Rows[^1]["Margin"]!,
                "highest margin first");
    }

    private async Task<PriceListSeeder.Outcome> SeedAsync()
    {
        using var scope = _fixture.NewScope();
        return await PharmaXFixture.Resolve<PriceListSeeder>(scope)
                                   .SeedAsync();
    }
}
