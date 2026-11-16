using FluentAssertions;
using NSubstitute;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Assistant;
using PharmaX.Application.Assistant.Guided;
using PharmaX.Application.Inventory;
using PharmaX.Application.Inventory.Dtos;
using PharmaX.Application.Patients;
using PharmaX.Application.Results;
using PharmaX.Application.Sales;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Application.Tills;
using PharmaX.Application.Tills.Dtos;
using PharmaX.Domain.Security;
using static PharmaX.Tests.Unit.Assistant.GuidedTestKit;

namespace PharmaX.Tests.Unit.Assistant;

/// <summary>
/// "Sell 2 paracetamol" at the till, talked through: which one, packs or
/// tablets, the customer, any discount, how it is paid, then completed on
/// a yes - and abandoned on a no.
/// </summary>
public class SellFlowTests
{
    private readonly ISaleService _sales = Substitute.For<ISaleService>();
    private readonly ITillSessionService _tills = Substitute.For<ITillSessionService>();
    private readonly IProductService _products = Substitute.For<IProductService>();
    private readonly IAccountService _accounts = Substitute.For<IAccountService>();
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();

    private static readonly Guid SaleId = Guid.NewGuid();
    private static readonly Guid Supervisor = Guid.NewGuid();
    private static readonly Guid Tablets = Guid.NewGuid();
    private static readonly Guid Syrup = Guid.NewGuid();
    private static readonly Guid Codeine = Guid.NewGuid();

    private readonly List<SaleLineDto> _lines = new();
    private IReadOnlyList<PaymentDto>? _paid;

