using FluentAssertions;
using NSubstitute;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Assistant;
using PharmaX.Application.Assistant.Guided;
using PharmaX.Application.Inventory;
using PharmaX.Application.Inventory.Dtos;
using PharmaX.Application.Results;
using PharmaX.Application.Suppliers;
using PharmaX.Application.Suppliers.Dtos;
using PharmaX.Domain.Security;
using static PharmaX.Tests.Unit.Assistant.GuidedTestKit;

namespace PharmaX.Tests.Unit.Assistant;

/// <summary>
/// "Receive stock from Pharmanova", talked through: against the open order,
/// the invoice, each line checked, the totals compared, then posted on a yes.
/// </summary>
public class ReceiveStockFlowTests
{
    private readonly IReceivingService _receiving = Substitute.For<IReceivingService>();
    private readonly IPurchasingService _purchasing = Substitute.For<IPurchasingService>();
    private readonly ISupplierService _suppliers = Substitute.For<ISupplierService>();
    private readonly IProductService _products = Substitute.For<IProductService>();
    private readonly IPharmacySettings _settings = Substitute.For<IPharmacySettings>();

    private static readonly SupplierListItemDto Pharmanova =
        new(Guid.NewGuid(), "PN", "Pharmanova", null, null, 30, 3, true);
    private static readonly SupplierListItemDto PharmaPlus =
        new(Guid.NewGuid(), "PP", "Pharmaplus Distributors", null, null, 30, 3, true);

    private static readonly Guid Tablets = Guid.NewGuid();
    private static readonly Guid Amoxicillin = Guid.NewGuid();
    private static readonly Guid Order = Guid.NewGuid();

    public ReceiveStockFlowTests()
    {
        _suppliers.ListAsync(Arg.Any<bool>(), Arg.Any<PharmaX.Application.Abstractions.ListFilter?>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<SupplierListItemDto>>.Ok(new[] { Pharmanova, PharmaPlus }));

        _purchasing.OutstandingAsync(Pharmanova.Id, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<PurchaseOrderDto>>.Ok(new[]
            {
                new PurchaseOrderDto(Order, "PO-001-000003", Pharmanova.Id, "Pharmanova", new DateOnly(2026, 10, 20),
                    null, "Sent", 556m, new[]
                    {
                        new PurchaseOrderLineDto(1, Tablets, "Panado 500mg tablets 20s", 20m, 0m, 20m, 2.80m, 56m, "PAN500"),
                        new PurchaseOrderLineDto(2, Amoxicillin, "Amoxicillin 500mg capsules 21s", 10m, 0m, 10m, 50m, 500m, "AMX500")
                    })
            }));
        _purchasing.OutstandingAsync(PharmaPlus.Id, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<PurchaseOrderDto>>.Ok(Array.Empty<PurchaseOrderDto>()));

        _products.GetAsync(Tablets, Arg.Any<CancellationToken>())
            .Returns(Result<ProductDto>.Ok(Detail(Tablets, "PAN500", "Panado 500mg tablets 20s", 4m, 2.80m, 12m)));
        _products.GetAsync(Amoxicillin, Arg.Any<CancellationToken>())
            .Returns(Result<ProductDto>.Ok(Detail(Amoxicillin, "AMX500", "Amoxicillin 500mg capsules 21s", 70m, 50m,
                5m, batched: true)));

        _purchasing.BuyingAsync(Tablets, Arg.Any<CancellationToken>())
            .Returns(Result<ProductBuyingDto>.Ok(new ProductBuyingDto(Tablets, 20m, null, null, null, null, 2.80m, null)));
        _purchasing.BuyingAsync(Amoxicillin, Arg.Any<CancellationToken>())
            .Returns(Result<ProductBuyingDto>.Ok(new ProductBuyingDto(Amoxicillin, 10m, null, null, null, null, 50m, null)));

        _receiving.ReceiveAsync(Arg.Any<GoodsReceiptEditDto>(), Arg.Any<CancellationToken>())
            .Returns(Result<GoodsReceiptDto>.Ok(new GoodsReceiptDto(Guid.NewGuid(), "G-001-000002", Pharmanova.Id,
                "Pharmanova", "INV1", Today, true, 556m, 556m, 0m, Array.Empty<GoodsReceiptLineDto>())));
        _products.UpdateAsync(Arg.Any<Guid>(), Arg.Any<ProductEditDto>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok());
    }

