using FluentAssertions;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Dispensing;
using PharmaX.Domain.Security;

namespace PharmaX.Tests.Integration.Workflows;

/// <summary>
/// A pack and its price set on a script line, for that script alone: what
/// the line is charged when it is dispensed, kept when the script is used
/// again, and nothing kept where they are the product's own (17 October
/// 2026). The pharmacist, the assistant and the administrator alike.
/// </summary>
public class ScriptPackPriceTests : IClassFixture<PharmaXFixture>
{
    private readonly WorkflowSteps _steps;

    public ScriptPackPriceTests(PharmaXFixture fixture) =>
        _steps = new WorkflowSteps(fixture);

    [Theory]
    [InlineData("sp.pharm", Role.Standard.Pharmacist)]
    [InlineData("sp.assist", Role.Standard.PharmacistAssistant)]
    [InlineData("sp.admin", Role.Standard.Administrator)]
    public async Task The_line_is_charged_the_pack_and_price_set_on_the_script(
        string username, string role)
    {
        var code = "SP" + username[3..].ToUpperInvariant();

        // A tub of a thousand at 25.00: pro rata, forty would be 1.00.
        await _steps.ProductAsync(code, "Priced tablets 1000s", 25m,
            schedule: 3, unitsPerPack: 1000m, quantity: 5m);
        await _steps.ProductAsync(code + "S", "Shelf tablets 100s", 6.66m,
            schedule: 3, unitsPerPack: 100m, quantity: 5m);

        await _steps.SignInAsync(username, role);
        var patientId = await _steps.PatientAsync("Priced", username);

        using var scope = _steps.Fixture.NewScope();
        var scripts = PharmaXFixture.Resolve<IScriptService>(scope);
        var today = PharmaXFixture.Resolve<IClock>(scope).Today;

        var captured = await scripts.CaptureAsync(new ScriptEditDto(patientId,
            "Dr Test", null, today, null, 0, new[]
            {
                // Sold here in twenties at 1.00 a twenty.
                new ScriptItemEditDto(code, 40m, "Two three times a day",
                    PackUnits: 20m, PackPrice: 1m),

                // The product's own pack and price, as the screen fills them in.
                new ScriptItemEditDto(code + "S", 14m, "One daily",
                    PackUnits: 100m, PackPrice: 6.66m)
            }));

        captured.IsSuccess.Should().BeTrue(captured.Error.Message);

        // The patient's list of scripts names what is on each one.
        (await scripts.ForPatientAsync(patientId)).Value.Single().Items.Should().Be(
            "Priced tablets 1000s  x40" + Environment.NewLine + "Shelf tablets 100s  x14");

        var items = (await scripts.GetAsync(captured.Value)).Value.Items;
        items[0].PackUnits.Should().Be(20m);
        items[0].PackPrice.Should().Be(1m);
        items[1].PackUnits.Should().BeNull("the product's own pack is no change");
        items[1].PackPrice.Should().BeNull("and so is its shelf price");

        var dispensing = PharmaXFixture.Resolve<IDispensingService>(scope);
        var dispensingId = (await dispensing.BeginAsync(captured.Value)).Value.Id;
        var lines = (await dispensing.AddScriptItemsAsync(dispensingId)).Value.State.Items;

        lines.Single(l => l.Description == "Priced tablets 1000s").Tariff
            .Should().Be(2m, "1.00 a pack of twenty, for forty");
        lines.Single(l => l.Description == "Shelf tablets 100s").Tariff
            .Should().Be(1.00m, "the shelf price pro rata is 0.93, rounded up to the dollar");

        // Used again, the new script is priced the same way.
        var draft = await scripts.RepeatAsync(captured.Value);

        draft.IsSuccess.Should().BeTrue(draft.Error.Message);
        draft.Value.Items.Single(i => i.ProductCode == code)
            .Should().Match<ScriptItemEditDto>(i => i.PackUnits == 20m && i.PackPrice == 1m);
    }

    [Fact]
    public async Task A_pack_of_nothing_or_a_price_below_nothing_is_refused()
    {
        await _steps.ProductAsync("SPBAD", "Refused tablets 30s", 10m,
            schedule: 3, unitsPerPack: 30m);

        await _steps.SignInAsync("sp.pharm2", Role.Standard.Pharmacist);
        var patientId = await _steps.PatientAsync("Refused", "Pat");

        using var scope = _steps.Fixture.NewScope();
        var scripts = PharmaXFixture.Resolve<IScriptService>(scope);
        var today = PharmaXFixture.Resolve<IClock>(scope).Today;

        ScriptEditDto With(decimal? pack, decimal? price) => new(patientId,
            "Dr Test", null, today, null, 0, new[]
            {
                new ScriptItemEditDto("SPBAD", 30m, "One daily",
                    PackUnits: pack, PackPrice: price)
            });

        (await scripts.CaptureAsync(With(0m, 5m))).Error.Code.Should().Be("DSP-055");
        (await scripts.CaptureAsync(With(30m, -1m))).Error.Code.Should().Be("DSP-056");
    }
}
