using FluentAssertions;
using NSubstitute;
using PharmaX.Application.Alerts;
using PharmaX.Application.Repositories;
using PharmaX.Application.Results;
using PharmaX.Application.Security;
using PharmaX.Application.Suppliers;
using PharmaX.Application.Suppliers.Dtos;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Security;
using PharmaX.Domain.Suppliers;
using PharmaX.Tests.Unit.Fakes;

namespace PharmaX.Tests.Unit.Application;

/// <summary>
/// The sign-in stock assistant: the summary it shows, and the draft orders
/// it makes - grouped by supplier, never sent, never made twice.
/// </summary>
public class StockAlertAssistantTests
{
    private readonly IStockAlertService _alerts = Substitute.For<IStockAlertService>();
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private readonly InMemoryProductRepository _products = new();
    private readonly ISupplierRepository _suppliers = Substitute.For<ISupplierRepository>();
    private readonly InMemoryPurchaseOrders _orders = new();

    public StockAlertAssistantTests()
    {
        _permissions.Check(Arg.Any<string>()).Returns(PermissionOutcome.Granted);
    }

    private PurchasingService Purchasing() => new(_orders, _suppliers, _products,
        new FakeDocumentNumbering(), _permissions, new FakeAuditWriter(),
        new FakeUnitOfWork(), new FakeClock());

    private StockAlertAssistant Assistant() => new(_alerts, Purchasing(),
        _products, _suppliers, _permissions);

    private Supplier AddSupplier(string code)
    {
        var supplier = Supplier.Create(code, code + " Wholesale");
        _suppliers.GetAsync(supplier.Id, Arg.Any<CancellationToken>()).Returns(supplier);
        _suppliers.ListAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(_ => (IReadOnlyList<Supplier>)_supplierList);
        _supplierList.Add(supplier);
        return supplier;
    }

    private readonly List<Supplier> _supplierList = new();

    private Product AddProduct(string code, decimal onHand, decimal reorder,
        Supplier? supplier, decimal cost = 2m)
    {
        var product = Product.Create(code, code + " tablets", Money.Of(10m));
        product.SetQuantityOnHand(onHand);
        product.SetReorderLevel(reorder);
        product.ChangeCostPrice(Money.Of(cost));
        product.SetDefaultSupplier(supplier?.Id);
        return _products.Add(product);
    }

    private static StockAlertDto Alert(AlertKind kind, string name, decimal qty = 0m,
        decimal reorder = 0m, int? days = null) =>
        new($"k:{name}", kind, AlertSeverity.Warning, Guid.NewGuid(), name, name,
            days is null ? null : "B1", null, days, qty, reorder, "");

    [Fact]
    public async Task The_digest_sorts_alerts_into_out_low_expired_and_expiring()
    {
        _alerts.CurrentAsync(Arg.Any<CancellationToken>()).Returns(
            Result<IReadOnlyList<StockAlertDto>>.Ok(new[]
            {
                Alert(AlertKind.OutOfStock, "Panado"),
                Alert(AlertKind.LowStock, "Amoxil", 3m, 10m),
                Alert(AlertKind.Expired, "Brufen", 5m, days: -2),
                Alert(AlertKind.ExpiringSoon, "Zinnat", 12m, days: 20)
            }));

        var digest = (await Assistant().DigestAsync()).Value;

        digest.OutOfStock.Should().ContainSingle(a => a.ProductName == "Panado");
        digest.BelowReorder.Should().ContainSingle(a => a.ProductName == "Amoxil");
        digest.Expired.Should().ContainSingle(a => a.ProductName == "Brufen");
        digest.ExpiringSoon.Should().ContainSingle(a => a.ProductName == "Zinnat");

        var text = digest.Describe();
        text.Should().Contain("Out of stock (1):").And.Contain("Panado");
        text.Should().Contain("Amoxil (3 left, reorder at 10)");
        text.Should().Contain("Zinnat batch B1, 12 units in 20 day(s)");
    }

    [Fact]
    public async Task A_long_list_is_cut_short_with_how_many_more()
    {
        _alerts.CurrentAsync(Arg.Any<CancellationToken>()).Returns(
            Result<IReadOnlyList<StockAlertDto>>.Ok(Enumerable.Range(1, 11)
                .Select(i => Alert(AlertKind.OutOfStock, $"Item {i}")).ToList()));

        var text = (await Assistant().DigestAsync()).Value.Describe();

        text.Should().Contain("Out of stock (11):").And.Contain("...and 3 more.");
        text.Should().NotContain("Item 9");
    }

