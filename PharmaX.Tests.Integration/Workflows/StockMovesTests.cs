using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Inventory;
using PharmaX.Application.Inventory.Dtos;
using PharmaX.Domain.Finance;
using PharmaX.Domain.Primitives;
using PharmaX.Persistence;

namespace PharmaX.Tests.Integration.Workflows;

/// Stock sent back to the supplier, and a bulk pack counted into pre-packs.
public sealed class StockMovesTests : IDisposable
{
    private readonly PharmaXFixture _fixture = new();
    private readonly WorkflowSteps _steps;

    public StockMovesTests() => _steps = new WorkflowSteps(_fixture);

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task A_bulk_pack_becomes_pre_packs_and_stock_goes_back_to_the_supplier()
    {
        await _steps.OpenTillAsync();

        var bulk = await _steps.ProductAsync("MV-1000", "Paracetamol 500mg 1000s", 90m,
            unitsPerPack: 1000m, batches: new[] { ("PCM-1", 12, 3m) });
        var prePack = await _steps.ProductAsync("MV-20", "Paracetamol 500mg 20s", 5m,
            unitsPerPack: 20m, batches: new[] { ("OLD", 12, 1m) });

        using (var costing = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(costing);
            (await db.Products.SingleAsync(p => p.Id == bulk)).ChangeCostPrice(Money.Of(40m));
            await db.SaveChangesAsync();
        }

        using var scope = _fixture.NewScope();
        var adjust = PharmaXFixture.Resolve<IStockAdjustmentService>(scope);
        var batch = (await adjust.FindAsync("MV-1000")).Value.Batches.Single().BatchId;

        var moved = await adjust.RecordAsync(new StockAdjustmentEditDto(
            StockAdjustmentKind.MoveToItem, "MV-1000", 1m, "Pre-packed for the shop",
            BatchId: batch, OtherBranch: "MV-20"));

        moved.IsSuccess.Should().BeTrue(moved.Error.Message);
        moved.Value.PrePacks.Should().Be(50, "a thousand tablets in twenties");
        moved.Value.PrePack!.BatchNumber.Should().Be("PCM-1");

        (await _steps.StockOfAsync(bulk)).Should().Be((2m, 2m));
        (await _steps.StockOfAsync(prePack)).Should().Be((51m, 51m));

        var returned = await adjust.RecordAsync(new StockAdjustmentEditDto(
            StockAdjustmentKind.ReturnToSupplier, "MV-1000", 1m, "Damaged in transit",
            BatchId: batch, OtherBranch: "Medpro Pharmaceuticals"));

        returned.IsSuccess.Should().BeTrue(returned.Error.Message);
        returned.Value.Summary.Should().Contain("to Medpro Pharmaceuticals");
        (await _steps.StockOfAsync(bulk)).Should().Be((1m, 1m));

        // The supplier owes the pharmacy the pack at cost.
        using var books = _fixture.NewScope();
        var credit = await PharmaXFixture.Resolve<PharmaXDbContext>(books).JournalBatches
            .Include(b => b.Lines).SingleAsync(b => b.Source == "RTS");
        credit.Lines.Should().ContainSingle(l => l.IsDebit && l.AccountCode == LedgerCode.TradeCreditors)
            .Which.Amount.Amount.Should().Be(40m);
    }

    [Fact]
    public async Task An_item_saved_with_a_cost_and_no_price_takes_the_markup()
    {
        await _steps.OpenTillAsync();

        using var scope = _fixture.NewScope();
        var settings = PharmaXFixture.Resolve<IPharmacySettings>(scope);
        await settings.SetAsync(PharmacySettingKey.Markup, "50");
        await settings.SetAsync(PharmacySettingKey.Rounding, "0.05");

        var products = PharmaXFixture.Resolve<IProductService>(scope);
        var created = await products.CreateAsync(new ProductEditDto(
            "MK-1", "Markup test item", null, 0m, 10.02m, 0m, "Z", 0, false, 0m, null));

        created.IsSuccess.Should().BeTrue(created.Error.Message);
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);
        (await db.Products.SingleAsync(p => p.Id == created.Value)).RetailPrice.Amount
            .Should().Be(15.05m, "10.02 and half again is 15.03, rounded up to the 5c");
    }
}
