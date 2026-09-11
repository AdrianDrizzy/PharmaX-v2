using FluentAssertions;
using PharmaX.Domain.Primitives;

namespace PharmaX.Tests.Unit.Domain;

public class MoneyTests
{
    [Theory]
    [InlineData(10.005, 10.01)]
    [InlineData(10.004, 10.00)]
    [InlineData(-10.005, -10.01)]
    [InlineData(2.675, 2.68)]
    public void Rounds_half_away_from_zero(decimal input, decimal expected) =>
        Money.Of(input).Amount.Should().Be(expected);

    [Fact]
    public void Combining_two_currencies_is_refused()
    {
        var act = () => Money.Of(10m, "ZAR") + Money.Of(10m, "USD");

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*ZAR*USD*");
    }

    [Fact]
    public void A_currency_code_must_be_three_letters()
    {
        var act = () => Money.Of(10m, "RAND");

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// A Money never holds a fraction of a cent: Of rounds on the way in,
    /// so 0.335 is 0.34 before it is ever multiplied. That is the design,
    /// and it is why a unit price is a Money and a VAT rate is not.
    /// </summary>
    [Fact]
    public void An_amount_is_rounded_on_construction_not_on_use()
    {
        Money.Of(0.335m).Amount.Should().Be(0.34m);
        (Money.Of(0.335m) * 3m).Amount.Should().Be(1.02m);
    }

    /// <summary>
    /// Where sub-cent precision has to survive, the rate stays a decimal
    /// and only the result becomes a Money. This is the shape every
    /// calculation in Pricing uses.
    /// </summary>
    [Fact]
    public void A_sub_cent_rate_is_applied_before_rounding() =>
        (Money.Of(100m) * 0.0335m).Amount.Should().Be(3.35m);

    [Fact]
    public void Percentage_of_an_amount_is_rounded_to_the_minor_unit() =>
        Money.Of(19.99m).Percentage(15m).Amount.Should().Be(3.00m);

    [Fact]
    public void Zero_is_neither_negative_nor_non_zero()
    {
        Money.Zero().IsZero.Should().BeTrue();
        Money.Zero().IsNegative.Should().BeFalse();
    }

    [Fact]
    public void Currency_is_normalised_to_upper_case() =>
        Money.Of(1m, "zar").Currency.Should().Be("ZAR");
}
