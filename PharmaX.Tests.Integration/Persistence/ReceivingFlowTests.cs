using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Sales;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Application.Security;
using PharmaX.Application.Suppliers;
using PharmaX.Application.Suppliers.Dtos;
using PharmaX.Application.Tills;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Primitives;
using PharmaX.Persistence;

namespace PharmaX.Tests.Integration.Persistence;

/// <summary>
/// Receiving is where batch numbers and expiry dates enter the system,
/// so it is where FEFO either becomes possible or does not. These tests
/// take stock in through the service and then sell it, because the only
/// proof that a receipt worked is that the till can find what it
/// created.
/// </summary>
public class ReceivingFlowTests : IClassFixture<PharmaXFixture>
{
    private readonly PharmaXFixture _fixture;

    public ReceivingFlowTests(PharmaXFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_receipt_creates_a_batch_that_the_till_then_sells()
    {
        await SignInAsync();

        var supplierId = await GivenASupplierAsync("ACME", "Acme Wholesale");
        var code = await GivenAProductAsync("VITC1000", "Vitamin C 1000mg",
                                            batchTracked: true);

        using (var scope = _fixture.NewScope())
        {
            var receiving = PharmaXFixture.Resolve<IReceivingService>(scope);
            var clock = PharmaXFixture.Resolve<IClock>(scope);

            var result = await receiving.ReceiveAsync(new GoodsReceiptEditDto(
                SupplierId: supplierId,
                PurchaseOrderId: null,
                SupplierInvoiceNumber: "INV-9001",
                ReceivedOn: clock.Today,
                InvoiceTotal: 300m,
                Lines: new[]
                {
                    new GoodsReceiptLineEditDto(code, 20m, 15m, "B-001",
                        clock.Today.AddMonths(9))
                }));

            result.IsSuccess.Should().BeTrue(result.Error.Message);
            result.Value.ReceiptNumber.Should().MatchRegex(@"^G-001-\d{6}$");
            result.Value.Total.Should().Be(300m);
            result.Value.InvoiceVariance.Should().Be(0m);
        }

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

            var product = await db.Products.AsNoTracking()
                .SingleAsync(p => p.Code == code);

            product.QuantityOnHand.Should().Be(20m);

            // The last price paid becomes the cost price, which is what
            // every subsequent margin is measured against.
            product.CostPrice.Amount.Should().Be(15m);
            product.RetailPrice.Amount.Should().Be(22.50m, "cost x 1.5, down from 30.00");

            var batch = await db.Batches.AsNoTracking()
                .SingleAsync(b => b.ProductId == product.Id);

            batch.BatchNumber.Should().Be("B-001");
            batch.QuantityOnHand.Should().Be(20m);
            batch.SupplierId.Should().Be(supplierId);

            var movement = await db.StockMovements.AsNoTracking()
                .SingleAsync(m => m.ProductId == product.Id
                                  && m.SourceDocumentType == "GoodsReceipt");

            movement.Quantity.Should().Be(20m, "a receipt is inbound");
            movement.BalanceAfter.Should().Be(20m);
        }

        // The proof: the till can now sell it, and FEFO finds the batch
        // the receipt created.
        await OpenTillSessionAsync();

        using (var scope = _fixture.NewScope())
        {
            var sales = PharmaXFixture.Resolve<ISaleService>(scope);

            var opened = await sales.OpenAsync();
            var line = await sales.AddLineAsync(opened.Value.Id, code, 3m);

            line.IsSuccess.Should().BeTrue(line.Error.Message);
            line.Value.Expiry.Should().NotBeNull(
                "the line should carry the batch expiry FEFO picked");
        }
    }

