using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Security;
using PharmaX.Application.Suppliers;
using PharmaX.Application.Suppliers.Dtos;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Security;
using PharmaX.Domain.Suppliers;
using PharmaX.Persistence;

namespace PharmaX.Tests.Integration.Workflows;

/// <summary>
/// Raise an order, receive it against the order, and check every place the
/// delivery should now show: the receipt, the order, the batch, the
/// product, the stock ledger.
/// </summary>
public class StockWorkflowTests : IClassFixture<PharmaXFixture>
{
    private readonly WorkflowSteps _steps;

    public StockWorkflowTests(PharmaXFixture fixture) =>
        _steps = new WorkflowSteps(fixture);

    [Theory]
    [InlineData("stk.controller", Role.Standard.StockController)]
    [InlineData("stk.assistant", Role.Standard.PharmacistAssistant)]
    [InlineData("stk.manager", Role.Standard.Manager)]
    [InlineData("stk.admin", Role.Standard.Administrator)]
    public async Task A_delivery_received_against_its_order_updates_everything(
        string username, string role)
    {
        await _steps.OpenTillAsync();

        var code = "GR" + username[4..7].ToUpperInvariant();
        var productId = await _steps.ProductAsync(code,
            $"Received capsules 21s ({role})", 100m, schedule: 4,
            batches: new[] { ($"{code}-OLD", 3, 5m) });

        var supplierId = await SupplierAsync();

        // Raised by the buyer.
        await _steps.SignInAsync("stk.buyer", Role.Standard.StockController);

        PurchaseOrderDto order;

        using (var scope = _steps.Fixture.NewScope())
        {
            var purchasing = PharmaXFixture.Resolve<IPurchasingService>(scope);

            var raised = await purchasing.RaiseAsync(new PurchaseOrderEditDto(
                supplierId, null,
                new[] { new PurchaseOrderLineEditDto(code, 60m, 55m) }));

            raised.IsSuccess.Should().BeTrue(raised.Error.Message);
            order = raised.Value;
        }

        order.OrderNumber.Should().StartWith("PO-");
        order.Lines.Single().ProductCode.Should().Be(code,
            "the order line carries the product's own code");

        // Received by the role under test, working from the order: the
        // line's product identity and code, not its description.
        await _steps.SignInAsync(username, role);

        var expiry = DateOnly.FromDateTime(DateTime.Today.AddMonths(20));
        var line = order.Lines.Single();

        var edit = new GoodsReceiptEditDto(supplierId, order.Id, "INV-1",
            DateOnly.FromDateTime(DateTime.Today), 3300m,
            new[]
            {
                new GoodsReceiptLineEditDto(line.ProductCode, 60m, 55m,
                    $"{code}-NEW", expiry, line.LineNo, line.ProductId)
            });

        GoodsReceiptDto receipt;

        using (var scope = _steps.Fixture.NewScope())
        {
            var receiving = PharmaXFixture.Resolve<IReceivingService>(scope);

            var preview = await receiving.PreviewAsync(edit);
            preview.IsSuccess.Should().BeTrue(preview.Error.Message);
            preview.Value.Single().ProductId.Should().Be(productId);
            preview.Value.Single().ProductCode.Should().Be(code);

            var received = await receiving.ReceiveAsync(edit);
            received.IsSuccess.Should().BeTrue(received.Error.Message);
            receipt = received.Value;
        }

        receipt.ReceiptNumber.Should().StartWith("G-");

        using var check = _steps.Fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(check);

        (await db.GoodsReceipts.AsNoTracking()
                .AnyAsync(r => r.Id == receipt.Id && r.IsPosted))
            .Should().BeTrue("the receipt is persisted and posted");

        var storedOrder = await db.PurchaseOrders.AsNoTracking()
            .Include(o => o.Lines).SingleAsync(o => o.Id == order.Id);
        storedOrder.Status.Should().Be(PurchaseOrderStatus.Received);
        storedOrder.Lines.Single().QuantityReceived.Should().Be(60m);

        var batch = await db.Batches.AsNoTracking()
            .SingleAsync(b => b.BatchNumber == $"{code}-NEW");
        batch.ProductId.Should().Be(productId);
        batch.QuantityOnHand.Should().Be(60m);
        batch.ExpiryDate.Should().Be(expiry);

        var (onHand, inBatches) = await _steps.StockOfAsync(productId);
        onHand.Should().Be(65m);
        inBatches.Should().Be(65m, "the product and its batches agree");

        var movement = await db.StockMovements.AsNoTracking()
            .SingleAsync(m => m.SourceDocumentId == receipt.Id);
        movement.MovementType.Should().Be(MovementType.Receipt);
        movement.Quantity.Should().Be(60m);
        movement.BatchId.Should().Be(batch.Id);
        movement.BalanceAfter.Should().Be(65m);
    }

    // The pharmacist used to be the second case here; the dispensary has
    // received its own deliveries since 16 October 2026.
    [Theory]
    [InlineData("stk.cashier", Role.Standard.Cashier)]
    public async Task Goods_receipt_is_refused_to_roles_without_it(
        string username, string role)
    {
        await _steps.OpenTillAsync();
        await _steps.ProductAsync("GRDENY", "Denied item", 10m);
        var supplierId = await SupplierAsync();

        await _steps.SignInAsync(username, role);

        var edit = new GoodsReceiptEditDto(supplierId, null, "INV-X",
            DateOnly.FromDateTime(DateTime.Today), null,
            new[] { new GoodsReceiptLineEditDto("GRDENY", 1m, 5m, null, null) });

        using var scope = _steps.Fixture.NewScope();
        var receiving = PharmaXFixture.Resolve<IReceivingService>(scope);

        await ((Func<Task>)(() => receiving.PreviewAsync(edit)))
            .Should().ThrowAsync<PermissionDeniedException>();
        await ((Func<Task>)(() => receiving.ReceiveAsync(edit)))
            .Should().ThrowAsync<PermissionDeniedException>();
        await ((Func<Task>)(() => receiving.IdentifyAsync(edit)))
            .Should().ThrowAsync<PermissionDeniedException>();
    }

