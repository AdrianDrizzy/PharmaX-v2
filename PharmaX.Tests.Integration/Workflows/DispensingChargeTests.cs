using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Dispensing;
using PharmaX.Domain.Security;
using PharmaX.Persistence;

namespace PharmaX.Tests.Integration.Workflows;

/// <summary>
/// The charge on a dispensed item changed by hand - fourteen tablets priced
/// at 0.93 charged 1.00 - by the pharmacist, the assistant, the manager and
/// the administrator, with a reason on the record (24 October 2026).
/// </summary>
public class DispensingChargeTests : IClassFixture<PharmaXFixture>
{
    /// Dispenses, as the pharmacy may set a role up, without the charge.
    private const string Locum = "Locum dispenser";

    private readonly WorkflowSteps _steps;

    public DispensingChargeTests(PharmaXFixture fixture) =>
        _steps = new WorkflowSteps(fixture);

    /// <summary>
    /// The step a cash charge is rounded up to, for the tests in this class
    /// that need one: they share the database, so each sets its own.
    /// </summary>
    private async Task RoundDispensingTo(string step)
    {
        using var scope = _steps.Fixture.NewScope();
        await PharmaXFixture.Resolve<IPharmacySettings>(scope)
            .SetAsync(PharmacySettingKey.DispensingRounding, step);
    }

    [Fact]
    public async Task A_part_pack_is_charged_rounded_up_to_the_dollar_as_the_old_system_did()
    {
        // Calcigard 20mg came across from the old system at 6.66 for 100,
        // and it charged 1.00 for 14, 2.00 for 30 and 4.00 for 60.
        await RoundDispensingTo("1.00");
        await _steps.ProductAsync("DCDOL", "Dollar tablets 100s", 6.66m,
            schedule: 3, unitsPerPack: 100m, quantity: 5m);

        await _steps.SignInAsync("dc.dollar1", Role.Standard.Pharmacist);
        var patientId = await _steps.PatientAsync("Dollar", "Pat");
        var scriptId = await _steps.ScriptAsync(patientId, ("DCDOL", 14m, "One daily"));

        using var scope = _steps.Fixture.NewScope();
        var dispensing = PharmaXFixture.Resolve<IDispensingService>(scope);

        var dispensingId = (await dispensing.BeginAsync(scriptId)).Value.Id;
        (await dispensing.AddScriptItemsAsync(dispensingId)).Value
            .State.Items.Single().Tariff.Should().Be(1.00m, "6.66 x 14 / 100 is 0.93, rounded up");

        foreach (var (tablets, charged) in new[] { (30m, 2.00m), (60m, 4.00m) })
        {
            var changed = await dispensing.AmendItemAsync(dispensingId, 1, tablets, null, null);

            changed.IsSuccess.Should().BeTrue(changed.Error.Message);
            changed.Value.State.Items.Single().Tariff.Should().Be(charged, $"{tablets} tablets");
        }
    }

