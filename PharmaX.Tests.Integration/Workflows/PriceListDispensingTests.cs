using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Dispensing;
using PharmaX.Domain.Batches;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Security;
using PharmaX.Persistence;
using PharmaX.Domain.Primitives;

namespace PharmaX.Tests.Integration.Workflows;

/// <summary>
/// A medicine from the pharmacy's own price list, dispensed in tablets
/// from a batch, the way the dispensary actually works.
/// </summary>
/// <remarks>
/// Every product the list loaded used to be dispensed as though one
/// tablet were one pack: a script for thirty Losartan was refused
/// against ten packs of a hundred on the shelf (OCT-DEF-01), and the
/// label printed from it had no batch or expiry because the line was not
/// batch-tracked (OCT-DEF-02). Its own fixture, because the list puts
/// seven hundred products into the database.
/// </remarks>
public sealed class PriceListDispensingTests : IDisposable
{
    private readonly PharmaXFixture _fixture = new();
    private readonly WorkflowSteps _steps;

    public PriceListDispensingTests() => _steps = new WorkflowSteps(_fixture);

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Thirty_tablets_of_a_hundred_pack_are_three_tenths_of_a_pack()
    {
        // A hundred-tablet pack at 12.00, as the RSI import brings it in.
        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);
            var losartan = Product.Create("1850", "LOSARTAN VALOSAR 50MG",
                Money.Of(12.00m), 0m, "Z", schedule: 3, isBatchTracked: true);
            losartan.SetUnitsPerPack(100m);
            losartan.SetPackSize("TAB 100");
            db.Products.Add(losartan);
            await db.SaveChangesAsync();
        }

        await _steps.OpenTillAsync();

        // Ten packs of LOSARTAN VALOSAR 50MG, TAB 100 at 12.00 a pack,
        // received onto a batch as a delivery would put them there.
        var expiry = await ReceiveAsync("1850", "LOS-2611", 10m);

        await _steps.SignInAsync("pld.pharm1", Role.Standard.Pharmacist);

        var patientId = await _steps.PatientAsync("Listdispense", "Tendai");
        var scriptId = await _steps.ScriptAsync(patientId,
            ("1850", 30m, "Take ONE tablet daily in the morning"));

        using var dispensing = _fixture.NewScope();
        var service = PharmaXFixture.Resolve<IDispensingService>(dispensing);

        var begun = await service.BeginAsync(scriptId);
        begun.IsSuccess.Should().BeTrue(begun.Error.Message);

        var added = await service.AddScriptItemsAsync(begun.Value.Id);
        added.IsSuccess.Should().BeTrue(added.Error.Message);
        added.Value.Problems.Should().BeEmpty("thirty tablets are well within ten packs of a hundred");

        var line = added.Value.State.Items.Should().ContainSingle().Subject;
        line.Quantity.Should().Be(30m);
        line.Tariff.Should().Be(3.60m, "thirty of a hundred at 12.00 a pack");
        line.BatchNumber.Should().Be("LOS-2611");

        var issued = await service.IssueAsync(begun.Value.Id);
        issued.IsSuccess.Should().BeTrue(issued.Error.Message);

        var label = issued.Value.Labels.Should().ContainSingle().Subject;
        label.BatchNumber.Should().Be("LOS-2611", "the label says which batch went into the bag");
        label.Expiry.Should().Be(expiry);

        var stock = await _steps.StockOfAsync(line.ProductId);
        stock.Product.Should().Be(9.7m, "three tenths of a pack left the shelf");
        stock.Batches.Should().Be(9.7m);
    }

    [Fact]
    public async Task Short_stock_is_owed_kept_and_given_when_it_comes_in()
    {
        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);
            var losartan = Product.Create("1850", "LOSARTAN VALOSAR 50MG",
                Money.Of(12.00m), 0m, "Z", schedule: 3, isBatchTracked: true);
            losartan.SetUnitsPerPack(100m);
            db.Products.Add(losartan);
            await db.SaveChangesAsync();
        }

        await _steps.OpenTillAsync();
        await ReceiveAsync("1850", "LOS-A", 1m);
        await _steps.SignInAsync("pld.pharm2", Role.Standard.Pharmacist);

        var patientId = await _steps.PatientAsync("Owed", "Rudo");
        var scriptId = await _steps.ScriptAsync(patientId,
            ("1850", 130m, "Take ONE tablet daily"));

        // 130 tablets against a pack of 100: the 30 the shelf is short of are owed.
        Guid id;
        using (var scope = _fixture.NewScope())
        {
            var service = PharmaXFixture.Resolve<IDispensingService>(scope);
            id = (await service.BeginAsync(scriptId)).Value.Id;
            var added = await service.AddScriptItemsAsync(id);
            added.Value.Problems.Should().BeEmpty();
            added.Value.State.Items.Should().ContainSingle().Which.Owed.Should().Be(30m);
        }

        // Kept to finish later: the same dispensing comes back, items and all.
        using (var scope = _fixture.NewScope())
        {
            var service = PharmaXFixture.Resolve<IDispensingService>(scope);
            (await service.SuspendedAsync()).Should().ContainSingle(k => k.ScriptId == scriptId);

            var resumed = await service.BeginAsync(scriptId);
            resumed.Value.Id.Should().Be(id);
            resumed.Value.Items.Should().ContainSingle();
        }

        using (var scope = _fixture.NewScope())
        {
            var service = PharmaXFixture.Resolve<IDispensingService>(scope);
            var issued = await service.IssueAsync(id);
            issued.IsSuccess.Should().BeTrue(issued.Error.Message);
            (await service.ToFollowAsync()).Should().ContainSingle().Which.Owed.Should().Be(30m);
        }

        (await _steps.StockOfAsync(await ProductIdAsync("1850"))).Product.Should().Be(0m);

        await ReceiveAsync("1850", "LOS-B", 1m);

        using (var scope = _fixture.NewScope())
        {
            var service = PharmaXFixture.Resolve<IDispensingService>(scope);
            var given = await service.GiveOwedAsync(id, 1);
            given.IsSuccess.Should().BeTrue(given.Error.Message);
            given.Value.Quantity.Should().Be(30m, "the label is for what is handed over now");
            given.Value.BatchNumber.Should().Be("LOS-B");
            (await service.ToFollowAsync()).Should().BeEmpty();
        }

        (await _steps.StockOfAsync(await ProductIdAsync("1850"))).Product.Should().Be(0.7m);
    }

    private async Task<Guid> ProductIdAsync(string code)
    {
        using var scope = _fixture.NewScope();
        return (await PharmaXFixture.Resolve<PharmaXDbContext>(scope).Products.SingleAsync(p => p.Code == code)).Id;
    }

    /// Puts packs on the shelf against a batch, with the receipt that explains them.
    private async Task<DateOnly> ReceiveAsync(string code, string batchNumber,
                                              decimal packs)
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);
        var clock = PharmaXFixture.Resolve<IClock>(scope);

        var product = await db.Products.SingleAsync(p => p.Code == code);
        product.IsBatchTracked.Should().BeTrue("dispensary lines are batch-tracked");

        var expiry = clock.Today.AddMonths(14);
        var batch = Batch.Receive(product.Id, batchNumber, expiry, packs,
                                  product.CostPrice, clock.UtcNow);

        db.Batches.Add(batch);
        product.SetQuantityOnHand(product.QuantityOnHand + packs);
        db.StockMovements.Add(StockMovement.Record(product.Id, batch.Id,
            MovementType.Receipt, packs, product.QuantityOnHand, clock.UtcNow,
            reasonCode: "OPENING"));

        await db.SaveChangesAsync();
        return expiry;
    }
}
