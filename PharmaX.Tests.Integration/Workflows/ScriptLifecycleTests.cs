using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Reporting;
using PharmaX.Application.Security;
using PharmaX.Domain.Dispensing;
using PharmaX.Domain.Security;
using PharmaX.Persistence;

namespace PharmaX.Tests.Integration.Workflows;

/// <summary>
/// A script from capture to reversal: numbered once, corrected while it
/// still can be, printed again, and taken back when it went wrong.
/// </summary>
/// <remarks>
/// Each of these was found missing or wrong by the administrator's
/// end-to-end run (ADM-DEF-13, -17, -22, ADM-OBS-13, -14).
/// </remarks>
public class ScriptLifecycleTests : IClassFixture<PharmaXFixture>
{
    private readonly WorkflowSteps _steps;

    public ScriptLifecycleTests(PharmaXFixture fixture) =>
        _steps = new WorkflowSteps(fixture);

    [Fact]
    public async Task A_refused_capture_spends_no_script_number()
    {
        await _steps.ProductAsync("SLNUM", "Numbering tablets 30s", 30m,
            schedule: 3, unitsPerPack: 30m);

        await _steps.SignInAsync("sl.pharm1", Role.Standard.Pharmacist);
        var patientId = await _steps.PatientAsync("Numbered", "Pat");

        var first = await NumberOfAsync(await _steps.ScriptAsync(patientId,
            ("SLNUM", 30m, "One daily")));

        using (var scope = _steps.Fixture.NewScope())
        {
            var scripts = PharmaXFixture.Resolve<IScriptService>(scope);
            var clock = PharmaXFixture.Resolve<IClock>(scope);

            var noDirections = await scripts.CaptureAsync(new ScriptEditDto(
                patientId, "Dr Test", null, clock.Today, null, 0,
                new[] { new ScriptItemEditDto("SLNUM", 30m, "") }));

            noDirections.Error.Code.Should().Be("DSP-025");

            var noPrescriber = await scripts.CaptureAsync(new ScriptEditDto(
                patientId, "  ", null, clock.Today, null, 0,
                new[] { new ScriptItemEditDto("SLNUM", 30m, "One daily") }));

            noPrescriber.Error.Code.Should().Be("DSP-029");
        }

        var next = await NumberOfAsync(await _steps.ScriptAsync(patientId,
            ("SLNUM", 30m, "One daily")));

        Sequence(next).Should().Be(Sequence(first) + 1,
            "the two refused saves used no number");
    }

    [Fact]
    public async Task A_script_is_found_by_its_number_and_by_the_day_it_was_captured()
    {
        await _steps.ProductAsync("SLFIND", "Findable tablets 28s", 28m,
            schedule: 3, unitsPerPack: 28m);

        await _steps.SignInAsync("sl.pharm5", Role.Standard.Pharmacist);
        var patientId = await _steps.PatientAsync("Findable", "Pat");
        var scriptId = await _steps.ScriptAsync(patientId, ("SLFIND", 28m, "One daily"));
        var number = await NumberOfAsync(scriptId);

        using var scope = _steps.Fixture.NewScope();
        var scripts = PharmaXFixture.Resolve<IScriptService>(scope);
        var captured = DateOnly.FromDateTime((await PharmaXFixture
            .Resolve<PharmaXDbContext>(scope).Scripts.AsNoTracking()
            .SingleAsync(s => s.Id == scriptId)).CreatedAtUtc.ToLocalTime());

        var byNumber = (await scripts.FindAsync(number, null)).Value;
        byNumber.Should().ContainSingle(s => s.Id == scriptId)
            .Which.Patient.Should().ContainEquivalentOf("Findable");

        (await scripts.FindAsync("", captured)).Value
            .Should().Contain(s => s.Id == scriptId);
        (await scripts.FindAsync("", captured.AddDays(-1))).Value
            .Should().NotContain(s => s.Id == scriptId);
    }

