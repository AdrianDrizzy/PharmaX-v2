using FluentAssertions;
using NSubstitute;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Assistant;
using PharmaX.Application.Assistant.Guided;
using PharmaX.Application.Inventory;
using PharmaX.Application.Inventory.Dtos;
using PharmaX.Application.Reporting;
using PharmaX.Application.Results;
using PharmaX.Application.Suppliers;
using PharmaX.Application.Suppliers.Dtos;
using static PharmaX.Tests.Unit.Assistant.GuidedTestKit;

namespace PharmaX.Tests.Unit.Assistant;

/// <summary>
/// "Create a purchase order for 20 panado", talked through: which one, how
/// many against the stock, who from at what cost, then raised on a yes.
/// </summary>
public class PurchaseOrderFlowTests
{
    private readonly IProductService _products = Substitute.For<IProductService>();
    private readonly IPurchasingService _purchasing = Substitute.For<IPurchasingService>();
    private readonly ISupplierService _suppliers = Substitute.For<ISupplierService>();
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly IPdfWriter _pdf = Substitute.For<IPdfWriter>();

    private static readonly SupplierListItemDto Pharmanova =
        new(Guid.NewGuid(), "PN", "Pharmanova", null, null, 30, 3, true);
    private static readonly SupplierListItemDto Greenwood =
        new(Guid.NewGuid(), "GW", "Greenwood Wholesalers", null, null, 30, 2, true);

    private static readonly Guid Tablets = Guid.NewGuid();
    private static readonly Guid Syrup = Guid.NewGuid();
    private static readonly Guid Ibuprofen = Guid.NewGuid();

    private readonly List<PurchaseOrderLineDto> _belowReorder = new();

    public PurchaseOrderFlowTests()
    {
        _user.Username.Returns("test.manager");

        _products.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<PharmaX.Application.Abstractions.ListFilter?>(), Arg.Any<CancellationToken>())
            .Returns(Result<ProductPageDto>.Ok(new ProductPageDto(new[]
            {
                Listed(Tablets, "PAN500", "Panado 500mg tablets 20s", 39.38m, 12m, pack: "TAB 20"),
                Listed(Syrup, "95", "PANADO SYRUP", 3m, 10m, pack: "SYR 100")
            }, 2)));
        _products.GetAsync(Tablets, Arg.Any<CancellationToken>())
            .Returns(Result<ProductDto>.Ok(Detail(Tablets, "PAN500", "Panado 500mg tablets 20s", 39.38m, 3m, 12m, 30m)));
        _products.GetAsync(Syrup, Arg.Any<CancellationToken>())
            .Returns(Result<ProductDto>.Ok(Detail(Syrup, "95", "PANADO SYRUP", 3m, 1.5m, 10m, 1m)));
        _products.GetAsync(Ibuprofen, Arg.Any<CancellationToken>())
            .Returns(Result<ProductDto>.Ok(Detail(Ibuprofen, "IBU400", "Ibuprofen 400mg", 5m, 2m, 1m, 10m)));

