using FluentAssertions;
using NSubstitute;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Assistant;
using PharmaX.Application.Assistant.Guided;
using PharmaX.Application.Inventory;
using PharmaX.Application.Inventory.Dtos;
using PharmaX.Application.Reference;
using PharmaX.Application.Results;
using PharmaX.Application.Security;
using PharmaX.Domain.Security;

namespace PharmaX.Tests.Unit.Assistant;

/// <summary>
/// "Add a new product called Vitamin C 1000mg", talked through in Ask
/// PharmaX: the questions in the order asked for, the defaults offered,
/// the checks, and nothing saved before the operator says yes.
/// </summary>
public class NewProductFlowTests
{
    private readonly IProductService _products = Substitute.For<IProductService>();
    private readonly IReferenceDataService _reference = Substitute.For<IReferenceDataService>();
    private readonly IPharmacySettings _settings = Substitute.For<IPharmacySettings>();
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly List<ProductListItemDto> _catalogue = new();
    private readonly Dictionary<string, ProductListItemDto> _barcodes = new();
    private static readonly DateOnly Today = new(2026, 10, 25);

    private static readonly ReferenceItemDto FrontShop = new(Guid.NewGuid(), "FS", "Front shop", null, true);
    private static readonly ReferenceItemDto Vitamins = new(Guid.NewGuid(), "VIT", "Vitamins and supplements", null, true);
    private static readonly ReferenceItemDto Cipla = new(Guid.NewGuid(), "CIP", "Cipla", null, true);

    public NewProductFlowTests()
    {
        _clock.Today.Returns(Today);
        Grant(PermissionCode.ProductMaintain, PermissionCode.ProductRead, PermissionCode.StockCorrect);

        _products.NextCodeAsync(Arg.Any<CancellationToken>()).Returns(Result<string>.Ok("2549"));
        _products.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Result<ProductListItemDto?>.Ok(_catalogue.Find(p => p.Code == (string)ci[0])
                                                          ?? _barcodes.GetValueOrDefault((string)ci[0])));
        _products.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(_ => Result<ProductPageDto>.Ok(new ProductPageDto(_catalogue.ToList(), _catalogue.Count)));
        _products.CreateAsync(Arg.Any<ProductEditDto>(), Arg.Any<CancellationToken>())
            .Returns(Result<Guid>.Ok(Guid.NewGuid()));

        List(ReferenceKind.Department, FrontShop);
        List(ReferenceKind.Category, Vitamins);
        List(ReferenceKind.Manufacturer, Cipla);

