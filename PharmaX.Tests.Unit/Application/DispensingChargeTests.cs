using FluentAssertions;
using NSubstitute;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Dispensing.Dtos;

namespace PharmaX.Tests.Unit.Application;

/// <summary>
/// A cash patient's dispensing charge: the pack price pro rata, rounded up
/// as the previous system charged it. Calcigard 20mg (Torrent) came across
/// at 6.66 for 100 tablets, and the counter charged 1.00 for 14, 2.00 for
/// 30 and 4.00 for 60.
/// </summary>
public class DispensingChargeTests
{
    [Theory]
    [InlineData(14, 1.00)]
    [InlineData(30, 2.00)]
    [InlineData(60, 4.00)]
    [InlineData(100, 7.00)]
    public void Calcigard_is_charged_as_the_old_system_charged_it(int tablets, double charged) =>
        DispensingCharge.For(6.66m, 100m, tablets, 1m).Should().Be((decimal)charged);

    [Fact]
    public void A_step_of_a_cent_charges_the_exact_share() =>
        DispensingCharge.For(6.66m, 100m, 14m, 0.01m).Should().Be(0.93m);

    [Fact]
    public void An_item_handed_over_whole_is_charged_its_price_unrounded() =>
        DispensingCharge.For(1.25m, 1m, 9m, 1m).Should().Be(11.25m, "nine injections at 1.25");

    [Fact]
    public void Metronidazole_42_of_a_thousand_at_47_60_is_two_dollars() =>
        DispensingCharge.Explain(47.60m, 1000m, "tablet", 42m, 1m)
            .Should().Be("47.60 for 1000, so 42 tablets come to 1.9992, rounded up to 2.00.");

    [Fact]
    public void Says_what_to_enter_and_how_it_is_charged()
    {
        DispensingCharge.WhatToEnter("tablet", 100m)
            .Should().Be("Enter the number of tablets handed over (a pack holds 100).");

        DispensingCharge.Explain(6.66m, 100m, "tablet", 14m, 1m)
            .Should().Be("6.66 for 100, so 14 tablets come to 0.9324, rounded up to 1.00.");

        DispensingCharge.Explain(6.66m, 100m, "tablet", 50m, 0.01m)
            .Should().Be("6.66 for 100, so 50 tablets come to 3.33.");
    }

    [Theory]
    [InlineData(null, 1.00)]
    [InlineData("", 1.00)]
    [InlineData("0.50", 0.50)]
    [InlineData("0.01", 0.01)]
    [InlineData("0", 0.00)]
    [InlineData("nonsense", 1.00)]
    public async Task The_step_is_a_dollar_unless_System_setup_says_otherwise(string? set, double step)
    {
        var settings = Substitute.For<IPharmacySettings>();
        settings.GetAsync(PharmacySettingKey.DispensingRounding, Arg.Any<CancellationToken>()).Returns(set);

        (await DispensingCharge.RoundToAsync(settings)).Should().Be((decimal)step);
    }

    [Fact]
    public void A_dispensary_quote_is_priced_as_the_dispensing_will_charge_it()
    {
        var quote = new PharmaX.Application.Sales.QuotationDto(Guid.NewGuid(), "Q-1", "", default, default, "Open",
            null, new[]
            {
                new PharmaX.Application.Sales.QuotationLineDto(1, Guid.NewGuid(), "METRONIDAZOLE TABLETS 200MG", 42m,
                    47.60m, "Z", 1.9992m, UnitsInPack: 1000m),
                new PharmaX.Application.Sales.QuotationLineDto(2, Guid.NewGuid(), "METRONIDAZOLE INJ 100ML", 2m,
                    1.25m, "Z", 2.50m)
            }, 4.4992m, Kind: "Dispensary");

        var priced = quote.ChargedAsDispensed(1m);

        priced.Lines.Select(l => l.LineTotal).Should().Equal(2.00m, 2.50m);
        priced.Total.Should().Be(4.50m);
        (quote with { Kind = "OverTheCounter" }).ChargedAsDispensed(1m).Total.Should().Be(4.4992m,
            "a till quote is priced as it is");
    }

    [Fact]
    public void The_item_prompt_works_the_charge_out_as_it_is_typed()
    {
        var proposal = new ItemProposalDto(Guid.NewGuid(), "1593", "NIFEDIPINE CALCIGARD (TORRENT) 20MG", 14m,
            "T1 OD", FromScript: true, Unit: "tablet", UnitsInPack: 100m, PackPrice: 6.66m, RoundTo: 1m);

        proposal.WhatToEnter.Should().Contain("number of tablets");
        proposal.ChargeFor(14m).Should().EndWith("rounded up to 1.00.");
        (proposal with { PackPrice = null }).ChargeFor(14m).Should().Be("Charged at the medical aid's tariff.");
    }
}
