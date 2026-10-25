using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Inventory;
using PharmaX.Domain.Security;
using PharmaX.Persistence;

namespace PharmaX.Tests.Integration.Workflows;

/// <summary>
/// A patient's supply picked from their history and used again, an item
/// changed before and after it was issued, and bulk stock counted into
/// pre-packs - the pharmacist's and the assistant's work of 16 October 2026.
/// </summary>
public class HistoryAmendPrepackTests : IClassFixture<PharmaXFixture>
{
    private readonly WorkflowSteps _steps;

    public HistoryAmendPrepackTests(PharmaXFixture fixture) =>
        _steps = new WorkflowSteps(fixture);

    [Fact]
    public async Task An_issued_item_changed_moves_the_stock_and_what_the_patient_pays()
    {
        var productId = await _steps.ProductAsync("HAAMD", "Amended tablets 30s",
            60m, schedule: 3, unitsPerPack: 30m, batches: ("HAAMD-A", 12, 4m));

        await _steps.SignInAsync("ha.pharm1", Role.Standard.Pharmacist);
        var patientId = await _steps.PatientAsync("Amended", "Pat");
        var scriptId = await _steps.ScriptAsync(patientId, ("HAAMD", 60m, "One daily"));
        var dispensingId = await IssueAsync(scriptId);

        (await _steps.StockOfAsync(productId)).Should().Be((2m, 2m));

        using (var scope = _steps.Fixture.NewScope())
        {
            var dispensing = PharmaXFixture.Resolve<IDispensingService>(scope);

            (await dispensing.AmendItemAsync(dispensingId, 1, 30m, null, " "))
                .Error.Code.Should().Be("DSP-052", "a change after the issue needs a reason");

            var less = await dispensing.AmendItemAsync(dispensingId, 1, 30m, null,
                "Patient took one month");

            less.IsSuccess.Should().BeTrue(less.Error.Message);
            less.Value.WasIssued.Should().BeTrue();
            less.Value.CoPaymentBefore.Should().Be(120m);
            less.Value.CoPaymentAfter.Should().Be(60m, "half the tablets, half the amount");
            less.Value.ToSettle.Should().Be(0m, "nothing was paid yet");
        }

        (await _steps.StockOfAsync(productId)).Should().Be((3m, 3m),
            "the thirty not handed over went back onto their batch");

        using (var scope = _steps.Fixture.NewScope())
        {
            var more = await PharmaXFixture.Resolve<IDispensingService>(scope)
                .AmendItemAsync(dispensingId, 1, 45m, 80m, "Fifteen more after all");

            more.IsSuccess.Should().BeTrue(more.Error.Message);
            more.Value.State.Items.Single().Tariff.Should().Be(80m, "the amount given");
        }

        (await _steps.StockOfAsync(productId)).Should().Be((2.5m, 2.5m));

        // Reversed after both changes, exactly what is still out comes back.
        using (var scope = _steps.Fixture.NewScope())
        {
            var reversed = await PharmaXFixture.Resolve<IDispensingService>(scope)
                .ReverseAsync(dispensingId, "Wrong patient");

            reversed.IsSuccess.Should().BeTrue(reversed.Error.Message);
        }

        (await _steps.StockOfAsync(productId)).Should().Be((4m, 4m),
            "nothing comes back twice and nothing is left out");
    }

    [Fact]
    public async Task An_item_changed_before_the_issue_is_priced_again()
    {
        await _steps.ProductAsync("HAPRE", "Repriced tablets 30s", 60m,
            schedule: 3, unitsPerPack: 30m, quantity: 10m);

        await _steps.SignInAsync("ha.assist1", Role.Standard.PharmacistAssistant);
        var patientId = await _steps.PatientAsync("Repriced", "Pat");
        var scriptId = await _steps.ScriptAsync(patientId, ("HAPRE", 60m, "One daily"));

        using var scope = _steps.Fixture.NewScope();
        var dispensing = PharmaXFixture.Resolve<IDispensingService>(scope);

        var dispensingId = (await dispensing.BeginAsync(scriptId)).Value.Id;
        (await dispensing.AddScriptItemsAsync(dispensingId)).Value
            .State.Items.Single().Tariff.Should().Be(120m);

        var fewer = await dispensing.AmendItemAsync(dispensingId, 1, 30m, null, null);

        fewer.IsSuccess.Should().BeTrue(fewer.Error.Message);
        fewer.Value.WasIssued.Should().BeFalse();

        var item = fewer.Value.State.Items.Single();
        item.Quantity.Should().Be(30m);
        item.Tariff.Should().Be(60m, "priced for thirty as a new item would be");

        (await dispensing.AmendItemAsync(dispensingId, item.LineNo, 30m, 50m, null))
            .Error.Code.Should().Be("DSP-055", "a charge by hand needs a reason");

        var cheaper = await dispensing.AmendItemAsync(dispensingId, item.LineNo,
            30m, 50m, "Price match");

        cheaper.IsSuccess.Should().BeTrue(cheaper.Error.Message);
        cheaper.Value.State.Items.Single().Tariff.Should().Be(50m);
        cheaper.Value.State.CoPaymentTotal.Should().Be(50m);
    }

