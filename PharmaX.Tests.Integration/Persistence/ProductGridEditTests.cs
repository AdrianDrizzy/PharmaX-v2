using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Inventory;
using PharmaX.Application.Inventory.Dtos;
using PharmaX.Application.Security;
using PharmaX.Domain.Batches;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Primitives;
using PharmaX.Persistence;

namespace PharmaX.Tests.Integration.Persistence;

/// <summary>
/// The product list commits a cell where it was typed: the code, the
/// description, the price, the quantity, the schedule, whether it is
/// batch tracked, whether it is still sold, and — a batch at a time —
/// the batch number, the expiry and the quantity of that batch.
/// </summary>
public class ProductGridEditTests : IClassFixture<PharmaXFixture>
{
    private readonly PharmaXFixture _fixture;

    public ProductGridEditTests(PharmaXFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_cell_edits_the_catalogue_fields_the_list_shows()
    {
        await SignInAsync();

        var id = await CreateAsync("PGE-LIST", "Original name", 9.50m);

        await EditAsync(id, ProductCell.Description, "Renamed on the list");
        await EditAsync(id, ProductCell.Code, "pge-renamed");
        await EditAsync(id, ProductCell.RetailPrice, "12.50");
        await EditAsync(id, ProductCell.Schedule, "2");
        await EditAsync(id, ProductCell.BatchTracked, "true");
        await EditAsync(id, ProductCell.Active, "false");

        var product = await FetchAsync(id);

        product.Code.Should().Be("PGE-RENAMED");
        product.Description.Should().Be("Renamed on the list");
        product.RetailPrice.Should().Be(12.50m);
        product.Schedule.Should().Be(2);
        product.IsBatchTracked.Should().BeTrue();
        product.IsActive.Should().BeFalse();

        await EditAsync(id, ProductCell.Active, "true");
        (await FetchAsync(id)).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Quantity_on_hand_is_a_stock_movement()
    {
        await SignInAsync();

        var id = await CreateAsync("PGE-QTY", "Counted item", 5m);

        await EditAsync(id, ProductCell.QuantityOnHand, "6");

        (await FetchAsync(id)).QuantityOnHand.Should().Be(6m);

        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        var movement = await db.StockMovements
            .SingleAsync(m => m.ProductId == id);

        movement.MovementType.Should().Be(MovementType.Adjustment);
        movement.Quantity.Should().Be(6m);
        movement.BalanceAfter.Should().Be(6m);
        movement.ReasonCode.Should().Be("COUNT-OVER: Counted on the she",
            "over or short, then why, to the thirty the column holds");

        var audit = await db.AuditEntries
            .Where(a => a.EntityId == id && a.Action == "inv.stock.adjust")
            .SingleAsync();

        audit.Reason.Should().Be("PGE-QTY on hand 0 to 6. Counted on the shelf");
    }

    [Fact]
    public async Task A_quantity_with_no_reason_is_refused_and_moves_nothing()
    {
        await SignInAsync();

        var id = await CreateAsync("PGE-WHY", "Needs a reason", 5m);

        using (var scope = _fixture.NewScope())
        {
            var products = PharmaXFixture.Resolve<IProductService>(scope);

            var refused = await products.ApplyCellEditAsync(
                new ProductCellEditDto(id, null, ProductCell.QuantityOnHand, "6", "  "));

            refused.IsFailure.Should().BeTrue();
            refused.Error.Code.Should().Be("INV-033");
        }

        (await FetchAsync(id)).QuantityOnHand.Should().Be(0m);

        using var check = _fixture.NewScope();
        (await PharmaXFixture.Resolve<PharmaXDbContext>(check).StockMovements
            .AnyAsync(m => m.ProductId == id)).Should().BeFalse();
    }

    [Fact]
    public async Task A_batch_row_renames_redates_and_recounts_that_batch()
    {
        await SignInAsync();

        var id = await CreateAsync("PGE-BATCH", "Batched item", 8m);
        await EditAsync(id, ProductCell.QuantityOnHand, "6");

        var clock = Clock();
        var expiry = clock.Today.AddDays(40).ToString("yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture);

        await EditAsync(id, ProductCell.OpenBatch, "PGE-LOT", extra: expiry);

        var row = (await SearchBatchesAsync("PGE-BATCH")).Should()
            .ContainSingle().Subject;

        row.BatchId.Should().NotBeNull();
        row.BatchNumber.Should().Be("PGE-LOT");
        row.QuantityOnHand.Should().Be(6m);
        row.DaysToExpiry.Should().Be(40);

        await EditAsync(id, ProductCell.BatchNumber, "PGE-LOT-2",
            batchId: row.BatchId);
        await EditAsync(id, ProductCell.DaysLeft, "10", batchId: row.BatchId);
        await EditAsync(id, ProductCell.QuantityOnHand, "4",
            batchId: row.BatchId);

        var updated = (await SearchBatchesAsync("PGE-BATCH")).Should()
            .ContainSingle().Subject;

        updated.BatchNumber.Should().Be("PGE-LOT-2");
        updated.DaysToExpiry.Should().Be(10);
        updated.Expiry.Should().Be(clock.Today.AddDays(10));
        updated.QuantityOnHand.Should().Be(4m);
        (await FetchAsync(id)).QuantityOnHand.Should().Be(4m);
    }

    [Fact]
    public async Task A_product_split_across_batches_is_counted_per_batch()
    {
        await SignInAsync();

        var id = await CreateAsync("PGE-SPLIT", "Split item", 4m);
        Guid near;

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);
            var clock = PharmaXFixture.Resolve<IClock>(scope);
            var product = await db.Products.SingleAsync(p => p.Id == id);

            product.SetBatchTracked(true);
            product.SetQuantityOnHand(10m);

            var first = Batch.Receive(id, "PGE-NEAR", clock.Today.AddDays(20),
                4m, Money.Of(1m), clock.UtcNow);
            var second = Batch.Receive(id, "PGE-FAR", clock.Today.AddYears(1),
                6m, Money.Of(1m), clock.UtcNow);

            db.Batches.AddRange(first, second);
            await db.SaveChangesAsync();
            near = first.Id;
        }

        var refused = await TryEditAsync(id, ProductCell.QuantityOnHand, "9");

        refused.IsFailure.Should().BeTrue();
        refused.Error.Message.Should().Contain("each batch");
        (await FetchAsync(id)).QuantityOnHand.Should().Be(10m);

        await EditAsync(id, ProductCell.QuantityOnHand, "2", batchId: near);

        (await FetchAsync(id)).QuantityOnHand.Should().Be(8m);

        var rows = await SearchBatchesAsync("PGE-SPLIT");
        rows.Single(r => r.BatchId == near).QuantityOnHand.Should().Be(2m);
        rows.Single(r => r.BatchId != near).QuantityOnHand.Should().Be(6m);
    }

    [Fact]
    public async Task A_bad_cell_is_refused_and_leaves_the_product_as_it_was()
    {
        await SignInAsync();

        var id = await CreateAsync("PGE-KEEP", "Stays put", 3m);
        await CreateAsync("PGE-TAKEN", "Already used", 3m);

        var duplicate = await TryEditAsync(id, ProductCell.Code, "PGE-TAKEN");
        var schedule = await TryEditAsync(id, ProductCell.Schedule, "9");
        var negative = await TryEditAsync(id, ProductCell.QuantityOnHand, "-1");
        var price = await TryEditAsync(id, ProductCell.RetailPrice, "-2");

        duplicate.IsFailure.Should().BeTrue();
        schedule.IsFailure.Should().BeTrue();
        negative.IsFailure.Should().BeTrue();
        price.IsFailure.Should().BeTrue();

        var product = await FetchAsync(id);

        product.Code.Should().Be("PGE-KEEP");
        product.Schedule.Should().Be(0);
        product.QuantityOnHand.Should().Be(0m);
        product.RetailPrice.Should().Be(3m);
    }

    [Fact]
    public async Task Batch_tracking_stays_on_while_a_batch_holds_stock()
    {
        await SignInAsync();

        var id = await CreateAsync("PGE-HELD", "Held on a batch", 2m);
        await EditAsync(id, ProductCell.QuantityOnHand, "3");
        await EditAsync(id, ProductCell.OpenBatch, "PGE-HELD-1",
            extra: Clock().Today.AddMonths(6).ToString("yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture));

        var refused = await TryEditAsync(id, ProductCell.BatchTracked, "false");

        refused.IsFailure.Should().BeTrue();
        (await FetchAsync(id)).IsBatchTracked.Should().BeTrue();
    }

    // ---- Steps -----------------------------------------------------------

    private async Task<Guid> CreateAsync(string code, string description,
        decimal retail)
    {
        using var scope = _fixture.NewScope();
        var products = PharmaXFixture.Resolve<IProductService>(scope);

        var created = await products.CreateAsync(new ProductEditDto(
            Code: code,
            Description: description,
            GenericName: null,
            RetailPrice: retail,
            CostPrice: 1m,
            VatRatePercent: 15m,
            TaxCode: "A",
            Schedule: 0,
            IsBatchTracked: false,
            ReorderLevel: 0m,
            PackSize: null));

        created.IsSuccess.Should().BeTrue(created.Error.Message);
        return created.Value;
    }

    private Task EditAsync(Guid id, ProductCell column, string value,
        Guid? batchId = null, string? extra = null) =>
        ApplyAsync(id, column, value, batchId, extra, expectSuccess: true);

    private async Task<Application.Results.Result> TryEditAsync(Guid id,
        ProductCell column, string value)
    {
        using var scope = _fixture.NewScope();
        var products = PharmaXFixture.Resolve<IProductService>(scope);

        return await products.ApplyCellEditAsync(
            new ProductCellEditDto(id, null, column, value));
    }

    /// <remarks>
    /// A quantity is sent with the reason the list asks for, as the list
    /// sends it; a refusal for having none is tested on its own.
    /// </remarks>
    private async Task ApplyAsync(Guid id, ProductCell column, string value,
        Guid? batchId, string? extra, bool expectSuccess)
    {
        using var scope = _fixture.NewScope();
        var products = PharmaXFixture.Resolve<IProductService>(scope);

        if (column == ProductCell.QuantityOnHand) extra ??= "Counted on the shelf";

        var result = await products.ApplyCellEditAsync(
            new ProductCellEditDto(id, batchId, column, value, extra));

        if (expectSuccess)
            result.IsSuccess.Should().BeTrue(result.Error.Message);
    }

    private async Task<ProductDto> FetchAsync(Guid id)
    {
        using var scope = _fixture.NewScope();
        var products = PharmaXFixture.Resolve<IProductService>(scope);
        var fetched = await products.GetAsync(id);

        fetched.IsSuccess.Should().BeTrue(fetched.Error.Message);
        return fetched.Value;
    }

    private async Task<IReadOnlyList<ProductBatchListItemDto>> SearchBatchesAsync(
        string term)
    {
        using var scope = _fixture.NewScope();
        var products = PharmaXFixture.Resolve<IProductService>(scope);
        var result = await products.SearchBatchesAsync(term);

        result.IsSuccess.Should().BeTrue(result.Error.Message);
        return result.Value.Items;
    }

    private IClock Clock()
    {
        using var scope = _fixture.NewScope();
        return PharmaXFixture.Resolve<IClock>(scope);
    }

    private async Task SignInAsync()
    {
        using var scope = _fixture.NewScope();
        var auth = PharmaXFixture.Resolve<IAuthenticationService>(scope);

        var result = await auth.AuthenticateAsync("admin", "ChangeMe#1");
        result.IsSuccess.Should().BeTrue(result.Error.Message);
    }
}
