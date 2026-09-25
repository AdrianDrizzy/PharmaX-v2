using System.Globalization;
using FluentAssertions;
using PharmaX.Domain.Primitives;

namespace PharmaX.Tests.Unit.Domain;

public class CurrenciesTests
{
    [Fact]
    public void Zimbabwe_gold_can_be_chosen_by_its_iso_code()
    {
        var zig = Currencies.ByCode("zwg");

        zig.Should().BeSameAs(Currencies.Zwg);
        zig.Name.Should().Be("Zimbabwe Gold");
        zig.Symbol.Should().Be("ZiG");
        Currencies.All.Should().ContainSingle(c => c.Code == "ZWG");
    }

    /// <summary>
    /// The notes and coins in circulation, largest first: the order the
    /// cash-up grid lists them in.
    /// </summary>
    [Fact]
    public void Zimbabwe_gold_counts_the_notes_and_coins_in_circulation()
    {
        Currencies.Zwg.Denominations.Select(d => (d.Key, d.Value, d.IsNote))
            .Should().Equal(
                ("ZiG20", 20m, true), ("ZiG10", 10m, true),
                ("ZiG5", 5m, false), ("ZiG2", 2m, false),
                ("ZiG1", 1m, false));
    }

    /// <summary>
    /// Every figure is formatted against the currency's culture. One the
    /// machine does not know falls back to the invariant culture, which
    /// would still carry the symbol but lose the grouping.
    /// </summary>
    [Fact]
    public void Zimbabwe_gold_formats_against_a_culture_that_exists()
    {
        var culture = CultureInfo.GetCultureInfo(Currencies.Zwg.Culture);

        culture.Name.Should().Be("en-ZW");
        1234.5m.ToString("N2", culture).Should().Be("1,234.50");
    }

    /// <summary>
    /// The currency-change warning shows a figure as the new currency
    /// writes it, not in a decimal comma it may never use.
    /// </summary>
    [Fact]
    public void An_amount_is_written_the_way_the_chosen_currency_writes_it()
    {
        PharmaX.Desktop.Mvp.Format.Amount(1500m, Currencies.Zwg)
            .Should().Be("1,500.00");
        PharmaX.Desktop.Mvp.Format.Amount(100m, Currencies.Usd)
            .Should().Be("100.00");
    }

    /// <summary>
    /// What any entry in the catalogue has to be for Money, the drawer and
    /// the database to accept it.
    /// </summary>
    [Fact]
    public void Every_currency_in_the_catalogue_is_well_formed()
    {
        Currencies.All.Select(c => c.Code).Should().OnlyHaveUniqueItems();

        foreach (var currency in Currencies.All)
        {
            currency.Code.Should().MatchRegex("^[A-Z]{3}$");
            currency.Denominations.Should().NotBeEmpty();
            currency.Denominations.Select(d => d.Key).Should()
                .OnlyHaveUniqueItems()
                .And.OnlyContain(key => key.Length <= 20);
            currency.Denominations.Select(d => d.Value).Should()
                .BeInDescendingOrder().And.OnlyContain(v => v > 0m);
        }
    }
}
