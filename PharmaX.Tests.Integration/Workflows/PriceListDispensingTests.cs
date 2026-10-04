using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Dispensing;
using PharmaX.Domain.Batches;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Security;
using PharmaX.Persistence;
using PharmaX.Persistence.Seeding;

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
        using (var scope = _fixture.NewScope())
            await PharmaXFixture.Resolve<PriceListSeeder>(scope).SeedAsync();

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
