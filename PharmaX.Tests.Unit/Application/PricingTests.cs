using FluentAssertions;
using NSubstitute;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Inventory;

namespace PharmaX.Tests.Unit.Application;

public sealed class PricingTests
{
    [Theory]
    // Cost x 1.5, whatever the price was before.
    [InlineData(0, 10, 50, 0.05, 15)]
    [InlineData(12, 10, 50, 0.05, 15)]
    [InlineData(18, 10, 50, 0.05, 15)]    // a cheaper delivery brings it down
    // Rounded up to the step: 11.11 x 1.5 is 16.665.
    [InlineData(15, 11.11, 50, 0.05, 16.70)]
    // Another markup where System setup sets one.
    [InlineData(0, 10, 40, 0.10, 14)]
    public void A_delivery_prices_the_item_at_cost_plus_the_markup(decimal retail, decimal cost,
        decimal markup, decimal step, decimal expected) =>
        Pricing.NewRetail(retail, cost, markup, step).Should().Be(expected);

    [Theory]
    [InlineData(15, 10, 50)]   // already cost x 1.5
    [InlineData(15, 0, 50)]    // a free line
    [InlineData(15, 10, 0)]    // markup set to nothing
    public void A_price_is_left_alone(decimal retail, decimal cost, decimal markup) =>
        Pricing.NewRetail(retail, cost, markup, 0.05m).Should().BeNull();

    [Fact]
    public async Task The_markup_is_fifty_percent_until_set()
    {
        var settings = Substitute.For<IPharmacySettings>();
        settings.GetAsync(PharmacySettingKey.Markup, Arg.Any<CancellationToken>()).Returns((string?)null);

        (await Pricing.ReadAsync(settings)).Markup.Should().Be(50m);

        settings.GetAsync(PharmacySettingKey.Markup, Arg.Any<CancellationToken>()).Returns("0");

        (await Pricing.ReadAsync(settings)).Markup.Should().Be(0m, "set to nothing on purpose");
    }
}