    [Fact]
    public async Task An_assistant_charges_an_item_other_than_its_price_with_a_reason()
    {
        // Charged to the cent, so the charge by hand below is a change.
        await RoundDispensingTo("0.01");

        await _steps.ProductAsync("DCNIF", "Charged tablets 100s", 6.66m,
            schedule: 3, unitsPerPack: 100m, quantity: 5m);

        await _steps.SignInAsync("dc.assist1", Role.Standard.PharmacistAssistant);
        var patientId = await _steps.PatientAsync("Charged", "Pat");
        var scriptId = await _steps.ScriptAsync(patientId, ("DCNIF", 14m, "One daily"));

        Guid dispensingId;

        using (var scope = _steps.Fixture.NewScope())
        {
            var dispensing = PharmaXFixture.Resolve<IDispensingService>(scope);

            dispensingId = (await dispensing.BeginAsync(scriptId)).Value.Id;
            (await dispensing.AddScriptItemsAsync(dispensingId)).Value
                .State.Items.Single().Tariff.Should().Be(0.93m, "6.66 x 14 / 100");

            (await dispensing.AmendItemAsync(dispensingId, 1, 14m, 1m, " "))
                .Error.Code.Should().Be("DSP-055", "a charge by hand needs a reason");

            var charged = await dispensing.AmendItemAsync(dispensingId, 1, 14m, 1m,
                "Rounded up to 1.00");

            charged.IsSuccess.Should().BeTrue(charged.Error.Message);
            charged.Value.State.Items.Single().Tariff.Should().Be(1m);
            charged.Value.State.CoPaymentTotal.Should().Be(1m, "the patient pays it");

            // Changing the quantity alone is still the dispenser's own.
            var fewer = await dispensing.AmendItemAsync(dispensingId, 1, 7m, null, null);

            fewer.IsSuccess.Should().BeTrue(fewer.Error.Message);
            fewer.Value.State.Items.Single().Tariff.Should().Be(0.47m,
                "priced for seven as a new item would be");
        }

        using (var scope = _steps.Fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

            (await db.AuditEntries.AsNoTracking()
                    .Where(a => a.EntityId == dispensingId
                                && a.Action == PermissionCode.DispensingChargeAdjust)
                    .Select(a => a.Reason).ToListAsync())
                .Should().Equal("Charged tablets 100s x14 priced 0.93, charged 1.00: "
                                + "Rounded up to 1.00");
        }
    }

    [Theory]
    [InlineData(Role.Standard.Pharmacist)]
    [InlineData(Role.Standard.Administrator)]
    public async Task The_pharmacist_and_administrator_change_the_charge_on_their_own(
        string role)
    {
        var code = role == Role.Standard.Pharmacist ? "DCPHA" : "DCADM";

        await _steps.ProductAsync(code, $"Own charge {code} 30s", 30m,
            schedule: 3, unitsPerPack: 30m, quantity: 5m);

        await _steps.SignInAsync($"dc.{code.ToLowerInvariant()}", role);
        var patientId = await _steps.PatientAsync("Own", code);
        var scriptId = await _steps.ScriptAsync(patientId, (code, 30m, "One daily"));

        using var scope = _steps.Fixture.NewScope();
        var dispensing = PharmaXFixture.Resolve<IDispensingService>(scope);

        var id = (await dispensing.BeginAsync(scriptId)).Value.Id;
        (await dispensing.AddScriptItemsAsync(id)).IsSuccess.Should().BeTrue();

        var charged = await dispensing.AmendItemAsync(id, 1, 30m, 25m, "Loyal patient");

        charged.IsSuccess.Should().BeTrue(charged.Error.Message);
        charged.Value.State.Items.Single().Tariff.Should().Be(25m);
    }

