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
/// The product editor behind the list's Edit button changes what the
/// list's cells change: the code, whether it is still sold, the quantity
/// on hand, and a batch at a time the batch number, the expiry and the
/// quantity - all in one save.
/// </summary>
public class ProductEditorStockTests : IClassFixture<PharmaXFixture>
{
    private readonly PharmaXFixture _fixture;

    public ProductEditorStockTests(PharmaXFixture fixture) =>
        _fixture = fixture;

    [Fact]
    public async Task Ticking_batch_tracked_with_loose_stock_and_no_batch_is_refused()
    {
        await SignInAsync();

        var id = await CreateAsync("PES-TRKSTK");
        (await UpdateAsync(id, Edit(await FetchAsync(id)) with { QuantityOnHand = 6m }))
            .IsSuccess.Should().BeTrue();

        var saved = await UpdateAsync(id, Edit(await FetchAsync(id)) with
        {
            IsBatchTracked = true
        });

        saved.Error.Code.Should().Be("INV-037");
        (await FetchAsync(id)).IsBatchTracked.Should().BeFalse();
    }

    [Fact]
    public async Task The_editor_renumbers_discontinues_and_counts_a_product()
    {
        await SignInAsync();

        var id = await CreateAsync("PES-CODE");
        var product = await FetchAsync(id);

        var saved = await UpdateAsync(id, Edit(product) with
        {
            Code = "pes-renumbered",
            IsActive = false,
            QuantityOnHand = 7m
        });

        saved.IsSuccess.Should().BeTrue(saved.Error.Message);

        var updated = await FetchAsync(id);

        updated.Code.Should().Be("PES-RENUMBERED");
        updated.IsActive.Should().BeFalse();
        updated.QuantityOnHand.Should().Be(7m);

        var movement = (await MovementsAsync(id)).Should().ContainSingle()
            .Subject;

        movement.MovementType.Should().Be(MovementType.Adjustment);
        movement.Quantity.Should().Be(7m);
        movement.BalanceAfter.Should().Be(7m);
    }

    /// <remarks>
    /// Opening the form and saving it without touching the stock must not
    /// post anything: the service is sent the batches as they were shown.
    /// </remarks>
    [Fact]
    public async Task Saving_the_stock_as_it_was_shown_moves_nothing()
    {
        await SignInAsync();

        var id = await CreateAsync("PES-SAME");
        await GivenBatchesAsync(id, ("PES-SAME-1", 20, 4m),
            ("PES-SAME-2", 200, 6m));

        var product = await FetchAsync(id);

        var saved = await UpdateAsync(id, Edit(product) with
        {
            Batches = Rows(product)
        });

        saved.IsSuccess.Should().BeTrue(saved.Error.Message);
        (await MovementsAsync(id)).Should().BeEmpty();
        (await FetchAsync(id)).Batches.Should()
            .BeEquivalentTo(product.Batches);
    }

    [Fact]
    public async Task The_editor_renames_redates_and_recounts_each_batch()
    {
        await SignInAsync();

        var id = await CreateAsync("PES-EACH");
        var ids = await GivenBatchesAsync(id, ("PES-EACH-1", 20, 4m),
            ("PES-EACH-2", 200, 6m));

        var product = await FetchAsync(id);

        product.Batches.Should().HaveCount(2);
        product.Batches![0].BatchId.Should().Be(ids[0], "earliest first");
        product.Batches[0].DaysToExpiry.Should().Be(20);

        var today = Clock().Today;

        var saved = await UpdateAsync(id, Edit(product) with
        {
            Batches = new[]
            {
                new ProductBatchEditDto(ids[0], "PES-EACH-1A",
                    today.AddDays(10), 1m),
                new ProductBatchEditDto(ids[1], "PES-EACH-2",
                    today.AddDays(200), 9m)
            }
        });

        saved.IsSuccess.Should().BeTrue(saved.Error.Message);

        var updated = await FetchAsync(id);

        updated.QuantityOnHand.Should().Be(10m, "4 became 1 and 6 became 9");
        updated.Batches.Should().HaveCount(2);

        var first = updated.Batches!.Single(b => b.BatchId == ids[0]);
        first.BatchNumber.Should().Be("PES-EACH-1A");
        first.Expiry.Should().Be(today.AddDays(10));
        first.DaysToExpiry.Should().Be(10);
        first.QuantityOnHand.Should().Be(1m);

        updated.Batches!.Single(b => b.BatchId == ids[1])
            .QuantityOnHand.Should().Be(9m);

        var movements = await MovementsAsync(id);

        movements.Should().HaveCount(2, "one count per batch that changed");
        movements.Select(m => m.Quantity).Should().BeEquivalentTo(
            new[] { -3m, 3m });
        movements.Should().OnlyContain(m => m.BatchId != null);
    }