    private ReceiveStockFlow Flow(string? supplier = "Pharmanova", params string[] granted) =>
        new(supplier, _receiving, _purchasing, _suppliers, _products, _settings,
            Granting(granted.Length == 0 ? new[] { PermissionCode.GoodsReceive } : granted), Clock());

    /// Against the order, both lines as ordered, the invoice agreeing.
    private static readonly string[] AsOrdered =
        { "1", "INV1", "", "556.00", "", "", "", "", "", "AMX1", "12/2027", "", "done" };

    [Fact]
    public async Task Asks_the_questions_in_the_order_given()
    {
        var asked = await KeysAsync(Flow(), AsOrdered);

        asked.Should().Equal("order", "invoice-number", "invoice-date", "invoice-total",
            "quantity", "cost", "bonus",
            "quantity", "cost", "batch", "expiry", "bonus",
            "product", GuidedFlow.ConfirmKey);
    }

    [Fact]
    public async Task The_open_orders_are_offered_with_what_is_outstanding()
    {
        (await Flow().StartAsync()).Should().Contain("Goods receipt from Pharmanova.")
            .And.Contain("1. PO-001-000003, raised 20/10, 2 line(s) outstanding");
    }

    [Fact]
    public async Task A_name_that_fits_more_than_one_supplier_asks_which()
    {
        var flow = Flow("pharma");

        (await flow.StartAsync()).Should().Contain("1. Pharmanova (PN)").And.Contain("2. Pharmaplus Distributors (PP)");
        await flow.ReplyAsync("2");

        flow.Waiting.Should().Be("invoice-number");   // no open orders for them
    }

    [Fact]
    public async Task More_than_ordered_is_warned_and_the_extra_goes_on_its_own_line()
    {
        var flow = Flow();
        await flow.StartAsync();
        await SayAsync(flow, "1", "INV1", "", "");

        (await flow.ReplyAsync("24")).Should().Contain("That is 4 more than ordered");
        await SayAsync(flow, "yes", "", "", "");

        flow.Lines.Select(l => (l.Quantity, l.OrderLineNo)).Should().Equal((20m, 1), (4m, (int?)null));
    }

    [Fact]
    public async Task A_cost_more_than_ten_percent_off_the_last_is_questioned()
    {
        var flow = Flow();
        await flow.StartAsync();
        await SayAsync(flow, "1", "INV1", "", "", "");

        (await flow.ReplyAsync("3.50")).Should().Contain("3.50 is 25% more than the last cost of 2.80");
        await flow.ReplyAsync("no");

        flow.Waiting.Should().Be("cost");
    }

    [Fact]
    public async Task A_cheaper_cost_says_the_price_comes_down()
    {
        var flow = Flow();
        await flow.StartAsync();
        await SayAsync(flow, "1", "INV1", "", "", "");

        (await flow.ReplyAsync("2.60")).Should().Contain("the selling price goes from 4.00 to 3.90");
    }

    [Theory]
    [InlineData("01/10/2026", "has already passed", "expiry")]
    [InlineData("31/01/2027", "expires in 98 days", "check")]
    public async Task An_expiry_is_refused_when_past_and_warned_within_six_months(string expiry, string said,
                                                                                    string waiting)
    {
        var flow = Flow();
        await flow.StartAsync();
        await SayAsync(flow, "1", "INV1", "", "", "", "", "", "", "", "AMX1");

        (await flow.ReplyAsync(expiry)).Should().Contain(said);
        flow.Waiting.Should().Be(waiting);
    }

