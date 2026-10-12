using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Dispensing.Dtos;
using PharmaX.Application.Security;
using PharmaX.Domain.Dispensing;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Security;
using PharmaX.Persistence;

namespace PharmaX.Tests.Integration.Workflows;

/// <summary>
/// Create the script, add the medicines, issue, dispense - as each role
/// that is meant to do it, with no authorisation asked for where none is
/// needed, and a pharmacist asked for exactly where one is.
/// </summary>
public class DispensingWorkflowTests : IClassFixture<PharmaXFixture>
{
    private readonly WorkflowSteps _steps;

    public DispensingWorkflowTests(PharmaXFixture fixture) =>
        _steps = new WorkflowSteps(fixture);

    [Theory]
    [InlineData("wf.pharmacist", Role.Standard.Pharmacist)]
    [InlineData("wf.assistant", Role.Standard.PharmacistAssistant)]
    [InlineData("wf.admin", Role.Standard.Administrator)]
    public async Task Each_dispensing_role_captures_and_issues_a_script_on_its_own_authority(
        string username, string role)
    {
        await _steps.OpenTillAsync();

        var code = "WF" + username[3..6].ToUpperInvariant();
        await _steps.ProductAsync(code, $"Metformin 500mg tablets 60s ({role})",
            90m, schedule: 3, unitsPerPack: 60m,
            batches: new[] { ($"{code}-A", 6, 120m) });

        var operatorId = await _steps.SignInAsync(username, role);

        // Patients and scripts are captured by the same role, not by an
        // administrator on its behalf.
        var patientId = await _steps.PatientAsync($"Role{username[3..]}", "Pat");
        var scriptId = await _steps.ScriptAsync(patientId,
            (code, 30m, "One tablet twice a day"));

        using var scope = _steps.Fixture.NewScope();
        var dispensing = PharmaXFixture.Resolve<IDispensingService>(scope);

        var begun = await dispensing.BeginAsync(scriptId);
        begun.IsSuccess.Should().BeTrue(begun.Error.Message);

        var added = await dispensing.AddScriptItemsAsync(begun.Value.Id);
        added.IsSuccess.Should().BeTrue(added.Error.Message);
        added.Value.Added.Should().Be(1);

        // The prescriber's quantity and directions, carried through
        // rather than re-keyed (DEF-0020).
        var line = added.Value.State.Items.Should().ContainSingle().Subject;
        line.Quantity.Should().Be(30m);
        line.Directions.Should().Be("One tablet twice a day");

        var authority = await dispensing.IssueAuthorityAsync(begun.Value.Id);
        authority.Value.OperatorMayIssue.Should().BeTrue(
            "an ordinary script needs nobody else's password");

        var issued = await dispensing.IssueAsync(begun.Value.Id);
        issued.IsSuccess.Should().BeTrue(issued.Error.Message);
        issued.Value.Labels.Should().ContainSingle()
            .Which.Directions.Should().Be("One tablet twice a day");

        using var check = _steps.Fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(check);

        var stored = await db.Dispensings.AsNoTracking()
            .SingleAsync(d => d.Id == begun.Value.Id);

        stored.Status.Should().Be(DispensingStatus.Issued);
        stored.PharmacistId.Should().Be(operatorId);
    }

    [Fact]
    public async Task A_cashier_cannot_begin_a_dispensing()
    {
        await _steps.OpenTillAsync();
        await _steps.ProductAsync("WFCSH1", "Cashier-proof capsules", 20m,
            schedule: 4, batches: new[] { ("WFCSH1-A", 6, 10m) });

        var patientId = await _steps.PatientAsync("NoCashier", "Pat");
        var scriptId = await _steps.ScriptAsync(patientId,
            ("WFCSH1", 5m, "One daily"));

        await _steps.SignInAsync("wf.cashier", Role.Standard.Cashier);

        using var scope = _steps.Fixture.NewScope();
        var dispensing = PharmaXFixture.Resolve<IDispensingService>(scope);

        var act = () => dispensing.BeginAsync(scriptId);

        await act.Should().ThrowAsync<PermissionDeniedException>();
    }