    public SellFlowTests()
    {
        _user.Username.Returns("test.cashier");
        _user.DiscountLimitPercent.Returns(5m);

        var catalogue = new[]
        {
            Listed(Tablets, "PAR20", "PARACETAMOL 500MG", 10m, 3993m, pack: "TAB 20", units: 20m),
            Listed(Syrup, "PARSY", "PARACETAMOL SYRUP", 3m, 12m, pack: "SYR 100"),
            Listed(Codeine, "COD30", "PARACETAMOL CODEINE", 6m, 50m, schedule: 3, pack: "TAB 30")
        };

        _products.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<PharmaX.Application.Abstractions.ListFilter?>(), Arg.Any<CancellationToken>())
            .Returns(ci => Result<ProductPageDto>.Ok(new ProductPageDto(
                catalogue.Where(p => p.Description.Contains(((string)ci[0]).ToUpperInvariant(), StringComparison.Ordinal)
                                     || ((string)ci[0]).Equals("paracetamol", StringComparison.OrdinalIgnoreCase))
                    .ToList(), 3)));
        _products.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<ProductListItemDto?>.Ok(null));

        _tills.CurrentAsync(Arg.Any<CancellationToken>()).Returns((TillSessionDto?)null);
        _tills.OpenAsync(Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(Result<TillSessionDto>.Ok(null!));

        _sales.OpenAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Result<SaleDto>.Ok(Sale()));
        _sales.GetAsync(SaleId, Arg.Any<CancellationToken>()).Returns(_ => Result<SaleDto>.Ok(Sale()));
        _sales.AddLineAsync(SaleId, Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var price = (string)ci[1] == "PAR20" ? 10m : 3m;
                var quantity = (decimal)ci[2];
                var line = new SaleLineDto(_lines.Count + 1, Guid.NewGuid(), (string)ci[1], quantity, price, 0m, "A",
                    Math.Round(quantity * price, 2), null, null, false, false);
                _lines.Add(line);
                return Result<SaleLineDto>.Ok(line);
            });
        _sales.ApplyLineDiscountAsync(SaleId, Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var i = _lines.FindIndex(l => l.LineNo == (int)ci[1]);
                _lines[i] = _lines[i] with { Discount = (decimal)ci[2], LineTotal = _lines[i].LineTotal - (decimal)ci[2] };
                return Result.Ok();
            });
        _sales.FinaliseAsync(SaleId, Arg.Any<IReadOnlyList<PaymentDto>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                _paid = (IReadOnlyList<PaymentDto>)ci[1];
                var total = Sale().Totals.Total;
                var change = Math.Max(0m, _paid.Sum(p => p.Amount) - total);
                return Result<FinaliseDto>.Ok(new FinaliseDto("S-001-000009", total, change));
            });
        _sales.AbandonAsync(SaleId, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Ok());
        _sales.HoldAsync(SaleId, Arg.Any<CancellationToken>()).Returns(Result.Ok());
    }

    private SaleDto Sale()
    {
        var gross = _lines.Sum(l => l.Quantity * l.UnitPrice);
        var discount = _lines.Sum(l => l.Discount);
        return new SaleDto(SaleId, "", _lines.ToList(),
            new SaleTotalsDto(gross, discount, 0m, gross - discount, 0m, 0m));
    }

    private SellFlow Flow(GuidedTestKit.Prompts? prompts = null, string item = "paracetamol", decimal? quantity = 2m,
                          params string[] granted) =>
        new(item, quantity, null, _sales, _tills, _products, _accounts, _user,
            Granting(granted.Length == 0
                ? new[] { PermissionCode.SaleCreate, PermissionCode.SaleTakePayment }
                : granted),
            prompts ?? new GuidedTestKit.Prompts(null));

    [Fact]
    public async Task Asks_the_questions_in_the_order_given()
    {
        var asked = await KeysAsync(Flow(), "20", "1", "2 packs", "", "cash", "", "cash", "50");

        asked.Should().Equal("float", "product", "quantity", "more", "customer", "discount", "tender", "tendered",
            GuidedFlow.ConfirmKey);
    }

    [Fact]
    public async Task With_a_till_session_open_the_float_is_not_asked()
    {
        _tills.CurrentAsync(Arg.Any<CancellationToken>()).Returns(new TillSessionDto(
            Guid.NewGuid(), "1", "test.cashier", DateTime.UtcNow, 20m, "Open", 0));
        var flow = Flow();

        await flow.StartAsync();

        flow.Waiting.Should().Be("product");
    }

    [Fact]
    public async Task The_float_opens_the_till_session()
    {
        var flow = Flow();
        await flow.StartAsync();

        (await flow.ReplyAsync("20")).Should().Contain("Till session opened with a float of 20.00");

        await _tills.Received(1).OpenAsync(20m, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Which_one_lists_pack_price_and_stock()
    {
        var flow = Flow();
        await flow.StartAsync();

        (await flow.ReplyAsync("20")).Should().Contain("Which paracetamol?")
            .And.Contain("1. PARACETAMOL 500MG TAB 20 - 10.00 - 3,993 on hand")
            .And.Contain("3. PARACETAMOL CODEINE TAB 30 - 6.00 - 50 on hand (pharmacist only)");
    }

    [Fact]
    public async Task Packs_or_tablets_is_asked_where_it_sells_both_and_tablets_ring_up_as_part_of_a_pack()
    {
        var flow = Flow();
        await flow.StartAsync();

        (await SayAsync(flow, "20", "1")).Should().Contain("2 packs or 2 tablets? (a pack is 20)");
        (await flow.ReplyAsync("2 tablets")).Should().Contain("Rung up 2 tablets of PARACETAMOL 500MG");

        await _sales.Received(1).AddLineAsync(SaleId, "PAR20", 0.1m, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sold_only_by_the_pack_it_rings_up_without_asking()
    {
        var flow = Flow(item: "syrup");
        await flow.StartAsync();

        var said = await flow.ReplyAsync("20");

        said.Should().Contain("Rung up 2 x PARACETAMOL SYRUP").And.EndWith("Anything else? Name it, or press Enter if that's all.");
        flow.Waiting.Should().Be("more");
    }

    [Fact]
    public async Task A_pharmacist_only_medicine_is_sent_to_the_dispensary()
    {
        var flow = Flow();
        await flow.StartAsync();
        await flow.ReplyAsync("20");

        (await flow.ReplyAsync("3")).Should().Contain("dispensed against a script, not sold at the till");

        flow.Waiting.Should().Be("product");
        await _sales.DidNotReceive().AddLineAsync(Arg.Any<Guid>(), "COD30", Arg.Any<decimal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Anything_else_asks_which_and_how_many_again()
    {
        var flow = Flow();
        await flow.StartAsync();
        await SayAsync(flow, "20", "1", "2 packs");

        (await flow.ReplyAsync("and 3 syrup")).Should().Contain("Rung up 3 x PARACETAMOL SYRUP");
        _lines.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_discount_over_the_limit_needs_a_supervisor()
    {
        var prompts = new GuidedTestKit.Prompts(Supervisor);
        var flow = Flow(prompts);
        await flow.StartAsync();
        await SayAsync(flow, "20", "1", "2 packs", "", "cash");

        await flow.ReplyAsync("10%");

        prompts.Asked.Should().Equal(PermissionCode.SaleDiscountAboveLimit);
        await _sales.Received(1).ApplyLineDiscountAsync(SaleId, 1, 2m, Supervisor, Arg.Any<CancellationToken>());
        flow.Waiting.Should().Be("tender");
    }

    [Fact]
    public async Task Without_the_supervisor_there_is_no_discount_over_the_limit()
    {
        var flow = Flow(new GuidedTestKit.Prompts(null));
        await flow.StartAsync();
        await SayAsync(flow, "20", "1", "2 packs", "", "cash");

        (await flow.ReplyAsync("10%")).Should().StartWith("Not authorised");

        flow.Waiting.Should().Be("discount");
        await _sales.DidNotReceive().ApplyLineDiscountAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<decimal>(),
            Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Within_the_limit_no_one_else_is_asked()
    {
        var prompts = new GuidedTestKit.Prompts(Supervisor);
        var flow = Flow(prompts);
        await flow.StartAsync();
        await SayAsync(flow, "20", "1", "2 packs", "", "cash");

        await flow.ReplyAsync("1.00");

        prompts.Asked.Should().BeEmpty();
        await _sales.Received(1).ApplyLineDiscountAsync(SaleId, 1, 1m, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_discount_is_offered_to_someone_who_may_give_none()
    {
        _user.DiscountLimitPercent.Returns(0m);
        var flow = Flow();
        await flow.StartAsync();

        await SayAsync(flow, "20", "1", "2 packs", "", "cash");

        flow.Waiting.Should().Be("tender");
    }

    [Fact]
    public async Task Cash_short_of_the_total_is_refused_and_the_change_is_shown()
    {
        var flow = Flow();
        await flow.StartAsync();
        await SayAsync(flow, "20", "1", "2 packs", "", "cash", "", "cash");

        (await flow.ReplyAsync("15")).Should().StartWith("That is short: 20.00 is due in cash.");
        (await flow.ReplyAsync("50")).Should().StartWith("Change: 30.00.")
            .And.Contain("Tendered: 50.00, change 30.00").And.EndWith("Complete the sale and print the receipt?");
    }

    [Fact]
    public async Task A_split_payment_takes_cash_first_and_the_rest_by_card()
    {
        var flow = Flow();
        await flow.StartAsync();
        await SayAsync(flow, "20", "1", "2 packs", "", "cash", "", "5 cash, rest card", "5", "yes");

        _paid.Should().Equal(new PaymentDto("Cash", 5m, null), new PaymentDto("Card", 15m, null));
    }

    [Fact]
    public async Task Account_is_refused_for_a_sale_on_no_account()
    {
        var flow = Flow();
        await flow.StartAsync();
        await SayAsync(flow, "20", "1", "2 packs", "", "cash", "");

        (await flow.ReplyAsync("account")).Should().Contain("The sale isn't on an account");
        flow.Waiting.Should().Be("tender");
    }

    [Fact]
    public async Task Nothing_is_completed_until_yes()
    {
        var flow = Flow();
        await flow.StartAsync();

        var summary = await SayAsync(flow, "20", "1", "2 packs", "", "cash", "", "cash", "50");

        summary.Should().Contain("PAR20 2 x 10.00 = 20.00").And.Contain("Total: 20.00").And.Contain("Paid: Cash 20.00");
        await _sales.DidNotReceive().FinaliseAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<PaymentDto>>(),
            Arg.Any<CancellationToken>());

        (await flow.ReplyAsync("yes")).Should().StartWith("Sale S-001-000009 complete, 20.00. Change: 30.00.");
        _paid.Should().Equal(new PaymentDto("Cash", 50m, null));
        flow.Result.Should().Be(new GuidedResult(AppTaskKind.Sell, SaleId, "S-001-000009"));
    }

    [Fact]
    public async Task No_abandons_the_sale()
    {
        var flow = Flow();
        await flow.StartAsync();
        await SayAsync(flow, "20", "1", "2 packs", "", "cash", "", "cash", "50");

        (await flow.ReplyAsync("no")).Should().Be("No sale was made.");

        await _sales.Received(1).AbandonAsync(SaleId, Arg.Any<string>(), Arg.Any<CancellationToken>());
        flow.Result.Should().BeNull();
    }

    [Fact]
    public async Task Cancel_after_ringing_up_abandons_the_sale()
    {
        var flow = Flow();
        await flow.StartAsync();
        await SayAsync(flow, "20", "1", "2 packs");

        await flow.ReplyAsync("cancel");

        await _sales.Received(1).AbandonAsync(SaleId, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Someone_who_does_not_take_money_holds_the_basket_for_the_cashier()
    {
        var flow = Flow(granted: PermissionCode.SaleCreate);
        await flow.StartAsync();

        var summary = await SayAsync(flow, "1", "2 packs", "", "cash", "");

        summary.Should().EndWith("Hold it for the cashier to take payment?");
        (await flow.ReplyAsync("yes")).Should().StartWith("Held for the cashier");
        await _sales.Received(1).HoldAsync(SaleId, Arg.Any<CancellationToken>());
    }
}