    /// <remarks>
    /// Stock counted before the product was batch tracked is already on
    /// the shelf. Giving it a batch is not a delivery; only what the new
    /// batch holds beyond it is a count.
    /// </remarks>
    [Fact]
    public async Task A_new_batch_takes_over_the_loose_stock_and_counts_the_rest()
    {
        await SignInAsync();

        var id = await CreateAsync("PES-LOOSE");
        var product = await FetchAsync(id);

        (await UpdateAsync(id, Edit(product) with { QuantityOnHand = 6m }))
            .IsSuccess.Should().BeTrue();

        product = await FetchAsync(id);

        var expiry = Clock().Today.AddDays(90);

        var saved = await UpdateAsync(id, Edit(product) with
        {
            Batches = new[]
            {
                new ProductBatchEditDto(null, "PES-LOOSE-1", expiry, 8m)
            }
        });

        saved.IsSuccess.Should().BeTrue(saved.Error.Message);

        var updated = await FetchAsync(id);

        updated.QuantityOnHand.Should().Be(8m);
        updated.IsBatchTracked.Should().BeTrue(
            "stock on a batch is sold by batch");

        var batch = updated.Batches.Should().ContainSingle().Subject;
        batch.BatchNumber.Should().Be("PES-LOOSE-1");
        batch.Expiry.Should().Be(expiry);
        batch.QuantityOnHand.Should().Be(8m);

        var counts = await MovementsAsync(id);

        counts.Should().HaveCount(2);
        counts[^1].Quantity.Should().Be(2m, "6 were already there");
        counts[^1].BatchId.Should().Be(batch.BatchId);
        counts[^1].BalanceAfter.Should().Be(8m);
    }