    [Fact]
    public async Task A_script_from_history_is_used_again_at_what_was_handed_over()
    {
        await _steps.ProductAsync("HAREP", "Repeated capsules 30s", 30m,
            schedule: 3, unitsPerPack: 30m, batches: ("HAREP-A", 12, 5m));

        await _steps.SignInAsync("ha.pharm2", Role.Standard.Pharmacist);
        var patientId = await _steps.PatientAsync("Repeated", "Pat");
        var scriptId = await _steps.ScriptAsync(patientId,
            ("HAREP", 60m, "One capsule twice a day"));
        var dispensingId = await IssueAsync(scriptId);

        using (var scope = _steps.Fixture.NewScope())
        {
            (await PharmaXFixture.Resolve<IDispensingService>(scope)
                    .AmendItemAsync(dispensingId, 1, 28m, null, "Four weeks only"))
                .IsSuccess.Should().BeTrue();
        }

        Guid repeatId;

        using (var scope = _steps.Fixture.NewScope())
        {
            var scripts = PharmaXFixture.Resolve<IScriptService>(scope);

            var draft = await scripts.RepeatAsync(scriptId);

            draft.IsSuccess.Should().BeTrue(draft.Error.Message);
            draft.Value.PatientId.Should().Be(patientId);
            draft.Value.PrescriberName.Should().Be("Dr Test");
            draft.Value.Items.Should().ContainSingle()
                .Which.Should().Be(new ScriptItemEditDto("HAREP", 28m,
                    "One capsule twice a day"), "what was last handed over");

            // Changed before it is captured, like any script.
            var captured = await scripts.CaptureAsync(draft.Value with
            {
                Items = new[] { draft.Value.Items[0] with { Quantity = 56m } }
            });

            captured.IsSuccess.Should().BeTrue(captured.Error.Message);
            repeatId = captured.Value;
        }

        using (var scope = _steps.Fixture.NewScope())
        {
            var supplied = (await PharmaXFixture.Resolve<IScriptService>(scope)
                .SuppliedAsync(patientId)).Value;

            supplied.Select(s => s.Quantity).Should().BeEquivalentTo(new[] { 56m, 28m },
                "the new script as prescribed, the first as handed over");

            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);
            (await db.Scripts.AsNoTracking().SingleAsync(s => s.Id == repeatId))
                .ScriptNumber.Should().NotBe(
                    (await db.Scripts.AsNoTracking().SingleAsync(s => s.Id == scriptId))
                        .ScriptNumber, "it is a new script");
        }
    }

    [Fact]
    public async Task A_tin_is_counted_into_pre_packs_of_their_own()
    {
        var tinId = await _steps.ProductAsync("HATIN", "Bulk tablets 1000", 500m,
            schedule: 3, unitsPerPack: 1000m, batches: ("HATIN-A", 12, 2m));

        await _steps.SignInAsync("ha.assist2", Role.Standard.PharmacistAssistant);

        Guid batchId;

        using (var scope = _steps.Fixture.NewScope())
        {
            batchId = (await PharmaXFixture.Resolve<PharmaXDbContext>(scope).Batches
                .AsNoTracking().SingleAsync(b => b.ProductId == tinId)).Id;
        }

        using (var scope = _steps.Fixture.NewScope())
        {
            var packed = await PharmaXFixture.Resolve<IStockAdjustmentService>(scope)
                .PrepackAsync(new PrepackEditDto("HATIN", batchId, 40m, 25));

            packed.IsSuccess.Should().BeTrue(packed.Error.Message);
            packed.Value.PrePacks.Should().Be(25);
            packed.Value.PrePack!.Quantity.Should().Be(40m, "each label says forty");
            packed.Value.PrePack.BatchNumber.Should().Be("HATIN-A");
        }

        (await _steps.StockOfAsync(tinId)).Should().Be((1m, 1m), "one tin of the two");

        // Again: the pre-pack item made the first time is used.
        using (var scope = _steps.Fixture.NewScope())
        {
            (await PharmaXFixture.Resolve<IStockAdjustmentService>(scope)
                    .PrepackAsync(new PrepackEditDto("HATIN", batchId, 40m, 10)))
                .IsSuccess.Should().BeTrue();
        }

        using (var scope = _steps.Fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

            var prepack = await db.Products.AsNoTracking()
                .SingleAsync(p => p.Code == "HATIN-P40");

            prepack.UnitsPerPack.Should().Be(40m);
            prepack.Schedule.Should().Be(3, "dispensary stock stays dispensary stock");
            prepack.RetailPrice.Amount.Should().Be(20m, "the tin's price, pro rata");
            prepack.IsBatchTracked.Should().BeTrue();

            (await _steps.StockOfAsync(prepack.Id)).Should().Be((35m, 35m));
        }

        (await _steps.StockOfAsync(tinId)).Should().Be((0.6m, 0.6m));
    }

    private async Task<Guid> IssueAsync(Guid scriptId)
    {
        using var scope = _steps.Fixture.NewScope();
        var dispensing = PharmaXFixture.Resolve<IDispensingService>(scope);

        var id = (await dispensing.BeginAsync(scriptId)).Value.Id;
        (await dispensing.AddScriptItemsAsync(id)).IsSuccess.Should().BeTrue();

        var issued = await dispensing.IssueAsync(id);
        issued.IsSuccess.Should().BeTrue(issued.Error.Message);

        return id;
    }
}