    /// <summary>
    /// BR-INV-02: batch tracking is worthless if a receipt can bring
    /// stock in without a batch number, because FEFO then has nothing
    /// to order by.
    /// </summary>
    [Fact]
    public async Task A_batch_tracked_line_without_a_batch_is_refused()
    {
        await SignInAsync();

        var supplierId = await GivenASupplierAsync("BATCHLESS", "No Batch Co");
        var code = await GivenAProductAsync("AMOX500", "Amoxicillin 500mg",
                                            batchTracked: true);

        using var scope = _fixture.NewScope();
        var receiving = PharmaXFixture.Resolve<IReceivingService>(scope);
        var clock = PharmaXFixture.Resolve<IClock>(scope);

        var noBatch = await receiving.ReceiveAsync(Receipt(supplierId,
            clock.Today, new GoodsReceiptLineEditDto(code, 5m, 10m, null,
                clock.Today.AddMonths(6))));

        noBatch.IsFailure.Should().BeTrue();
        noBatch.Error.Code.Should().Be("GRN-007");

        var noExpiry = await receiving.ReceiveAsync(Receipt(supplierId,
            clock.Today, new GoodsReceiptLineEditDto(code, 5m, 10m, "B-X",
                null)));

        noExpiry.IsFailure.Should().BeTrue();
        noExpiry.Error.Code.Should().Be("GRN-008");
    }

    /// <summary>
    /// Taking expired stock in is how it ends up on a shelf and then in
    /// front of a patient, so the receipt refuses it outright.
    /// </summary>
    [Fact]
    public async Task Expired_stock_cannot_be_taken_onto_the_shelf()
    {
        await SignInAsync();

        var supplierId = await GivenASupplierAsync("OLDSTOCK", "Old Stock Ltd");
        var code = await GivenAProductAsync("EXPIRED1", "Expiring item",
                                            batchTracked: true);

        using var scope = _fixture.NewScope();
        var receiving = PharmaXFixture.Resolve<IReceivingService>(scope);
        var clock = PharmaXFixture.Resolve<IClock>(scope);

        var result = await receiving.ReceiveAsync(Receipt(supplierId,
            clock.Today, new GoodsReceiptLineEditDto(code, 5m, 10m, "B-OLD",
                clock.Today.AddDays(-1))));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("GRN-009");
    }

    /// <summary>
    /// Bonus stock comes in at nothing on a line of its own. It used to
    /// become the cost price, so the item showed a 100% margin.
    /// </summary>
    [Fact]
    public async Task A_free_line_keeps_the_cost_and_the_screen_shows_the_new_price()
    {
        await SignInAsync();

        var supplierId = await GivenASupplierAsync("BONUS", "Bonus Wholesale");
        var code = await GivenAProductAsync("BONUS1", "Bonus item");

        using (var scope = _fixture.NewScope())
        {
            var receiving = PharmaXFixture.Resolve<IReceivingService>(scope);
            var receipt = Receipt(supplierId, PharmaXFixture.Resolve<IClock>(scope).Today,
                new GoodsReceiptLineEditDto(code, 10m, 8m, null, null),
                new GoodsReceiptLineEditDto(code, 2m, 0m, null, null));

            (await receiving.PreviewAsync(receipt)).Value.Select(l => l.SellingPrice)
                .Should().Equal(new decimal?[] { 12m, 12m }, "8.00 x 1.5, which the free line leaves");

            var result = await receiving.ReceiveAsync(receipt);
            result.IsSuccess.Should().BeTrue(result.Error.Message);
        }

        using (var scope = _fixture.NewScope())
        {
            var product = await PharmaXFixture.Resolve<PharmaXDbContext>(scope).Products
                .AsNoTracking().SingleAsync(p => p.Code == code);

            product.CostPrice.Amount.Should().Be(8m);
            product.RetailPrice.Amount.Should().Be(12m);
        }
    }

    /// <summary>
    /// Nothing is written until every line resolves, so one mistyped
    /// code cannot leave half a delivery posted and the other half in a
    /// box nobody has recorded.
    /// </summary>
    [Fact]
    public async Task A_bad_code_anywhere_posts_nothing_at_all()
    {
        await SignInAsync();

        var supplierId = await GivenASupplierAsync("PARTIAL", "Partial Co");
        var good = await GivenAProductAsync("GOODCODE", "A real product");

        using var scope = _fixture.NewScope();
        var receiving = PharmaXFixture.Resolve<IReceivingService>(scope);
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);
        var clock = PharmaXFixture.Resolve<IClock>(scope);

        var before = await db.GoodsReceipts.CountAsync();