    [Fact]
    public async Task A_refused_row_leaves_the_whole_product_as_it_was()
    {
        await SignInAsync();

        var id = await CreateAsync("PES-KEEP");
        var ids = await GivenBatchesAsync(id, ("PES-KEEP-1", 30, 2m),
            ("PES-KEEP-2", 60, 3m));

        var product = await FetchAsync(id);
        var today = Clock().Today;

        var duplicate = await UpdateAsync(id, Edit(product) with
        {
            Description = "Should not be kept",
            Batches = new[]
            {
                new ProductBatchEditDto(ids[0], "PES-KEEP-2",
                    today.AddDays(30), 2m)
            }
        });

        var negative = await UpdateAsync(id, Edit(product) with
        {
            Batches = new[]
            {
                new ProductBatchEditDto(ids[0], "PES-KEEP-1",
                    today.AddDays(30), -1m)
            }
        });

        var noExpiry = await UpdateAsync(id, Edit(product) with
        {
            Batches = new[]
            {
                new ProductBatchEditDto(null, "PES-KEEP-3", null, 1m)
            }
        });

        var onBatches = await UpdateAsync(id, Edit(product) with
        {
            QuantityOnHand = 99m
        });

        duplicate.Error.Message.Should().Contain("already on this product");
        negative.IsFailure.Should().BeTrue();
        noExpiry.Error.Message.Should().Contain("expiry");
        onBatches.Error.Message.Should().Contain("each batch");

        var unchanged = await FetchAsync(id);

        unchanged.Description.Should().Be(product.Description);
        unchanged.QuantityOnHand.Should().Be(5m);
        unchanged.Batches.Should().BeEquivalentTo(product.Batches);
        (await MovementsAsync(id)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_code_another_product_answers_to_is_refused()
    {
        await SignInAsync();

        var id = await CreateAsync("PES-MINE");
        await CreateAsync("PES-THEIRS");

        var product = await FetchAsync(id);

        var refused = await UpdateAsync(id, Edit(product) with
        {
            Code = "PES-THEIRS"
        });

        refused.Error.Message.Should().Contain("already in use");
        (await FetchAsync(id)).Code.Should().Be("PES-MINE");
    }

    /// <remarks>
    /// The list keeps one scope for as long as it is open, and the editor
    /// saves through a scope of its own. The list then has to show what
    /// was saved, and its own cells must count from it rather than from
    /// the copy it read first - which the row version would refuse.
    /// </remarks>
    [Fact]
    public async Task A_list_left_open_sees_the_editors_save_and_counts_from_it()
    {
        await SignInAsync();

        var id = await CreateAsync("PES-OPEN");
        var batchId = (await GivenBatchesAsync(id, ("PES-OPEN-1", 50, 5m)))[0];

        using var list = _fixture.NewScope();
        var listed = PharmaXFixture.Resolve<IProductService>(list);

        (await listed.ApplyCellEditAsync(new ProductCellEditDto(id, batchId,
            ProductCell.QuantityOnHand, "4", "Counted"))).IsSuccess.Should().BeTrue();

        var product = await FetchAsync(id);

        (await UpdateAsync(id, Edit(product) with
        {
            Batches = new[]
            {
                new ProductBatchEditDto(batchId, "PES-OPEN-1",
                    product.Batches![0].Expiry, 9m)
            }
        })).IsSuccess.Should().BeTrue();

        var rows = await listed.SearchBatchesAsync("PES-OPEN");
        rows.Value.Items.Should().ContainSingle()
            .Which.QuantityOnHand.Should().Be(9m, "the editor saved 9");

        var counted = await listed.ApplyCellEditAsync(new ProductCellEditDto(
            id, batchId, ProductCell.QuantityOnHand, "7", "Two broken"));

        counted.IsSuccess.Should().BeTrue(counted.Error.Message);

        (await FetchAsync(id)).QuantityOnHand.Should().Be(7m);
        (await MovementsAsync(id)).Select(m => m.Quantity).Should()
            .BeEquivalentTo(new[] { -1m, 5m, -2m },
                "5 to 4 on the list, 4 to 9 in the editor, 9 to 7 on the list");
    }

    // ---- Steps -----------------------------------------------------------

    private static ProductEditDto Edit(ProductDto p) => new(
        Code: p.Code,
        Description: p.Description,
        GenericName: p.GenericName,
        RetailPrice: p.RetailPrice,
        CostPrice: p.CostPrice,
        VatRatePercent: p.VatRatePercent,
        TaxCode: p.TaxCode,
        Schedule: p.Schedule,
        IsBatchTracked: p.IsBatchTracked,
        ReorderLevel: p.ReorderLevel,
        PackSize: p.PackSize,
        DepartmentId: p.DepartmentId,
        CategoryId: p.CategoryId,
        ManufacturerId: p.ManufacturerId,
        UnitsPerPack: p.UnitsPerPack,
        Barcodes: p.Barcodes,
        StockReason: "Counted on the shelf");

    private static List<ProductBatchEditDto> Rows(ProductDto p) =>
        (p.Batches ?? Array.Empty<ProductBatchDto>())
        .Select(b => new ProductBatchEditDto(b.BatchId, b.BatchNumber,
            b.Expiry, b.QuantityOnHand))
        .ToList();

    private async Task<Guid> CreateAsync(string code)
    {
        using var scope = _fixture.NewScope();
        var products = PharmaXFixture.Resolve<IProductService>(scope);

        var created = await products.CreateAsync(new ProductEditDto(
            Code: code,
            Description: $"{code} item",
            GenericName: null,
            RetailPrice: 5m,
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

    /// <summary>
    /// Batches straight into the database, the way a delivery leaves
    /// them, with the product's balance to match.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> GivenBatchesAsync(Guid productId,
        params (string Number, int Days, decimal Quantity)[] batches)
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);
        var clock = PharmaXFixture.Resolve<IClock>(scope);
        var product = await db.Products.SingleAsync(p => p.Id == productId);

        product.SetBatchTracked(true);
        product.SetQuantityOnHand(batches.Sum(b => b.Quantity));

        var added = batches
            .Select(b => Batch.Receive(productId, b.Number,
                clock.Today.AddDays(b.Days), b.Quantity, Money.Of(1m),
                clock.UtcNow))
            .ToList();

        db.Batches.AddRange(added);
        await db.SaveChangesAsync();

        return added.Select(b => b.Id).ToList();
    }

    private async Task<Application.Results.Result> UpdateAsync(Guid id,
        ProductEditDto edit)
    {
        using var scope = _fixture.NewScope();
        var products = PharmaXFixture.Resolve<IProductService>(scope);

        return await products.UpdateAsync(id, edit);
    }

    private async Task<ProductDto> FetchAsync(Guid id)
    {
        using var scope = _fixture.NewScope();
        var products = PharmaXFixture.Resolve<IProductService>(scope);
        var fetched = await products.GetAsync(id);

        fetched.IsSuccess.Should().BeTrue(fetched.Error.Message);
        return fetched.Value;
    }

    private async Task<IReadOnlyList<StockMovement>> MovementsAsync(Guid id)
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        var movements = await db.StockMovements
            .Where(m => m.ProductId == id)
            .ToListAsync();

        return movements.OrderBy(m => m.OccurredAtUtc)
            .ThenBy(m => m.BalanceAfter)
            .ToList();
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