    /// <summary>
    /// DEF-0009 and DEF-0008 together: 45 capsules needed from packs of
    /// 30, one pack in the earliest batch and two in the next. Both
    /// batches are drawn on, earliest first; the product's own on-hand
    /// falls by the same pack and a half; and there is a stock movement
    /// for every batch touched.
    /// </summary>
    /// <remarks>
    /// This test used to dispense 30 capsules against batches of 10 and
    /// 20 packs and expect all thirty packs to go - the tablets-as-packs
    /// mistake itself (ADM-DEF-15). Thirty capsules of a thirty-capsule
    /// pack is one pack.
    /// </remarks>
    [Fact]
    public async Task Dispensing_allocates_FEFO_across_batches_and_keeps_stock_consistent()
    {
        await _steps.OpenTillAsync();

        var productId = await _steps.ProductAsync("WFFEFO", "Fefo caps 30s",
            60m, schedule: 4, unitsPerPack: 30m,
            batches: new[] { ("FEFO-LATE", 12, 2m), ("FEFO-EARLY", 2, 1m) });

        await _steps.SignInAsync("wf.pharm2", Role.Standard.Pharmacist);

        var patientId = await _steps.PatientAsync("Fefo", "Pat");
        var scriptId = await _steps.ScriptAsync(patientId,
            ("WFFEFO", 45m, "One capsule daily"));

        Guid dispensingId;

        using (var scope = _steps.Fixture.NewScope())
        {
            var dispensing = PharmaXFixture.Resolve<IDispensingService>(scope);

            var begun = await dispensing.BeginAsync(scriptId);
            dispensingId = begun.Value.Id;

            var added = await dispensing.AddScriptItemsAsync(dispensingId);
            added.Value.State.Items.Single().BatchNumber
                .Should().Be("FEFO-EARLY x30, FEFO-LATE x15",
                    "the split is in capsules, as the label reads");

            var issued = await dispensing.IssueAsync(dispensingId);
            issued.IsSuccess.Should().BeTrue(issued.Error.Message);

            var label = issued.Value.Labels.Single();
            label.BatchNumber.Should().Contain("FEFO-EARLY").And
                .Contain("FEFO-LATE");
        }

        var (onHand, inBatches) = await _steps.StockOfAsync(productId);
        onHand.Should().Be(1.5m, "three packs less one and a half");
        inBatches.Should().Be(1.5m);

        using var check = _steps.Fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(check);

        var movements = await db.StockMovements.AsNoTracking()
            .Where(m => m.SourceDocumentId == dispensingId)
            .ToListAsync();

        movements.Should().HaveCount(2);
        movements.Should().OnlyContain(m => m.MovementType == MovementType.Issue
                                            && m.SourceDocumentType == "Dispensing");
        movements.Sum(m => m.Quantity).Should().Be(-1.5m,
            "the shelf is counted in packs");

        var allocations = await db.Set<DispensingLineBatch>().AsNoTracking()
            .ToListAsync();
        allocations.Where(a => a.BatchNumber.StartsWith("FEFO", StringComparison.Ordinal))
            .Select(a => (a.BatchNumber, a.Quantity))
            .Should().BeEquivalentTo(new[] { ("FEFO-EARLY", 30m), ("FEFO-LATE", 15m) });
    }