    [Fact]
    public async Task A_captured_script_can_be_corrected_until_it_is_dispensed()
    {
        await _steps.OpenTillAsync();
        await _steps.ProductAsync("SLEDIT", "Editable capsules 21s", 42m,
            schedule: 3, unitsPerPack: 21m, quantity: 10m);

        await _steps.SignInAsync("sl.pharm2", Role.Standard.Pharmacist);
        var patientId = await _steps.PatientAsync("Corrected", "Pat");

        var scriptId = await _steps.ScriptAsync(patientId,
            ("SLEDIT", 12m, "One capsule three times a day"));

        using (var scope = _steps.Fixture.NewScope())
        {
            var scripts = PharmaXFixture.Resolve<IScriptService>(scope);
            var clock = PharmaXFixture.Resolve<IClock>(scope);

            (await scripts.GetAsync(scriptId)).Value.IsEditable.Should().BeTrue();

            var corrected = await scripts.UpdateAsync(scriptId,
                new ScriptEditDto(patientId, "Dr Corrected", "MP42",
                    clock.Today, null, 2,
                    new[] { new ScriptItemEditDto("SLEDIT", 21m,
                        "One capsule three times a day for seven days") }));

            corrected.IsSuccess.Should().BeTrue(corrected.Error.Message);
        }

        using (var scope = _steps.Fixture.NewScope())
        {
            var detail = (await PharmaXFixture.Resolve<IScriptService>(scope)
                .GetAsync(scriptId)).Value;

            detail.Script.PrescriberName.Should().Be("Dr Corrected");
            detail.PrescriberNumber.Should().Be("MP42");
            detail.Script.RepeatsAuthorised.Should().Be(2);
            detail.Items.Single().Quantity.Should().Be(21m);
            detail.Items.Single().ProductCode.Should().Be("SLEDIT");
        }

        // Once a dispensing is under way against it, it is a record.
        using (var scope = _steps.Fixture.NewScope())
        {
            var dispensing = PharmaXFixture.Resolve<IDispensingService>(scope);
            (await dispensing.BeginAsync(scriptId)).IsSuccess.Should().BeTrue();
        }

        using (var scope = _steps.Fixture.NewScope())
        {
            var scripts = PharmaXFixture.Resolve<IScriptService>(scope);
            var clock = PharmaXFixture.Resolve<IClock>(scope);

            var refused = await scripts.UpdateAsync(scriptId,
                new ScriptEditDto(patientId, "Dr Late", null, clock.Today,
                    null, 0, new[] { new ScriptItemEditDto("SLEDIT", 7m, "Daily") }));

            refused.Error.Code.Should().Be("DSP-028");
        }
    }

    [Fact]
    public async Task A_dispensing_closed_without_issue_is_set_aside()
    {
        await _steps.ProductAsync("SLABAN", "Abandoned tablets 10s", 10m,
            schedule: 3, unitsPerPack: 10m, quantity: 5m);

        await _steps.SignInAsync("sl.pharm3", Role.Standard.Pharmacist);
        var patientId = await _steps.PatientAsync("Abandoned", "Pat");
        var scriptId = await _steps.ScriptAsync(patientId,
            ("SLABAN", 10m, "Daily"));

        Guid dispensingId;

        using (var scope = _steps.Fixture.NewScope())
        {
            var dispensing = PharmaXFixture.Resolve<IDispensingService>(scope);
            dispensingId = (await dispensing.BeginAsync(scriptId)).Value.Id;

            (await dispensing.AbandonAsync(dispensingId)).IsSuccess
                .Should().BeTrue();
        }

        using (var scope = _steps.Fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

            (await db.Dispensings.AsNoTracking()
                    .SingleAsync(d => d.Id == dispensingId))
                .Status.Should().Be(DispensingStatus.Abandoned);

            (await PharmaXFixture.Resolve<IScriptService>(scope)
                    .GetAsync(scriptId))
                .Value.IsEditable.Should().BeTrue(
                    "nothing was dispensed, so it can still be corrected");
        }
    }

