using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Abstractions;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Primitives;
using PharmaX.Persistence;

namespace PharmaX.Tests.Integration.Persistence;

/// <summary>
/// Changing the currency a site trades in, against a real database.
/// </summary>
/// <remarks>
/// Its own fixture rather than the shared one: this walks Money's static
/// default currency, and leaving that pointing somewhere else would
/// quietly change what every other test in a shared fixture asserts.
/// The default is restored before the test returns either way.
/// </remarks>
public sealed class CurrencyTests : IDisposable
{
    private readonly PharmaXFixture _fixture = new();
    private readonly string _original = Money.DefaultCurrency;

    public void Dispose()
    {
        Money.UseCurrency(_original);
        _fixture.Dispose();
    }

    [Theory]
    [InlineData("GBP")]
    [InlineData("ZWG")]
    public async Task Changing_currency_gives_the_till_the_right_drawer(
        string code)
    {
        using var scope = _fixture.NewScope();
        var changer = PharmaXFixture.Resolve<ICurrencyChanger>(scope);
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        var currency = Currencies.ByCode(code);
        currency.Code.Should().Be(code, "the currency is in the catalogue");

        await changer.ApplyAsync(currency);

        var drawer = await db.Denominations.AsNoTracking()
            .Where(d => d.Currency == code)
            .OrderBy(d => d.SortOrder)
            .Select(d => d.Key)
            .ToListAsync();

        drawer.Should().Equal(currency.Denominations.Select(d => d.Key));

        Money.DefaultCurrency.Should().Be(code);
    }

    /// <remarks>
    /// The reason the relabel exists: Money refuses to add two
    /// currencies together, so an amount left under the old code turns
    /// the first cash-up that touches it into an exception.
    /// </remarks>
    [Fact]
    public async Task Changing_currency_relabels_amounts_already_stored()
    {
        Guid productId;

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

            var product = Product.Create("CURR0001", "Priced in the old one",
                                         Money.Of(42m, "ZAR"));

            product.ChangeCostPrice(Money.Of(21m, "ZAR"));

            db.Products.Add(product);
            await db.SaveChangesAsync();
            productId = product.Id;
        }

        using (var scope = _fixture.NewScope())
        {
            await PharmaXFixture.Resolve<ICurrencyChanger>(scope)
                .ApplyAsync(Currencies.Eur);
        }

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

            var product = await db.Products.AsNoTracking()
                .SingleAsync(p => p.Id == productId);

            product.RetailPrice.Currency.Should().Be("EUR");
            product.CostPrice.Currency.Should().Be("EUR");

            // A relabel, not a conversion. Nobody's figures moved.
            product.RetailPrice.Amount.Should().Be(42m);
            product.CostPrice.Amount.Should().Be(21m);
        }
    }

    /// <remarks>
    /// A denomination's currency says which drawer a note belongs to,
    /// not what an amount is worth. Relabelling those would leave the
    /// till counting rand notes under a euro heading.
    /// </remarks>
    [Fact]
    public async Task Changing_currency_leaves_the_old_drawer_alone()
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        var before = await db.Denominations.AsNoTracking()
            .CountAsync(d => d.Currency == _original);

        before.Should().BeGreaterThan(0);

        await PharmaXFixture.Resolve<ICurrencyChanger>(scope)
            .ApplyAsync(Currencies.Kes);

        var after = await db.Denominations.AsNoTracking()
            .CountAsync(d => d.Currency == _original);

        after.Should().Be(before);
    }
}