    [Fact]
    public async Task A_user_who_cannot_read_stock_gets_an_empty_digest()
    {
        _permissions.Check(PermissionCode.ProductRead).Returns(PermissionOutcome.Denied);

        var digest = (await Assistant().DigestAsync()).Value;

        digest.IsEmpty.Should().BeTrue();
        await _alerts.DidNotReceive().CurrentAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Drafts_one_order_per_default_supplier_and_sends_none()
    {
        var acme = AddSupplier("ACME");
        var medi = AddSupplier("MEDI");
        AddProduct("PAN", 0m, 10m, acme);
        AddProduct("AMX", 3m, 10m, acme);
        AddProduct("ZIN", 1m, 5m, medi);
        AddProduct("FULL", 50m, 10m, medi);

        var drafted = (await Assistant().DraftOrdersAsync()).Value;

        drafted.Orders.Should().HaveCount(2);
        drafted.Orders.Should().OnlyContain(o => o.Status == nameof(PurchaseOrderStatus.Draft));
        _orders.All.Should().OnlyContain(o => o.Status == PurchaseOrderStatus.Draft);

        var acmeOrder = drafted.Orders.Single(o => o.SupplierId == acme.Id);
        acmeOrder.Lines.Select(l => (l.ProductCode, l.QuantityOrdered))
            .Should().BeEquivalentTo(new[] { ("PAN", 20m), ("AMX", 17m) });

        drafted.Orders.Single(o => o.SupplierId == medi.Id).Lines
            .Should().ContainSingle(l => l.ProductCode == "ZIN" && l.QuantityOrdered == 9m);
    }

    /// <remarks>
    /// ADM-DEF-06: what is on a draft counts as coming, so signing in again
    /// the same afternoon drafts nothing more.
    /// </remarks>
    [Fact]
    public async Task Signing_in_again_does_not_draft_the_same_stock_twice()
    {
        var acme = AddSupplier("ACME");
        AddProduct("PAN", 0m, 10m, acme);

        (await Assistant().DraftOrdersAsync()).Value.Orders.Should().HaveCount(1);
        (await Assistant().DraftOrdersAsync()).Value.Orders.Should().BeEmpty();

        _orders.All.Should().ContainSingle();
    }

    [Fact]
    public async Task Stock_already_on_a_sent_order_is_not_drafted_again()
    {
        var acme = AddSupplier("ACME");
        AddProduct("PAN", 0m, 10m, acme);

        await Purchasing().RaiseAsync(new PurchaseOrderEditDto(acme.Id, null,
            new[] { new PurchaseOrderLineEditDto("PAN", 15m, 2m) }));

        var drafted = (await Assistant().DraftOrdersAsync()).Value;

        // 0 on hand + 15 coming is above the reorder level of 10.
        drafted.Orders.Should().BeEmpty();
    }

    [Fact]
    public async Task A_product_with_no_default_supplier_is_reported_not_drafted()
    {
        AddProduct("ORPHAN", 0m, 10m, supplier: null);

        var drafted = (await Assistant().DraftOrdersAsync()).Value;

        drafted.Orders.Should().BeEmpty();
        drafted.NoSupplier.Should().ContainSingle(l => l.ProductCode == "ORPHAN");
    }

    [Fact]
    public async Task Nothing_is_drafted_for_a_user_who_may_not_raise_orders()
    {
        var acme = AddSupplier("ACME");
        AddProduct("PAN", 0m, 10m, acme);
        _permissions.Check(PermissionCode.PurchaseOrderRaise).Returns(PermissionOutcome.Denied);

        var drafted = (await Assistant().DraftOrdersAsync()).Value;

        drafted.Orders.Should().BeEmpty();
        _orders.All.Should().BeEmpty();
    }

    [Fact]
    public async Task Sending_a_draft_applies_the_buyers_quantities_and_drops_noughts()
    {
        var acme = AddSupplier("ACME");
        AddProduct("PAN", 0m, 10m, acme);
        AddProduct("AMX", 0m, 10m, acme);
        var draft = (await Assistant().DraftOrdersAsync()).Value.Orders.Single();

        var sent = await Purchasing().SendDraftAsync(draft.Id,
            new Dictionary<int, decimal> { [1] = 30m, [2] = 0m });

        sent.IsSuccess.Should().BeTrue();
        sent.Value.Status.Should().Be(nameof(PurchaseOrderStatus.Sent));
        sent.Value.Lines.Should().ContainSingle(l => l.ProductCode == "PAN" && l.QuantityOrdered == 30m);
        (await Purchasing().DraftsAsync()).Value.Should().BeEmpty();
    }

    [Fact]
    public async Task A_draft_cannot_be_sent_twice_or_emptied()
    {
        var acme = AddSupplier("ACME");
        AddProduct("PAN", 0m, 10m, acme);
        var draft = (await Assistant().DraftOrdersAsync()).Value.Orders.Single();

        (await Purchasing().SendDraftAsync(draft.Id, new Dictionary<int, decimal> { [1] = 0m }))
            .Error.Code.Should().Be("PO-002");

        (await Purchasing().SendDraftAsync(draft.Id, new Dictionary<int, decimal>()))
            .IsSuccess.Should().BeTrue();
        (await Purchasing().SendDraftAsync(draft.Id, new Dictionary<int, decimal>()))
            .Error.Code.Should().Be("PO-006");
    }

    [Fact]
    public async Task The_notice_names_each_draft_and_says_nothing_was_sent()
    {
        var acme = AddSupplier("ACME");
        AddProduct("PAN", 0m, 10m, acme, cost: 2.5m);
        AddProduct("ORPHAN", 0m, 4m, supplier: null);

        var drafted = (await Assistant().DraftOrdersAsync()).Value;
        var text = DraftNotice.Describe(drafted.Orders, justMade: true);

        text.Should().StartWith("I've created the purchase order for these items that are running out:");
        text.Should().Contain("to ACME Wholesale: 1 item(s), 50.00");
        text.Should().Contain("Click here to view and finalise it for sending to the supplier.");
        text.Should().Contain("Nothing has been sent yet.");
        drafted.DescribeMissing().Should().Contain("1 item(s)").And.Contain("ORPHAN tablets");

        DraftNotice.Caption(0).Should().BeEmpty();
        DraftNotice.Caption(2).Should().Be("AI: 2 draft orders to finalise");
    }

    [Fact]
    public void The_order_report_carries_every_line_and_the_total()
    {
        var order = new PurchaseOrderDto(Guid.NewGuid(), "PO-001-000009", Guid.NewGuid(),
            "ACME Wholesale", new DateOnly(2026, 10, 25), null, "Draft", 0m, new[]
            {
                new PurchaseOrderLineDto(1, Guid.NewGuid(), "Panado", 20m, 0m, 20m, 2m, 40m, "PAN"),
                new PurchaseOrderLineDto(2, Guid.NewGuid(), "Amoxil", 5m, 0m, 5m, 3m, 15m, "AMX")
            });

        var report = PurchaseOrderReport.Build(order, "test.manager", DateTime.Now);

        report.Title.Should().Be("Purchase order PO-001-000009");
        report.Rows.Should().HaveCount(2);
        report.Rows[1]["Code"].Should().Be("AMX");
        report.Totals["Total"].Should().Be(55m);
        report.Provenance.DataCurrency.Should().Contain("not yet sent");
        report.Figures.Should().Contain(f => f.Label == "Supplier" && f.Value == "ACME Wholesale");
    }

    [Fact]
    public void A_sent_order_keeps_its_quantities()
    {
        var order = PurchaseOrder.Raise("PO-1", Guid.NewGuid(), new DateOnly(2026, 10, 25));
        order.AddLine(Guid.NewGuid(), "Panado", 10m, Money.Of(2m));
        order.Send();

        var change = () => order.ChangeQuantity(1, 20m);

        change.Should().Throw<DomainException>();
    }

    /// The orders, held in a list.
    private sealed class InMemoryPurchaseOrders : IPurchaseOrderRepository
    {
        public List<PurchaseOrder> All { get; } = new();

        public Task<PurchaseOrder?> GetAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(All.FirstOrDefault(o => o.Id == id));

        public Task<IReadOnlyList<PurchaseOrder>> OutstandingAsync(Guid? supplierId = null,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PurchaseOrder>>(All
                .Where(o => o.Status is PurchaseOrderStatus.Sent or PurchaseOrderStatus.PartiallyReceived)
                .ToList());

        public Task<IReadOnlyDictionary<Guid, decimal>> OnOrderAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, decimal>>(All
                .Where(o => o.Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.Sent
                            or PurchaseOrderStatus.PartiallyReceived)
                .SelectMany(o => o.Lines)
                .GroupBy(l => l.ProductId)
                .ToDictionary(g => g.Key, g => g.Sum(l => l.Outstanding)));

        public Task<IReadOnlyList<PurchaseOrder>> DraftsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PurchaseOrder>>(All
                .Where(o => o.Status == PurchaseOrderStatus.Draft).ToList());

        public Task AddAsync(PurchaseOrder order, CancellationToken ct = default)
        {
            All.Add(order);
            return Task.CompletedTask;
        }
    }
}