        _settings.GetAsync(PharmacySettingKey.Markup, Arg.Any<CancellationToken>()).Returns("50");
        _settings.GetAsync(PharmacySettingKey.Rounding, Arg.Any<CancellationToken>()).Returns("0.05");
    }

    private readonly HashSet<string> _granted = new();

    /// Granted is the enum's default, so a bare substitute would grant everything.
    private void Grant(params string[] codes)
    {
        _granted.UnionWith(codes);
        _permissions.Check(Arg.Any<string>())
            .Returns(ci => _granted.Contains((string)ci[0]) ? PermissionOutcome.Granted : PermissionOutcome.Denied);
    }

    private void List(ReferenceKind kind, params ReferenceItemDto[] items) =>
        _reference.ListAsync(kind, Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<ReferenceItemDto>>.Ok(items));

    private NewProductFlow Flow(string? name = "Vitamin C 1000mg") =>
        new(name, _products, _reference, _settings, _permissions, _clock);

    /// Every answer up to the opening stock, as at the counter.
    private static readonly string[] UpToStock =
    {
        "", "", "30 tablets", "6001234567890", "front shop, vitamins", "1", "", "standard", "4.00", "",
        "10", ""
    };

    private static async Task<string> SayAsync(GuidedFlow flow, params string[] answers)
    {
        var last = "";
        foreach (var answer in answers) last = await flow.ReplyAsync(answer);
        return last;
    }

    [Fact]
    public async Task Opens_with_the_name_from_the_request_and_asks_for_the_code_first()
    {
        var flow = Flow();

        var said = await flow.StartAsync();

        said.Should().Contain("New product: Vitamin C 1000mg")
            .And.Contain("Product code? Press Enter to let PharmaX number it (2549).");
        flow.Waiting.Should().Be("code");
    }

    [Fact]
    public async Task Asks_the_questions_in_the_order_given()
    {
        var flow = Flow();
        await flow.StartAsync();
        var asked = new List<string?> { flow.Waiting };

        foreach (var answer in UpToStock.Concat(new[] { "12", "B123", "31/12/2027", "" }))
        {
            await flow.ReplyAsync(answer);
            asked.Add(flow.Waiting);
        }

        asked.Should().Equal("code", "generic", "pack", "barcode", "department-category", "manufacturer",
            "schedule", "tax", "cost", "price", "reorder", "batch-tracked", "stock", "batch-number",
            "expiry", "reason", GuidedFlow.ConfirmKey);
    }

    [Fact]
    public async Task No_opening_stock_skips_the_batch_questions()
    {
        var flow = Flow();
        await flow.StartAsync();

        await SayAsync(flow, UpToStock);
        await flow.ReplyAsync("");

        flow.Waiting.Should().Be(GuidedFlow.ConfirmKey);
    }

    [Fact]
    public async Task Without_the_stock_permission_opening_stock_is_not_asked()
    {
        _granted.Remove(PermissionCode.StockCorrect);
        var flow = Flow();
        await flow.StartAsync();

        var said = await SayAsync(flow, UpToStock);

        flow.Waiting.Should().Be(GuidedFlow.ConfirmKey);
        said.Should().Contain("counting stock needs the stock permission");
    }

    [Fact]
    public async Task The_defaults_fill_in_the_code_the_ingredient_the_schedule_and_the_price()
    {
        var flow = Flow();
        await flow.StartAsync();

        (await flow.ReplyAsync("")).Should().Contain("Press Enter for \"ascorbic acid\"");
        await SayAsync(flow, UpToStock.Skip(1).ToArray());
        await SayAsync(flow, "", "yes");

        var edit = flow.Edit();
        edit.Code.Should().Be("2549");
        edit.GenericName.Should().Be("ascorbic acid");
        edit.PackSize.Should().Be("TAB 30");
        edit.UnitsPerPack.Should().Be(30m);
        edit.Schedule.Should().Be(0);
        edit.RetailPrice.Should().Be(6.00m);   // cost 4.00 plus 50%
        edit.IsBatchTracked.Should().BeTrue(); // a medicine: it has an ingredient
        edit.TaxCode.Should().Be("A");
        edit.VatRatePercent.Should().Be(15m);
        edit.DepartmentId.Should().Be(FrontShop.Id);
        edit.CategoryId.Should().Be(Vitamins.Id);
        edit.ManufacturerId.Should().Be(Cipla.Id);
        edit.Barcodes.Should().Equal("6001234567890");
    }

    [Fact]
    public async Task The_selling_price_offers_the_markup_and_shows_the_margin()
    {
        var flow = Flow();
        await flow.StartAsync();

        var said = await SayAsync(flow, UpToStock.Take(9).ToArray());

        said.Should().Contain("Press Enter for 6.00: cost plus the usual 50% markup, a margin of 33.3%");
    }

    [Fact]
    public async Task A_code_already_in_use_is_refused()
    {
        _catalogue.Add(new ProductListItemDto(Guid.NewGuid(), "2310", "VITAMIN C 1000MG", 5m, 3m, 0, true, true, false));
        var flow = Flow();
        await flow.StartAsync();

        var said = await flow.ReplyAsync("2310");

        said.Should().Contain("2310 is already VITAMIN C 1000MG (2310)");
        flow.Waiting.Should().Be("code");
    }

    [Theory]
    [InlineData("2", "tax")]
    [InlineData("9", "schedule")]
    [InlineData("two", "schedule")]
    public async Task The_schedule_is_0_to_8(string answer, string next)
    {
        var flow = Flow();
        await flow.StartAsync();
        await SayAsync(flow, UpToStock.Take(6).ToArray());

        await flow.ReplyAsync(answer);

        flow.Waiting.Should().Be(next);
    }

    [Fact]
    public async Task Schedule_3_warns_it_becomes_pharmacist_only_and_waits_for_a_yes()
    {
        Grant(PermissionCode.ProductDispensary);
        var flow = Flow();
        await flow.StartAsync();
        await SayAsync(flow, UpToStock.Take(6).ToArray());

        var said = await flow.ReplyAsync("3");

        said.Should().Contain("pharmacist only");
        flow.Waiting.Should().Be("check");
        (await flow.ReplyAsync("maybe")).Should().StartWith("Please answer yes or no.");

        await flow.ReplyAsync("yes");
        flow.Waiting.Should().Be("tax");
    }

    [Fact]
    public async Task Schedule_3_is_refused_without_the_dispensary_permission()
    {
        var flow = Flow();
        await flow.StartAsync();
        await SayAsync(flow, UpToStock.Take(6).ToArray());

        var said = await flow.ReplyAsync("4");

        said.Should().Contain("needs the dispensary permission");
        flow.Waiting.Should().Be("schedule");
    }

    [Fact]
    public async Task A_price_below_cost_needs_a_yes()
    {
        var flow = Flow();
        await flow.StartAsync();
        await SayAsync(flow, UpToStock.Take(9).ToArray());

        var said = await flow.ReplyAsync("3.00");

        said.Should().Contain("below the cost of 4.00");
        await flow.ReplyAsync("no");
        flow.Waiting.Should().Be("price");
    }

    [Fact]
    public async Task An_expiry_already_past_is_refused()
    {
        var flow = Flow();
        await flow.StartAsync();
        await SayAsync(flow, UpToStock);
        await SayAsync(flow, "12", "B123");

        var said = await flow.ReplyAsync("01/10/2026");

        said.Should().Contain("has already passed");
        flow.Waiting.Should().Be("expiry");
    }

    [Fact]
    public async Task An_expiry_within_three_months_is_warned_about()
    {
        var flow = Flow();
        await flow.StartAsync();
        await SayAsync(flow, UpToStock);
        await SayAsync(flow, "12", "B123");

        var said = await flow.ReplyAsync("15/12/2026");

        said.Should().Contain("expires in 51 days");
        await flow.ReplyAsync("yes");
        flow.Waiting.Should().Be("reason");
    }

    [Fact]
    public async Task Opening_stock_goes_on_a_batch_with_its_reason()
    {
        var flow = Flow();
        await flow.StartAsync();
        await SayAsync(flow, UpToStock);

        await SayAsync(flow, "12", "b123", "03/2028", "");

        var edit = flow.Edit();
        edit.Batches.Should().ContainSingle().Which.Should().Be(
            new ProductBatchEditDto(null, "B123", new DateOnly(2028, 3, 31), 12m));
        edit.QuantityOnHand.Should().BeNull();
        edit.StockReason.Should().Be("Opening stock");
    }

    [Fact]
    public async Task A_duplicate_name_is_asked_about_before_the_summary()
    {
        _catalogue.Add(new ProductListItemDto(Guid.NewGuid(), "2310", "VITAMIN C 1000MG", 5m, 3m, 0, true, true, false));
        var flow = Flow();
        await flow.StartAsync();
        await SayAsync(flow, UpToStock);

        var said = await flow.ReplyAsync("");

        said.Should().Be("There is already VITAMIN C 1000MG (2310). Add anyway?");
        (await flow.ReplyAsync("no")).Should().StartWith("Nothing was saved");
        flow.IsFinished.Should().BeTrue();
        await _products.DidNotReceive().CreateAsync(Arg.Any<ProductEditDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_duplicate_barcode_is_refused_where_it_is_scanned()
    {
        _barcodes["6001234567890"] = new ProductListItemDto(Guid.NewGuid(), "1759", "NUESTRA VITAMIN C 500MG", 5m,
            3m, 2, true, true, false);
        var flow = Flow();
        await flow.StartAsync();
        await SayAsync(flow, "", "", "30 tablets");

        var said = await flow.ReplyAsync("6001234567890");

        said.Should().Contain("already belongs to NUESTRA VITAMIN C 500MG (1759)");
        flow.Waiting.Should().Be("barcode");
    }

    [Fact]
    public async Task The_summary_shows_the_whole_product_and_nothing_is_saved_until_yes()
    {
        var flow = Flow();
        await flow.StartAsync();
        await SayAsync(flow, UpToStock);

        var said = await flow.ReplyAsync("");

        said.Should().Contain("Description: Vitamin C 1000mg").And.Contain("Code: 2549")
            .And.Contain("Selling price: 6.00 (margin 33.3%)").And.EndWith("Save it?");
        await _products.DidNotReceive().CreateAsync(Arg.Any<ProductEditDto>(), Arg.Any<CancellationToken>());

        (await flow.ReplyAsync("yes")).Should().Be("Saved Vitamin C 1000mg as product 2549.");
        await _products.Received(1).CreateAsync(Arg.Is<ProductEditDto>(e => e.Code == "2549"), Arg.Any<CancellationToken>());
        flow.Result!.Kind.Should().Be(AppTaskKind.NewProduct);
    }

    [Fact]
    public async Task No_at_the_summary_saves_nothing()
    {
        var flow = Flow();
        await flow.StartAsync();
        await SayAsync(flow, UpToStock);
        await flow.ReplyAsync("");

        (await flow.ReplyAsync("no")).Should().Be("Nothing was saved.");

        flow.IsFinished.Should().BeTrue();
        await _products.DidNotReceive().CreateAsync(Arg.Any<ProductEditDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_at_any_question_saves_nothing()
    {
        var flow = Flow();
        await flow.StartAsync();
        await SayAsync(flow, "", "");

        (await flow.ReplyAsync("cancel")).Should().Contain("Nothing was saved");

        flow.IsFinished.Should().BeTrue();
        await _products.DidNotReceive().CreateAsync(Arg.Any<ProductEditDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task One_answer_can_be_changed_from_the_summary()
    {
        var flow = Flow();
        await flow.StartAsync();
        await SayAsync(flow, UpToStock);
        await flow.ReplyAsync("");

        (await flow.ReplyAsync("change the price")).Should().Contain("Selling price?");
        var said = await flow.ReplyAsync("7.50");

        flow.Waiting.Should().Be(GuidedFlow.ConfirmKey);
        said.Should().Contain("Selling price: 7.50");
    }

    [Fact]
    public async Task A_refusal_from_the_service_is_said_and_nothing_is_lost()
    {
        _products.CreateAsync(Arg.Any<ProductEditDto>(), Arg.Any<CancellationToken>())
            .Returns(Result<Guid>.Fail("INV-002", "Product code 2549 is already in use."));
        var flow = Flow();
        await flow.StartAsync();
        await SayAsync(flow, UpToStock);
        await flow.ReplyAsync("");

        var said = await flow.ReplyAsync("yes");

        said.Should().StartWith("It was not saved: Product code 2549 is already in use.");
        flow.IsFinished.Should().BeFalse();
        flow.Waiting.Should().Be(GuidedFlow.ConfirmKey);
    }

    [Fact]
    public async Task Without_a_name_it_asks_for_one_first()
    {
        var flow = Flow(null);

        await flow.StartAsync();

        flow.Waiting.Should().Be("name");
    }

    [Theory]
    [InlineData("30 tablets", "TAB 30", 30)]
    [InlineData("100ml", "ML 100", 100)]
    [InlineData("TAB 30, 30", "TAB 30", 30)]
    [InlineData("200 ml syrup", "ML 200", 200)]
    public void A_pack_size_is_read_the_way_the_catalogue_writes_it(string said, string label, int units) =>
        PackSize.Read(said).Should().Be((label, (decimal?)units));

    [Theory]
    [InlineData("Vitamin C 1000mg", "ascorbic acid")]
    [InlineData("Panado 500mg tablets", "paracetamol")]
    [InlineData("Vitamin B12 injection", "cyanocobalamin")]
    [InlineData("Baby wipes", null)]
    public void A_common_name_suggests_its_ingredient(string name, string? ingredient) =>
        Ingredients.For(name).Should().Be(ingredient);
}