    [Fact]
    public async Task Someone_dispensing_without_it_has_the_charge_authorised()
    {
        await LocumRoleAsync();

        await _steps.ProductAsync("DCLOC", "Locum tablets 30s", 30m,
            schedule: 3, unitsPerPack: 30m, quantity: 5m);

        var managerId = await _steps.UserAsync("dc.mgr1", Role.Standard.Manager);
        var cashierId = await _steps.UserAsync("dc.cash1", Role.Standard.Cashier);
        var locumId = await _steps.SignInAsync("dc.locum1", Locum);

        var patientId = await _steps.PatientAsync("Locum", "Pat");
        var scriptId = await _steps.ScriptAsync(patientId, ("DCLOC", 30m, "One daily"));

        Guid id;

        using (var scope = _steps.Fixture.NewScope())
        {
            var dispensing = PharmaXFixture.Resolve<IDispensingService>(scope);

            id = (await dispensing.BeginAsync(scriptId)).Value.Id;
            (await dispensing.AddScriptItemsAsync(id)).IsSuccess.Should().BeTrue();

            (await dispensing.AmendItemAsync(id, 1, 30m, 28m, "Price match"))
                .Error.Code.Should().Be(DispensingService.ChargeNeedsAuthority,
                    "someone who may change a charge has to authorise it");

            (await dispensing.AmendItemAsync(id, 1, 30m, 28m, "Price match", cashierId))
                .Error.Code.Should().Be("DSP-057", "a cashier may not change a charge");

            (await dispensing.AmendItemAsync(id, 1, 30m, 28m, "Price match", locumId))
                .Error.Code.Should().Be("DSP-057", "nobody authorises their own");

            // The quantity alone needs nobody.
            (await dispensing.AmendItemAsync(id, 1, 28m, null, null))
                .IsSuccess.Should().BeTrue();
        }

        using (var scope = _steps.Fixture.NewScope())
        {
            var charged = await PharmaXFixture.Resolve<IDispensingService>(scope)
                .AmendItemAsync(id, 1, 28m, 25m, "Price match", managerId);

            charged.IsSuccess.Should().BeTrue(charged.Error.Message);
            charged.Value.State.Items.Single().Tariff.Should().Be(25m);
        }

        using (var scope = _steps.Fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

            var trail = await db.AuditEntries.AsNoTracking()
                .Where(a => a.EntityId == id
                            && a.Action == PermissionCode.DispensingChargeAdjust)
                .Select(a => new { a.Outcome, a.Reason }).ToListAsync();

            trail.Where(a => a.Outcome == "Applied").Select(a => a.Reason)
                .Should().Equal("Locum tablets 30s x28 priced 28.00, charged 25.00: "
                                + "Price match (authorised by dc.mgr1)");
            trail.Count(a => a.Outcome == "Refused").Should().Be(2,
                "the cashier's and the locum's own authorisations were refused");
        }
    }

    [Fact]
    public async Task An_issued_item_charged_pro_rata_is_not_a_charge_by_hand()
    {
        await _steps.ProductAsync("DCISS", "Issued charge tablets 30s", 30m,
            schedule: 3, unitsPerPack: 30m, batches: ("DCISS-A", 12, 4m));

        await _steps.SignInAsync("dc.pharm2", Role.Standard.Pharmacist);
        var patientId = await _steps.PatientAsync("Issued", "Charge");
        var scriptId = await _steps.ScriptAsync(patientId, ("DCISS", 60m, "One daily"));

        Guid id;

        using (var scope = _steps.Fixture.NewScope())
        {
            var dispensing = PharmaXFixture.Resolve<IDispensingService>(scope);

            id = (await dispensing.BeginAsync(scriptId)).Value.Id;
            (await dispensing.AddScriptItemsAsync(id)).IsSuccess.Should().BeTrue();
            (await dispensing.IssueAsync(id)).IsSuccess.Should().BeTrue();
        }

        using (var scope = _steps.Fixture.NewScope())
        {
            var dispensing = PharmaXFixture.Resolve<IDispensingService>(scope);

            // The amount the screen offers for thirty: half of 60.00.
            (await dispensing.AmendItemAsync(id, 1, 30m, 30m, "Took one month"))
                .IsSuccess.Should().BeTrue();

            (await dispensing.AmendItemAsync(id, 1, 30m, 29m, "Rounded down"))
                .IsSuccess.Should().BeTrue();
        }

        using (var scope = _steps.Fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

            (await db.AuditEntries.AsNoTracking()
                    .Where(a => a.EntityId == id
                                && a.Action == PermissionCode.DispensingChargeAdjust)
                    .Select(a => a.Reason).ToListAsync())
                .Should().Equal("Issued charge tablets 30s x30 priced 30.00, "
                                + "charged 29.00: Rounded down");
        }
    }

    /// A role that dispenses and captures scripts, without dsp.charge.adjust.
    private async Task LocumRoleAsync()
    {
        using var scope = _steps.Fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        if (await db.Roles.AnyAsync(r => r.Name == Locum)) return;

        var role = Role.Create(Locum);

        foreach (var code in new[]
                 {
                     PermissionCode.ScriptDispense, PermissionCode.ScriptCapture,
                     PermissionCode.PatientMaintain, PermissionCode.ProductRead,
                     PermissionCode.ProductDispensary
                 })
            role.Grant(code);

        db.Roles.Add(role);
        await db.SaveChangesAsync();
    }
}