    /// <summary>
    /// A line that names an order line but a different product is refused
    /// by name, rather than posting the wrong stock.
    /// </summary>
    [Fact]
    public async Task A_receipt_line_that_does_not_match_its_order_line_is_refused()
    {
        await _steps.OpenTillAsync();
        await _steps.ProductAsync("GRMATA", "Ordered item", 10m);
        await _steps.ProductAsync("GRMATB", "Something else", 10m);
        var supplierId = await SupplierAsync();

        await _steps.SignInAsync("stk.ctl2", Role.Standard.StockController);

        using var scope = _steps.Fixture.NewScope();
        var purchasing = PharmaXFixture.Resolve<IPurchasingService>(scope);
        var receiving = PharmaXFixture.Resolve<IReceivingService>(scope);

        var order = (await purchasing.RaiseAsync(new PurchaseOrderEditDto(
            supplierId, null,
            new[] { new PurchaseOrderLineEditDto("GRMATA", 5m, 6m) }))).Value;

        var wrong = await receiving.PreviewAsync(new GoodsReceiptEditDto(
            supplierId, order.Id, null, DateOnly.FromDateTime(DateTime.Today),
            null, new[]
            {
                new GoodsReceiptLineEditDto("GRMATB", 5m, 6m, null, null,
                    order.Lines[0].LineNo)
            }));

        wrong.IsFailure.Should().BeTrue();
        wrong.Error.Code.Should().Be("GRN-015");
        wrong.Error.Message.Should().Contain("Ordered item");
    }

    /// <summary>
    /// Every line is named for what its code is while another line still
    /// stops the receipt posting: the screen shows this beside the code,
    /// and it used to show the product a line had named before.
    /// </summary>
    [Fact]
    public async Task Each_code_is_identified_while_another_line_has_a_problem()
    {
        await _steps.OpenTillAsync();
        var plainId = await _steps.ProductAsync("GRIDA", "Plain item", 20m);
        await _steps.ProductAsync("GRIDB", "Batched item", 50m,
            batches: new[] { ("GRIDB-1", 12, 4m) });
        var supplierId = await SupplierAsync();

        await _steps.SignInAsync("stk.ctl3", Role.Standard.StockController);

        using var scope = _steps.Fixture.NewScope();
        var receiving = PharmaXFixture.Resolve<IReceivingService>(scope);

        var edit = new GoodsReceiptEditDto(supplierId, null, null,
            DateOnly.FromDateTime(DateTime.Today), null, new[]
            {
                new GoodsReceiptLineEditDto("GRIDA", 2m, 0m, null, null),
                new GoodsReceiptLineEditDto("GRIDB", 3m, 0m, null, null),
                new GoodsReceiptLineEditDto("NOSUCH", 1m, 0m, null, null)
            });

        (await receiving.PreviewAsync(edit)).IsFailure.Should().BeTrue(
            "the batched line has no batch and the last code names nothing");

        var identified = await receiving.IdentifyAsync(edit);

        identified.IsSuccess.Should().BeTrue();
        identified.Value.Should().HaveCount(3, "a line per captured line");
        identified.Value[0]!.ProductId.Should().Be(plainId);
        identified.Value[0]!.CostPrice.Should().Be(12m);
        identified.Value[1]!.Description.Should().Be("Batched item");
        identified.Value[1]!.IsBatchTracked.Should().BeTrue();
        identified.Value[2].Should().BeNull();
    }

    /// <summary>
    /// DEF-0010: every kind of document has its own prefix, so a script
    /// number can never be mistaken for a sale's, nor a purchase order for
    /// a patient file.
    /// </summary>
    [Fact]
    public async Task Document_numbers_are_distinct_across_document_types()
    {
        using var scope = _steps.Fixture.NewScope();
        var numbering = PharmaXFixture.Resolve<IDocumentNumbering>(scope);

        var numbers = new List<string>();

        foreach (var series in DocumentSeries.All)
            numbers.Add(await numbering.NextAsync(series));

        numbers.Select(n => n[..n.IndexOf('-', StringComparison.Ordinal)])
            .Should().OnlyHaveUniqueItems();
        numbers.Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// Many callers at once - separate scopes, as separate screens or
    /// separate processes would be - and no number is handed out twice.
    /// </summary>
    [Fact]
    public async Task Concurrent_callers_never_receive_the_same_number()
    {
        var provider = _steps.Fixture;

        async Task<List<string>> TakeAsync(int count)
        {
            var taken = new List<string>();

            for (var i = 0; i < count; i++)
            {
                using var scope = provider.NewScope();
                var numbering = PharmaXFixture.Resolve<IDocumentNumbering>(scope);
                taken.Add(await numbering.NextAsync(DocumentSeries.Quotation));
            }

            return taken;
        }

        var all = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() => TakeAsync(15))));

        all.SelectMany(n => n).Should().HaveCount(60).And
            .OnlyHaveUniqueItems();
    }

    private async Task<Guid> SupplierAsync()
    {
        using var scope = _steps.Fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        var existing = await db.Suppliers.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Code == "WFSUP");

        if (existing is not null) return existing.Id;

        var supplier = Supplier.Create("WFSUP", "Workflow Wholesale", 30, 2);
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        return supplier.Id;
    }
}