        _purchasing.BuyingAsync(Tablets, Arg.Any<CancellationToken>())
            .Returns(Result<ProductBuyingDto>.Ok(new ProductBuyingDto(Tablets, 0m, Pharmanova.Id, Pharmanova.Name,
                Pharmanova.Id, Pharmanova.Name, 2.80m, Today.AddDays(-20))));
        _purchasing.BuyingAsync(Syrup, Arg.Any<CancellationToken>())
            .Returns(Result<ProductBuyingDto>.Ok(new ProductBuyingDto(Syrup, 4m, null, null, null, null, null, null)));
        _purchasing.SuggestForAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Result<IReadOnlyList<PurchaseOrderLineDto>>.Ok(_belowReorder.ToList()));
        _purchasing.RaiseAsync(Arg.Any<PurchaseOrderEditDto>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var edit = (PurchaseOrderEditDto)ci[0];
                return Result<PurchaseOrderDto>.Ok(new PurchaseOrderDto(Guid.NewGuid(), "PO-001-000009",
                    edit.SupplierId, "Pharmanova", Today, edit.ExpectedOn, "Sent",
                    edit.Lines.Sum(l => l.Quantity * l.UnitCost),
                    edit.Lines.Select((l, i) => new PurchaseOrderLineDto(i + 1, Guid.NewGuid(), l.ProductCode,
                        l.Quantity, 0m, l.Quantity, l.UnitCost, l.Quantity * l.UnitCost, l.ProductCode)).ToList()));
            });

        _suppliers.ListAsync(Arg.Any<bool>(), Arg.Any<PharmaX.Application.Abstractions.ListFilter?>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<SupplierListItemDto>>.Ok(new[] { Pharmanova, Greenwood }));
    }

    private PurchaseOrderFlow Flow(string? item = "panado", decimal? quantity = 20m, string? supplier = null) =>
        new(item, quantity, supplier, _products, _purchasing, _suppliers, Clock(), _user, _pdf);

    [Fact]
    public async Task Asks_the_questions_in_the_order_given()
    {
        var asked = await KeysAsync(Flow(), "1", "", "", "", "", "", "", "yes", "");

        asked.Should().Equal("product", "quantity", "supplier", "cost", "more", "expected", "notes",
            GuidedFlow.ConfirmKey, "export", "export");
    }

    [Fact]
    public async Task Which_one_lists_pack_stock_reorder_level_and_what_is_on_order()
    {
        var said = await Flow().StartAsync();

        said.Should().Contain("Which panado?")
            .And.Contain("1. Panado 500mg tablets 20s (PAN500) - TAB 20, 12 on hand, reorder at 30, 0 on order")
            .And.Contain("2. PANADO SYRUP (95) - SYR 100, 10 on hand, reorder at 1, 4 on order");
    }

    [Fact]
    public async Task The_quantity_asked_is_confirmed_against_the_stock()
    {
        var flow = Flow();
        await flow.StartAsync();

        (await flow.ReplyAsync("1")).Should().Contain(
            "20 packs? You have 12 on hand, your reorder level is 30, and 0 are on order.");

        await flow.ReplyAsync("24");
        await SayAsync(flow, "", "");
        flow.Lines.Single().Quantity.Should().Be(24m);
    }

    [Fact]
    public async Task The_supplier_defaults_to_the_usual_one_and_the_cost_to_the_last()
    {
        var flow = Flow();
        await flow.StartAsync();

        var supplier = await SayAsync(flow, "1", "");
        supplier.Should().Contain("Press Enter for Pharmanova, the usual supplier")
            .And.Contain("It last came from Pharmanova at 2.80");

        (await flow.ReplyAsync("")).Should().Contain("Press Enter for 2.80");
        await flow.ReplyAsync("");

        flow.Lines.Single().Cost.Should().Be(2.80m);
    }

    [Fact]
    public async Task Another_supplier_takes_the_product_cost_instead_of_the_last()
    {
        var flow = Flow();
        await flow.StartAsync();

        (await SayAsync(flow, "1", "", "greenwood")).Should().Contain("Press Enter for 3.00");
    }

    [Fact]
    public async Task The_supplier_named_in_the_request_is_not_asked()
    {
        var flow = Flow(supplier: "Greenwood");
        await flow.StartAsync();

        await SayAsync(flow, "1", "");

        flow.Waiting.Should().Be("cost");
    }

    [Fact]
    public async Task Items_below_their_reorder_level_from_that_supplier_can_be_added()
    {
        _belowReorder.Add(new PurchaseOrderLineDto(1, Ibuprofen, "Ibuprofen 400mg", 19m, 0m, 19m, 2m, 38m, "IBU400"));
        var flow = Flow();
        await flow.StartAsync();

        var more = await SayAsync(flow, "1", "", "", "");
        more.Should().Contain("1 other item(s) from Pharmanova are below their reorder level")
            .And.Contain("Ibuprofen 400mg (IBU400) - 19 to bring it back up");

        await flow.ReplyAsync("add them");
        flow.Lines.Select(l => (l.Product.Code, l.Quantity)).Should().Equal(("PAN500", 20m), ("IBU400", 19m));
    }

    [Fact]
    public async Task A_cost_of_nought_needs_a_yes()
    {
        var flow = Flow();
        await flow.StartAsync();
        await SayAsync(flow, "1", "", "");

        (await flow.ReplyAsync("0")).Should().Contain("Order it at no cost?");
        await flow.ReplyAsync("no");

        flow.Waiting.Should().Be("cost");
        flow.Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task An_expected_date_already_past_is_refused()
    {
        var flow = Flow();
        await flow.StartAsync();
        await SayAsync(flow, "1", "", "", "", "");

        (await flow.ReplyAsync("01/10/2026")).Should().StartWith("That date has already passed.");
        flow.Waiting.Should().Be("expected");
    }

    [Fact]
    public async Task Nothing_is_raised_until_yes_and_then_it_offers_the_export()
    {
        var flow = Flow();
        await flow.StartAsync();

        var summary = await SayAsync(flow, "1", "", "", "", "", "", "Deliver before noon");

        summary.Should().Contain("Purchase order to Pharmanova:")
            .And.Contain("1. Panado 500mg tablets 20s (PAN500) 20 x 2.80 = 56.00")
            .And.Contain("Expected: 28/10/2026").And.Contain("Notes: Deliver before noon")
            .And.EndWith("Raise the order?");
        await _purchasing.DidNotReceive().RaiseAsync(Arg.Any<PurchaseOrderEditDto>(), Arg.Any<CancellationToken>());

        (await flow.ReplyAsync("yes")).Should().Contain("Raised PO-001-000009").And.Contain("Save as PDF or Excel");
        await _purchasing.Received(1).RaiseAsync(
            Arg.Is<PurchaseOrderEditDto>(e => e.SupplierId == Pharmanova.Id && e.Lines.Single().ProductCode == "PAN500"),
            Arg.Any<CancellationToken>());
        flow.Result!.Kind.Should().Be(AppTaskKind.PurchaseOrder);

        (await flow.ReplyAsync("pdf")).Should().StartWith("Saved ").And.EndWith(".pdf.");
        _pdf.Received(1).WriteTo(
            Arg.Is<ReportResult>(r => r.Figures.Any(f => f.Label == "Notes" && f.Value == "Deliver before noon")),
            Arg.Is<string>(p => p.EndsWith("Purchase order PO-001-000009 Pharmanova.pdf", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task No_raises_nothing()
    {
        var flow = Flow();
        await flow.StartAsync();
        await SayAsync(flow, "1", "", "", "", "", "", "");

        (await flow.ReplyAsync("no")).Should().Be("No order was raised.");
        await _purchasing.DidNotReceive().RaiseAsync(Arg.Any<PurchaseOrderEditDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_number_off_the_list_is_not_taken_for_a_product()
    {
        var flow = Flow();
        await flow.StartAsync();

        (await flow.ReplyAsync("7")).Should().StartWith("Choose 1 to 2.");
        flow.Waiting.Should().Be("product");
    }
}