    [Fact]
    public async Task An_issued_dispensing_is_reversed_back_onto_the_shelf()
    {
        await _steps.OpenTillAsync();

        var productId = await _steps.ProductAsync("SLREV",
            "Reversible tablets 30s", 60m, schedule: 3, unitsPerPack: 30m,
            batches: new[] { ("SLREV-A", 12, 4m) });

        await _steps.SignInAsync("sl.pharm4", Role.Standard.Pharmacist);
        var patientId = await _steps.PatientAsync("Reversed", "Pat");
        var scriptId = await _steps.ScriptAsync(patientId,
            ("SLREV", 45m, "One daily"));

        Guid dispensingId;

        using (var scope = _steps.Fixture.NewScope())
        {
            var dispensing = PharmaXFixture.Resolve<IDispensingService>(scope);

            dispensingId = (await dispensing.BeginAsync(scriptId)).Value.Id;
            (await dispensing.AddScriptItemsAsync(dispensingId))
                .IsSuccess.Should().BeTrue();
            (await dispensing.IssueAsync(dispensingId))
                .IsSuccess.Should().BeTrue();
        }

        (await _steps.StockOfAsync(productId)).Product.Should().Be(2.5m);

        using (var scope = _steps.Fixture.NewScope())
        {
            var dispensing = PharmaXFixture.Resolve<IDispensingService>(scope);

            (await dispensing.ReverseAsync(dispensingId, "")).Error.Code
                .Should().Be("DSP-034", "a reversal needs a reason");

            var reversed = await dispensing.ReverseAsync(dispensingId,
                "Issued to the wrong patient");

            reversed.IsSuccess.Should().BeTrue(reversed.Error.Message);
        }

        var (onHand, inBatches) = await _steps.StockOfAsync(productId);
        onHand.Should().Be(4m, "every pack that left came back");
        inBatches.Should().Be(4m);

        using var check = _steps.Fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(check);

        var stored = await db.Dispensings.AsNoTracking()
            .SingleAsync(d => d.Id == dispensingId);

        stored.Status.Should().Be(DispensingStatus.Reversed);
        stored.ReversalReason.Should().Be("Issued to the wrong patient");
        stored.ReversedAtUtc.Should().NotBeNull();

        var script = await db.Scripts.AsNoTracking()
            .SingleAsync(s => s.Id == scriptId);

        script.RepeatsUsed.Should().Be(0, "the repeat goes back to the script");

        // Printed again, with the reversal on it.
        var printout = await PharmaXFixture.Resolve<IScriptService>(check)
            .PrintoutAsync(scriptId);

        printout.IsSuccess.Should().BeTrue(printout.Error.Message);
        printout.Value.Title.Should().StartWith("Script SC-");
        printout.Value.Lines.Should().Contain(l => l.Contains("REVERSED",
            StringComparison.Ordinal));

        // And found on the Reversed Scripts report by the words of its
        // reason, which the report's Reason box used to ignore.
        (await ReversedAsync("wrong patient")).Should()
            .Contain(stored.DocumentNumber);
        (await ReversedAsync("expired stock")).Should()
            .NotContain(stored.DocumentNumber);
    }

    /// Only someone with dsp.script.delete (administrator by default) may
    /// clear an uncollected script from Collect. A cashier is refused.
    [Fact]
    public async Task A_cashier_cannot_delete_an_uncollected_script()
    {
        await _steps.OpenTillAsync();

        await _steps.ProductAsync("SLDELX",
            "Protected tablets 30s", 60m, schedule: 3, unitsPerPack: 30m,
            batches: new[] { ("SLDELX-A", 12, 4m) });

        await _steps.SignInAsync("sl.pharm5x", Role.Standard.Pharmacist);
        var patientId = await _steps.PatientAsync("Kept", "Pat");
        var scriptId = await _steps.ScriptAsync(patientId,
            ("SLDELX", 45m, "One daily"));

        Guid dispensingId;

        using (var scope = _steps.Fixture.NewScope())
        {
            var dispensing = PharmaXFixture.Resolve<IDispensingService>(scope);

            dispensingId = (await dispensing.BeginAsync(scriptId)).Value.Id;
            (await dispensing.AddScriptItemsAsync(dispensingId))
                .IsSuccess.Should().BeTrue();
            (await dispensing.IssueAsync(dispensingId))
                .IsSuccess.Should().BeTrue();
        }

        await _steps.SignInAsync("sl.cash5x", Role.Standard.Cashier);

        using var till = _steps.Fixture.NewScope();
        var collect = PharmaXFixture.Resolve<PharmaX.Application.Sales.ICollectionService>(till);

        var act = () => collect.RemoveAsync(new[] { dispensingId });
        await act.Should().ThrowAsync<PermissionDeniedException>();

        (await collect.WaitingAsync()).Value
            .Should().Contain(w => w.DispensingId == dispensingId);
    }