    /// <summary>
    /// A script is counted in tablets; the shelf in packs.
    /// </summary>
    /// <remarks>
    /// Sixty tablets of a sixty-tablet pack against sixteen packs was
    /// refused as "16 unexpired in stock, so 60 cannot be dispensed", and
    /// would have taken sixty packs had it gone through (ADM-DEF-15).
    /// Priced as the one pack it is.
    /// </remarks>
    [Fact]
    public async Task A_script_in_tablets_takes_packs_off_the_shelf()
    {
        await _steps.OpenTillAsync();

        var productId = await _steps.ProductAsync("WFMETF",
            "Metformin 850mg tablets 60s", 150m, schedule: 4,
            unitsPerPack: 60m, batches: new[] { ("METF-A", 12, 16m) });

        await _steps.SignInAsync("wf.pharm3", Role.Standard.Pharmacist);

        var patientId = await _steps.PatientAsync("Units", "Pat");
        var scriptId = await _steps.ScriptAsync(patientId,
            ("WFMETF", 60m, "One tablet twice daily"));

        using (var scope = _steps.Fixture.NewScope())
        {
            var dispensing = PharmaXFixture.Resolve<IDispensingService>(scope);

            var begun = await dispensing.BeginAsync(scriptId);
            var added = await dispensing.AddScriptItemsAsync(begun.Value.Id);
            added.IsSuccess.Should().BeTrue(added.Error.Message);

            var issued = await dispensing.IssueAsync(begun.Value.Id);
            issued.IsSuccess.Should().BeTrue(issued.Error.Message);
            issued.Value.CoPayment.Should().Be(150m, "sixty tablets is one pack");
        }

        var (onHand, inBatches) = await _steps.StockOfAsync(productId);
        onHand.Should().Be(15m);
        inBatches.Should().Be(15m);
    }

    /// <remarks>
    /// Twenty tablets from packs of twenty, off an untracked shelf: one
    /// pack priced, one pack gone (ADM-DEF-14).
    /// </remarks>
    [Fact]
    public async Task An_untracked_product_is_dispensed_by_the_pack_it_holds()
    {
        await _steps.OpenTillAsync();

        var productId = await _steps.ProductAsync("WFPAN",
            "Panado 500mg tablets 20s", 13m, schedule: 3, unitsPerPack: 20m,
            quantity: 5m);

        await _steps.SignInAsync("wf.pharm4", Role.Standard.Pharmacist);

        var patientId = await _steps.PatientAsync("Untracked", "Pat");
        var scriptId = await _steps.ScriptAsync(patientId,
            ("WFPAN", 20m, "Two tablets when needed"));

        using (var scope = _steps.Fixture.NewScope())
        {
            var dispensing = PharmaXFixture.Resolve<IDispensingService>(scope);

            var begun = await dispensing.BeginAsync(scriptId);
            (await dispensing.AddScriptItemsAsync(begun.Value.Id))
                .IsSuccess.Should().BeTrue();

            var issued = await dispensing.IssueAsync(begun.Value.Id);
            issued.IsSuccess.Should().BeTrue(issued.Error.Message);
            issued.Value.CoPayment.Should().Be(13m);
        }

        (await _steps.StockOfAsync(productId)).Product.Should().Be(4m);
    }

    /// <summary>
    /// DEF-0006: penicillin-allergic patient, amoxicillin on the script. A
    /// hard stop, passed only with a written reason, which is kept.
    /// </summary>
    [Fact]
    public async Task A_penicillin_allergy_stops_amoxicillin_until_it_is_overridden_with_a_reason()
    {
        await _steps.OpenTillAsync();
        await _steps.ProductAsync("WFAMOX", "Amoxicillin 500mg capsules 21s",
            118m, schedule: 4, unitsPerPack: 21m,
            batches: new[] { ("WFAMOX-A", 6, 50m) });

        await _steps.SignInAsync("wf.assist3", Role.Standard.PharmacistAssistant);

        var patientId = await _steps.PatientAsync("Naidoo", "Priya",
            ("Penicillin", "Moderate"));
        var scriptId = await _steps.ScriptAsync(patientId,
            ("WFAMOX", 21m, "One capsule three times a day"));

        using var scope = _steps.Fixture.NewScope();
        var dispensing = PharmaXFixture.Resolve<IDispensingService>(scope);

        var begun = await dispensing.BeginAsync(scriptId);
        var added = await dispensing.AddScriptItemsAsync(begun.Value.Id);

        var warning = added.Value.State.Warnings.Should().ContainSingle().Subject;
        warning.Kind.Should().Be("Allergy");
        warning.IsHardStop.Should().BeTrue();
        warning.Message.Should().Contain("Penicillin").And.Contain("penicillin");

        // An assistant releases it on their own written reason: no
        // pharmacist's credentials since 12 October 2026.
        (await dispensing.OverrideNeedsPharmacistAsync(warning.Id))
            .Should().BeFalse();

        (await dispensing.AcknowledgeWarningAsync(warning.Id, null))
            .Error.Code.Should().Be("DSP-033", "a hard stop still needs a reason");

        var released = await dispensing.AcknowledgeWarningAsync(warning.Id,
            "Tolerated amoxicillin in 2024; prescriber consulted");
        released.IsSuccess.Should().BeTrue(released.Error.Message);

        var issued = await dispensing.IssueAsync(begun.Value.Id);
        issued.IsSuccess.Should().BeTrue(issued.Error.Message);

        using var check = _steps.Fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(check);

        var stored = await db.Set<ClinicalWarning>().AsNoTracking()
            .SingleAsync(w => w.Id == warning.Id);

        stored.IsAcknowledged.Should().BeTrue();
        stored.AuthorisedBy.Should().BeNull();
        stored.OverrideReason.Should().Contain("prescriber consulted");

        (await db.AuditEntries.AsNoTracking()
                .AnyAsync(a => a.Action == "dsp.warning.override"
                               && a.EntityId == warning.Id
                               && a.Outcome == "Applied"))
            .Should().BeTrue();
    }

