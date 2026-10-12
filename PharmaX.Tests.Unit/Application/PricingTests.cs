using FluentAssertions;
using PharmaX.Application.Inventory;

namespace PharmaX.Tests.Unit.Application;

public sealed class PricingTests
{
    [Theory]
    // A dearer delivery keeps the margin: cost 10 -> 12, retail 15 -> 18.
    [InlineData(15, 10, 12, 0, 0.05, 18)]
    // Rounded up to the step.
    [InlineData(15, 10, 11.11, 0, 0.05, 16.70)]
    // No price yet: the default markup on cost.
    [InlineData(0, 0, 10, 40, 0.10, 14)]
    public void A_price_goes_up_with_its_cost(decimal retail, decimal oldCost,
        decimal newCost, decimal markup, decimal step, decimal expected) =>
        Pricing.NewRetail(retail, oldCost, newCost, markup, step).Should().Be(expected);

    [Theory]
    [InlineData(15, 10, 9, 40)]   // cheaper delivery: left alone
    [InlineData(15, 10, 10, 40)]  // same cost
    [InlineData(0, 0, 10, 0)]     // no price and no markup set
    public void A_price_is_never_lowered_or_guessed(decimal retail, decimal oldCost,
        decimal newCost, decimal markup) =>
        Pricing.NewRetail(retail, oldCost, newCost, markup, 0.05m).Should().BeNull();
}