    /// An administrator deletes one nobody came to pay for - medicine
    /// goes back on the shelf, and the script leaves the queue at once.
    [Fact]
    public async Task An_administrator_deletes_an_uncollected_script_back_onto_the_shelf()
    {
        await _steps.OpenTillAsync();

        var productId = await _steps.ProductAsync("SLDEL",
            "Deletable tablets 30s", 60m, schedule: 3, unitsPerPack: 30m,
            batches: new[] { ("SLDEL-A", 12, 4m) });

        await _steps.SignInAsync("sl.pharm5", Role.Standard.Pharmacist);
        var patientId = await _steps.PatientAsync("Deleted", "Pat");
        var scriptId = await _steps.ScriptAsync(patientId,
            ("SLDEL", 45m, "One daily"));

        Guid dispensingId;

        using (var scope = _steps.Fixture.NewScope())
        {
            var dispensing = PharmaXFixture.Resolve<IDispensingService>(scope);

            dispensingId = (await dispensing.BeginAsync(scriptId)).Value.Id;
            (await dispensing.AddScriptItemsAsync(dispensingId))
                .IsSuccess.Should().BeTrue();
            (await dispensing.IssueAsync(dispensingId))
                .IsSuccess.Should().BeTrue();
        }

        await _steps.SignInAsync("sl.admin5", Role.Standard.Administrator);

        using (var scope = _steps.Fixture.NewScope())
        {
            var collect = PharmaXFixture.Resolve<PharmaX.Application.Sales.ICollectionService>(scope);

            var removed = await collect.RemoveAsync(new[] { dispensingId });

            removed.IsSuccess.Should().BeTrue(removed.Error.Message);
            (await collect.WaitingAsync()).Value
                .Should().NotContain(w => w.DispensingId == dispensingId);
        }

        (await _steps.StockOfAsync(productId)).Product.Should().Be(4m,
            "the medicine nobody paid for goes back on the shelf");

        using var check = _steps.Fixture.NewScope();
        var stored = await PharmaXFixture.Resolve<PharmaXDbContext>(check)
            .Dispensings.AsNoTracking().SingleAsync(d => d.Id == dispensingId);

        stored.Status.Should().Be(DispensingStatus.Reversed);
        stored.ReversalReason.Should().Be("Deleted from Scripts to collect.");
    }

    private async Task<IReadOnlyList<string?>> ReversedAsync(string reason)
    {
        using var scope = _steps.Fixture.NewScope();
        var clock = PharmaXFixture.Resolve<IClock>(scope);

        var result = await PharmaXFixture.Resolve<IReportRunner>(scope)
            .RunAsync(new ReportRequest(
                PresetKey: "dsp.scripts.reversed",
                BranchCode: "1",
                From: clock.Today.AddDays(-1),
                To: clock.Today,
                Slicers: new Dictionary<string, string?> { ["reason"] = reason }));

        return result.Rows.Select(r => r["Document"] as string).ToList();
    }

    private async Task<string> NumberOfAsync(Guid scriptId)
    {
        using var scope = _steps.Fixture.NewScope();

        return (await PharmaXFixture.Resolve<IScriptService>(scope)
            .GetAsync(scriptId)).Value.Script.ScriptNumber;
    }

    private static int Sequence(string number) =>
        int.Parse(number[(number.LastIndexOf('-') + 1)..],
                  System.Globalization.CultureInfo.InvariantCulture);
}