    /// <summary>
    /// An assistant issues a controlled medicine on their own authority
    /// since 12 October 2026, but an authoriser named on the issue is
    /// still checked.
    /// </summary>
    [Fact]
    public async Task An_assistant_issues_a_controlled_medicine_without_a_pharmacist()
    {
        await _steps.OpenTillAsync();
        await _steps.ProductAsync("WFCTRL", "Codeine phosphate 30mg 20s",
            55m, schedule: 6, batches: new[] { ("WFCTRL-A", 6, 40m) });

        var otherAssistant = await _steps.UserAsync("wf.assist5",
            Role.Standard.PharmacistAssistant);

        await _steps.SignInAsync("wf.assist4", Role.Standard.PharmacistAssistant);

        var patientId = await _steps.PatientAsync("Controlled", "Pat");
        var scriptId = await _steps.ScriptAsync(patientId,
            ("WFCTRL", 10m, "One at night when needed"));

        using var scope = _steps.Fixture.NewScope();
        var dispensing = PharmaXFixture.Resolve<IDispensingService>(scope);

        var begun = await dispensing.BeginAsync(scriptId);
        await dispensing.AddScriptItemsAsync(begun.Value.Id);

        var authority = await dispensing.IssueAuthorityAsync(begun.Value.Id);
        authority.Value.OperatorMayIssue.Should().BeTrue();

        (await dispensing.IssueAsync(begun.Value.Id, otherAssistant)).Error.Code
            .Should().Be("DSP-031", "another assistant is not a pharmacist");

        var issued = await dispensing.IssueAsync(begun.Value.Id);
        issued.IsSuccess.Should().BeTrue(issued.Error.Message);
    }

    /// <summary>
    /// DEF-0021: the pack price is for 21 capsules, so 7 cost a third.
    /// </summary>
    [Fact]
    public async Task The_price_follows_the_quantity_dispensed()
    {
        await _steps.OpenTillAsync();
        await _steps.ProductAsync("WFPRO", "Prorata capsules 21s", 126m,
            schedule: 4, unitsPerPack: 21m,
            batches: new[] { ("WFPRO-A", 6, 100m) });

        await _steps.SignInAsync("wf.pharm5", Role.Standard.Pharmacist);

        var patientId = await _steps.PatientAsync("Prorata", "Pat");
        var scriptId = await _steps.ScriptAsync(patientId,
            ("WFPRO", 7m, "One daily"));

        using var scope = _steps.Fixture.NewScope();
        var dispensing = PharmaXFixture.Resolve<IDispensingService>(scope);

        var begun = await dispensing.BeginAsync(scriptId);
        var seven = await dispensing.AddScriptItemsAsync(begun.Value.Id);
        seven.Value.State.Items.Single().Tariff.Should().Be(42m);

        var more = await dispensing.AddItemAsync(begun.Value.Id, "WFPRO",
            new DirectionsDto(21m, "One daily"));
        more.Value.Items.Last().Tariff.Should().Be(126m);
    }
}