    [Fact]
    public async Task Bonus_stock_goes_on_a_free_line()
    {
        var flow = Flow();
        await flow.StartAsync();

        await SayAsync(flow, "1", "INV1", "", "", "", "", "2");

        flow.Lines.Should().HaveCount(2);
        flow.Lines[1].Should().Match<ReceiveStockFlow.Line>(l => l.Quantity == 2m && l.Cost == 0m && l.Free
                                                                 && l.OrderLineNo == null);
    }

    [Fact]
    public async Task Totals_that_differ_need_a_reason_before_posting()
    {
        var flow = Flow();
        await flow.StartAsync();
        var answers = AsOrdered.ToArray();
        answers[3] = "500.00";

        (await SayAsync(flow, answers)).Should().Contain("Captured 556.00 against the invoice's 500.00: a difference of -56.00");
        (await flow.ReplyAsync("")).Should().Contain("a reason is needed before posting");

        (await flow.ReplyAsync("Credit note to follow")).Should()
            .Contain("difference -56.00 (Credit note to follow)").And.EndWith("Post the receipt and update stock?");
    }

    [Fact]
    public async Task Nothing_is_posted_until_yes()
    {
        var flow = Flow();
        await flow.StartAsync();

        var summary = await SayAsync(flow, AsOrdered);

        summary.Should().Contain("against PO-001-000003")
            .And.Contain("1. Panado 500mg tablets 20s (PAN500) 20 x 2.80 = 56.00 [order line 1]")
            .And.Contain("2. Amoxicillin 500mg capsules 21s (AMX500) 10 x 50.00 = 500.00, batch AMX1, expires 31/12/2027 [order line 2]");
        await _receiving.DidNotReceive().ReceiveAsync(Arg.Any<GoodsReceiptEditDto>(), Arg.Any<CancellationToken>());

        (await flow.ReplyAsync("yes")).Should().StartWith("Posted G-001-000002");
        await _receiving.Received(1).ReceiveAsync(Arg.Is<GoodsReceiptEditDto>(e =>
            e.PurchaseOrderId == Order && e.SupplierInvoiceNumber == "INV1" && e.InvoiceTotal == 556m
            && e.Lines.Count == 2 && e.Lines[1].BatchNumber == "AMX1" && e.Lines[1].PurchaseOrderLineNo == 2),
            Arg.Any<CancellationToken>());
        flow.Result!.Kind.Should().Be(AppTaskKind.ReceiveStock);
    }

    [Fact]
    public async Task No_receives_nothing()
    {
        var flow = Flow();
        await flow.StartAsync();
        await SayAsync(flow, AsOrdered);

        (await flow.ReplyAsync("no")).Should().Be("Nothing was received.");
        await _receiving.DidNotReceive().ReceiveAsync(Arg.Any<GoodsReceiptEditDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_dearer_cost_offers_a_new_price_where_the_user_may_change_it()
    {
        var flow = Flow("Pharmanova", PermissionCode.GoodsReceive, PermissionCode.PriceChange);
        await flow.StartAsync();
        await SayAsync(flow, "1", "INV1", "", "", "", "3.00");

        (await flow.ReplyAsync("")).Should().Contain("The cost goes up from 2.80 to 3.00")
            .And.Contain("Change the selling price?");
        await flow.ReplyAsync("4.50");
        await SayAsync(flow, "", "", "AMX1", "12/2027", "", "done", "yes");

        await _products.Received(1).UpdateAsync(Tablets, Arg.Is<ProductEditDto>(e => e.RetailPrice == 4.50m),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Without_the_price_permission_the_price_is_not_asked()
    {
        var flow = Flow();
        await flow.StartAsync();
        await SayAsync(flow, "1", "INV1", "", "", "", "3.00");

        await flow.ReplyAsync("");

        flow.Waiting.Should().Be("quantity");   // straight on to the next order line
    }
}
