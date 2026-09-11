using FluentAssertions;
using PharmaX.Domain.Pricing;
using PharmaX.Domain.Primitives;

namespace PharmaX.Tests.Unit.Domain;

public class VatCalculatorTests
{
    [Theory]
    [InlineData(115.00, 15, 15.00, 100.00)]
    [InlineData(46.00, 15, 6.00, 40.00)]
    [InlineData(24.00, 15, 3.13, 20.87)]
    [InlineData(180.00, 0, 0.00, 180.00)]
    public void Extracts_inclusive_vat_correctly(
        decimal gross, decimal rate, decimal expectedVat, decimal expectedNet)
    {
        var split = VatCalculator.FromInclusive(Money.Of(gross), rate);

        split.VatAmount.Amount.Should().Be(expectedVat);
        split.NetAmount.Amount.Should().Be(expectedNet);
        split.GrossAmount.Amount.Should().Be(gross);
    }

    /// <summary>
    /// The test that matters. Extraction plus rounding can lose or gain a
    /// cent, and a cent per line across a year of trading is a
    /// reconciliation the bookkeeper cannot close.
    /// </summary>
    [Fact]
    public void Net_plus_vat_always_equals_gross()
    {
        for (decimal gross = 0.01m; gross < 500m; gross += 0.37m)
        {
            var split = VatCalculator.FromInclusive(Money.Of(gross), 15m);

            (split.NetAmount + split.VatAmount).Amount
                .Should().Be(gross, $"gross was {gross}");
        }
    }

    [Fact]
    public void Exclusive_addition_adds_vat_on_top()
    {
        var split = VatCalculator.FromExclusive(Money.Of(100m), 15m);

        split.NetAmount.Amount.Should().Be(100.00m);
        split.VatAmount.Amount.Should().Be(15.00m);
        split.GrossAmount.Amount.Should().Be(115.00m);
    }

    [Fact]
    public void A_negative_rate_is_refused()
    {
        var act = () => VatCalculator.FromInclusive(Money.Of(100m), -1m);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
