using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmaX.Persistence;
using PharmaX.Persistence.Seeding;

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
    /// The point of re-running it: next month's list is the same file
    /// with different numbers in it.
    /// </remarks>
    [Fact]
    public async Task A_price_that_has_moved_is_brought_up_to_date()
    {
        await SeedAsync();

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);
            var product = await db.Products.SingleAsync(p => p.Code == "1454");

            product.ChangeRetailPrice(Domain.Primitives.Money.Of(1m));
            await db.SaveChangesAsync();
        }

        var outcome = await SeedAsync();

        outcome.Updated.Should().Be(1);

        using var check = _fixture.NewScope();
        var after = PharmaXFixture.Resolve<PharmaXDbContext>(check);

        (await after.Products.AsNoTracking()
            .SingleAsync(p => p.Code == "1454"))
            .RetailPrice.Amount.Should().Be(50.00m);
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

    private async Task<PriceListSeeder.Outcome> SeedAsync()
    {
        using var scope = _fixture.NewScope();
        return await PharmaXFixture.Resolve<PriceListSeeder>(scope)
                                   .SeedAsync();
    }
}