        var result = await receiving.ReceiveAsync(new GoodsReceiptEditDto(
            supplierId, null, "INV-BAD", clock.Today, null,
            new[]
            {
                new GoodsReceiptLineEditDto(good, 5m, 10m, null, null),
                new GoodsReceiptLineEditDto("NOSUCHCODE", 5m, 10m, null, null)
            }));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("GRN-004");

        (await db.GoodsReceipts.CountAsync()).Should().Be(before);

        var product = await db.Products.AsNoTracking()
            .SingleAsync(p => p.Code == good);

        product.QuantityOnHand.Should().Be(0m,
            "the good line must not have posted on its own");
    }

    /// <summary>
    /// The same batch number twice is one batch with more units in it,
    /// not two rows competing for the same place in FEFO order.
    /// </summary>
    [Fact]
    public async Task The_same_batch_received_twice_is_one_batch()
    {
        await SignInAsync();

        var supplierId = await GivenASupplierAsync("REPEAT", "Repeat Supplies");
        var code = await GivenAProductAsync("REPEAT01", "Repeatedly received",
                                            batchTracked: true);

        using (var scope = _fixture.NewScope())
        {
            var receiving = PharmaXFixture.Resolve<IReceivingService>(scope);
            var clock = PharmaXFixture.Resolve<IClock>(scope);
            var expiry = clock.Today.AddMonths(10);

            foreach (var quantity in new[] { 6m, 4m })
            {
                var result = await receiving.ReceiveAsync(Receipt(supplierId,
                    clock.Today, new GoodsReceiptLineEditDto(code, quantity,
                        12m, "B-SAME", expiry)));

                result.IsSuccess.Should().BeTrue(result.Error.Message);
            }
        }

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

            var product = await db.Products.AsNoTracking()
                .SingleAsync(p => p.Code == code);

            var batches = await db.Batches.AsNoTracking()
                .Where(b => b.ProductId == product.Id)
                .ToListAsync();

            batches.Should().ContainSingle();
            batches[0].QuantityOnHand.Should().Be(10m);
            product.QuantityOnHand.Should().Be(10m);
        }
    }

    /// <summary>
    /// A short delivery leaves the order open rather than looking
    /// complete, because the rest of it is still owed.
    /// </summary>
    [Fact]
    public async Task A_short_delivery_leaves_the_order_partially_received()
    {
        await SignInAsync();

        var supplierId = await GivenASupplierAsync("ORDERED", "Ordered Goods");
        var code = await GivenAProductAsync("ORDER01", "Ordered item");

        Guid orderId;
        int lineNo;

        using (var scope = _fixture.NewScope())
        {
            var purchasing = PharmaXFixture.Resolve<IPurchasingService>(scope);

            var raised = await purchasing.RaiseAsync(new PurchaseOrderEditDto(
                supplierId, null,
                new[] { new PurchaseOrderLineEditDto(code, 10m, 8m) }));

            raised.IsSuccess.Should().BeTrue(raised.Error.Message);
            raised.Value.OrderNumber.Should().MatchRegex(@"^PO-001-\d{6}$");
            raised.Value.Status.Should().Be("Sent");

            orderId = raised.Value.Id;
            lineNo = raised.Value.Lines[0].LineNo;
        }

        using (var scope = _fixture.NewScope())
        {
            var receiving = PharmaXFixture.Resolve<IReceivingService>(scope);
            var clock = PharmaXFixture.Resolve<IClock>(scope);

            var result = await receiving.ReceiveAsync(new GoodsReceiptEditDto(
                supplierId, orderId, "INV-SHORT", clock.Today, null,
                new[]
                {
                    new GoodsReceiptLineEditDto(code, 4m, 8m, null, null,
                        PurchaseOrderLineNo: lineNo)
                }));

            result.IsSuccess.Should().BeTrue(result.Error.Message);
        }

        using (var scope = _fixture.NewScope())
        {
            var purchasing = PharmaXFixture.Resolve<IPurchasingService>(scope);
            var order = await purchasing.GetAsync(orderId);

            order.Value.Status.Should().Be("PartiallyReceived");
            order.Value.Lines[0].QuantityReceived.Should().Be(4m);
            order.Value.Lines[0].Outstanding.Should().Be(6m);
        }
    }

    [Fact]
    public async Task An_invoice_mismatch_is_kept_rather_than_refused()
    {
        await SignInAsync();

        var supplierId = await GivenASupplierAsync("MISMATCH", "Mismatch Co");
        var code = await GivenAProductAsync("MIS01", "Mismatched item");

        using var scope = _fixture.NewScope();
        var receiving = PharmaXFixture.Resolve<IReceivingService>(scope);
        var clock = PharmaXFixture.Resolve<IClock>(scope);

        // Lines come to 50; the supplier invoiced 55.
        var result = await receiving.ReceiveAsync(new GoodsReceiptEditDto(
            supplierId, null, "INV-MIS", clock.Today, 55m,
            new[] { new GoodsReceiptLineEditDto(code, 5m, 10m, null, null) }));

        result.IsSuccess.Should().BeTrue(result.Error.Message);
        result.Value.Total.Should().Be(50m);
        result.Value.InvoiceTotal.Should().Be(55m);
        result.Value.InvoiceVariance.Should().Be(5m);
    }

    [Fact]
    public async Task Receiving_needs_its_own_permission()
    {
        await SignInAsync();

        var supplierId = await GivenASupplierAsync("PERMS", "Permissions Co");
        var code = await GivenAProductAsync("PERM01", "Permission item");

        using var scope = _fixture.NewScope();
        var receiving = PharmaXFixture.Resolve<IReceivingService>(scope);
        var clock = PharmaXFixture.Resolve<IClock>(scope);

        // The seeded administrator holds every permission, so this is a
        // positive check: the demand is wired and does not refuse a
        // user who legitimately holds the right.
        var result = await receiving.ReceiveAsync(Receipt(supplierId,
            clock.Today, new GoodsReceiptLineEditDto(code, 1m, 1m, null,
                null)));

        result.IsSuccess.Should().BeTrue(result.Error.Message);
    }

    [Fact]
    public async Task A_duplicate_supplier_code_is_refused()
    {
        await SignInAsync();
        await GivenASupplierAsync("DUPE", "First Supplier");

        using var scope = _fixture.NewScope();
        var suppliers = PharmaXFixture.Resolve<ISupplierService>(scope);

        var second = await suppliers.CreateAsync(new SupplierEditDto(
            "DUPE", "Second Supplier", null, null, null, null, 30, 2));

        second.IsFailure.Should().BeTrue();
        second.Error.Code.Should().Be("SUP-002");
    }

    // ---- Steps -----------------------------------------------------------


    /// <summary>
    /// An expiry typed on a delivery used to stay on the delivery note
    /// alone, so the label printed without one. Now the item starts
    /// keeping batches from that delivery on.
    /// </summary>
    [Fact]
    public async Task Untracked_product_received_with_batch_and_expiry_becomes_batch_tracked()
    {
        await SignInAsync();

        var supplierId = await GivenASupplierAsync("AUTOTRK", "Auto Track Co");
        var code = await GivenAProductAsync("AUTOTRK1", "Amlovar 10mg");

        using (var scope = _fixture.NewScope())
        {
            var clock = PharmaXFixture.Resolve<IClock>(scope);
            var result = await PharmaXFixture.Resolve<IReceivingService>(scope)
                .ReceiveAsync(Receipt(supplierId, clock.Today,
                    new GoodsReceiptLineEditDto(code, 6m, 10m, "AM-1",
                        clock.Today.AddMonths(8))));

            result.IsSuccess.Should().BeTrue(result.Error.Message);
        }

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);
            var product = await db.Products.AsNoTracking().SingleAsync(p => p.Code == code);

            product.IsBatchTracked.Should().BeTrue();
            var batch = await db.Batches.AsNoTracking().SingleAsync(b => b.ProductId == product.Id);
            batch.BatchNumber.Should().Be("AM-1");
            batch.QuantityOnHand.Should().Be(6m);
        }
    }

    [Fact]
    public async Task Untracked_product_received_with_expiry_but_no_batch_is_refused()
    {
        await SignInAsync();

        var supplierId = await GivenASupplierAsync("NOBATCH2", "No Batch Two");
        var code = await GivenAProductAsync("NOBATCH2", "Expiry only");

        using (var scope = _fixture.NewScope())
        {
            var clock = PharmaXFixture.Resolve<IClock>(scope);
            var result = await PharmaXFixture.Resolve<IReceivingService>(scope)
                .ReceiveAsync(Receipt(supplierId, clock.Today,
                    new GoodsReceiptLineEditDto(code, 6m, 10m, null,
                        clock.Today.AddMonths(8))));

            result.Error.Code.Should().Be("GRN-007");
        }

        using (var scope = _fixture.NewScope())
            (await PharmaXFixture.Resolve<PharmaXDbContext>(scope).Products.AsNoTracking()
                .SingleAsync(p => p.Code == code)).IsBatchTracked.Should().BeFalse();
    }

    [Fact]
    public async Task Auto_tracked_product_keeps_its_existing_unbatched_stock()
    {
        await SignInAsync();

        var supplierId = await GivenASupplierAsync("KEEPSTK", "Keep Stock Co");
        var code = await GivenAProductAsync("KEEPSTK1", "Has loose stock");

        using (var scope = _fixture.NewScope())
        {
            var clock = PharmaXFixture.Resolve<IClock>(scope);
            var receiving = PharmaXFixture.Resolve<IReceivingService>(scope);

            (await receiving.ReceiveAsync(Receipt(supplierId, clock.Today,
                new GoodsReceiptLineEditDto(code, 10m, 10m, null, null))))
                .IsSuccess.Should().BeTrue();
            (await receiving.ReceiveAsync(Receipt(supplierId, clock.Today,
                new GoodsReceiptLineEditDto(code, 5m, 10m, "KS-1",
                    clock.Today.AddMonths(8))))).IsSuccess.Should().BeTrue();
        }

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);
            var product = await db.Products.AsNoTracking().SingleAsync(p => p.Code == code);

            product.QuantityOnHand.Should().Be(15m);
            (await db.Batches.AsNoTracking().SingleAsync(b => b.ProductId == product.Id))
                .QuantityOnHand.Should().Be(5m);
        }
    }
    private static GoodsReceiptEditDto Receipt(Guid supplierId,
        DateOnly receivedOn, params GoodsReceiptLineEditDto[] lines) =>
        new(supplierId, null, null, receivedOn, null, lines);

    private async Task SignInAsync()
    {
        using var scope = _fixture.NewScope();
        var auth = PharmaXFixture.Resolve<IAuthenticationService>(scope);

        var result = await auth.AuthenticateAsync("admin", "ChangeMe#1");
        result.IsSuccess.Should().BeTrue(result.Error.Message);
    }

    private async Task<Guid> GivenASupplierAsync(string code, string name)
    {
        using var scope = _fixture.NewScope();
        var suppliers = PharmaXFixture.Resolve<ISupplierService>(scope);

        var existing = await suppliers.ListAsync(includeInactive: true);

        var found = existing.Value
            .FirstOrDefault(s => string.Equals(s.Code, code,
                                               StringComparison.Ordinal));

        if (found is not null) return found.Id;

        var result = await suppliers.CreateAsync(new SupplierEditDto(
            Code: code, Name: name, ContactName: "A Contact",
            Telephone: "0110000000", Email: null, AccountNumber: "ACC-1",
            TermsDays: 30, LeadTimeDays: 3));

        result.IsSuccess.Should().BeTrue(result.Error.Message);
        return result.Value;
    }

    private async Task<string> GivenAProductAsync(string code,
        string description, bool batchTracked = false)
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        if (await db.Products.AnyAsync(p => p.Code == code)) return code;

        var product = Product.Create(code, description, Money.Of(30m),
            isBatchTracked: batchTracked);

        db.Products.Add(product);
        await db.SaveChangesAsync();

        return code;
    }

    private async Task OpenTillSessionAsync()
    {
        using var scope = _fixture.NewScope();
        var sessions = PharmaXFixture.Resolve<ITillSessionService>(scope);

        if (await sessions.CurrentAsync() is not null) return;

        var result = await sessions.OpenAsync(100m);
        result.IsSuccess.Should().BeTrue(result.Error.Message);
    }
}
