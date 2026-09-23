using FluentAssertions;
using NSubstitute;
using PharmaX.Application.Results;
using PharmaX.Application.Sales;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Application.Security;
using PharmaX.Desktop.Features.Sales;

namespace PharmaX.Tests.Unit.Presenters;

/// <summary>
/// The proof that the architecture works, and it runs in milliseconds.
/// No Form is constructed, no message pump runs, no database exists. If
/// this test ever needs any of those, something has leaked from the view
/// into the presenter.
/// </summary>
public class PosPresenterTests
{
    private readonly ISaleService _sales = Substitute.For<ISaleService>();
    private readonly IPermissionService _permissions =
        Substitute.For<IPermissionService>();
    private readonly IReceiptPrinter _receipts =
        Substitute.For<IReceiptPrinter>();
    private readonly ICashDrawer _drawer = Substitute.For<ICashDrawer>();
    private readonly ICollectionService _collections =
        Substitute.For<ICollectionService>();
    private readonly IPosView _view = Substitute.For<IPosView>();

    private readonly ReceiptHeaderFactory _headers =
        new(new PharmaX.Application.Abstractions.PharmaXOptions(),
            new Fakes.FakeCurrentUser(), new Fakes.FakeClock(),
            new PharmaX.Desktop.Mvp.Branding());

    private PosPresenter CreateSut()
    {
        var presenter = new PosPresenter(_sales, _collections, _permissions,
                                         _receipts, _drawer, _headers);
        presenter.Attach(_view);
        return presenter;
    }

    /// <summary>
    /// The till takes a name as readily as a barcode: anything that
    /// matched no code becomes a search seeded with whatever was typed,
    /// so the operator carries on rather than starting again.
    /// </summary>
    [Fact]
    public async Task An_unmatched_entry_becomes_a_search_for_what_was_typed()
    {
        _sales.OpenAsync().Returns(Result<SaleDto>.Ok(EmptySale()));
        _sales.AddLineAsync(Arg.Any<Guid>(), "panado", 1m)
              .Returns(Result<SaleLineDto>.Fail("POS-001",
                  "No product for code panado."));

        var sut = CreateSut();
        await sut.InitialiseAsync();

        _view.CodeScanned += Raise.Event<EventHandler<string>>(this, "panado");

        _view.Received().PromptProductSearch("panado");
    }

    [Fact]
    public async Task A_product_chosen_by_name_is_sold_by_its_code()
    {
        var sale = SaleWithOneLine();
        _sales.OpenAsync().Returns(Result<SaleDto>.Ok(sale));
        _sales.GetAsync(sale.Id).Returns(Result<SaleDto>.Ok(sale));

        _sales.AddLineAsync(sale.Id, "panado", 1m)
              .Returns(Result<SaleLineDto>.Fail("POS-001",
                  "No product for code panado."));

        _view.PromptProductSearch("panado").Returns("PAN500");

        _sales.AddLineAsync(sale.Id, "PAN500", 1m)
              .Returns(Result<SaleLineDto>.Ok(sale.Lines[0]));

        var sut = CreateSut();
        await sut.InitialiseAsync();

        _view.CodeScanned += Raise.Event<EventHandler<string>>(this, "panado");

        await _sales.Received().AddLineAsync(sale.Id, "PAN500", 1m);
        _view.DidNotReceive().ShowError(Arg.Any<string>());
    }

    /// <summary>
    /// Giving up on the search is not an error: the operator has just
    /// seen what did and did not match.
    /// </summary>
    [Fact]
    public async Task Abandoning_the_search_returns_focus_without_complaining()
    {
        _sales.OpenAsync().Returns(Result<SaleDto>.Ok(EmptySale()));
        _sales.AddLineAsync(Arg.Any<Guid>(), "9999", 1m)
              .Returns(Result<SaleLineDto>.Fail("POS-001",
                  "No product for code 9999."));

        _view.PromptProductSearch(Arg.Any<string>()).Returns((string?)null);

        var sut = CreateSut();
        await sut.InitialiseAsync();

        _view.CodeScanned += Raise.Event<EventHandler<string>>(this, "9999");

        _view.Received().FocusScan();
        _view.DidNotReceive().ShowError(Arg.Any<string>());
    }

    /// <summary>
    /// The guard on the fall-through. A discontinued product, or one
    /// with no sellable batch, did identify something: that has to be
    /// reported rather than reopened as a search, or the operator is
    /// sent hunting for an item the till has already refused to sell.
    /// </summary>
    [Fact]
    public async Task A_refusal_that_found_the_product_is_reported_not_searched()
    {
        _sales.OpenAsync().Returns(Result<SaleDto>.Ok(EmptySale()));
        _sales.AddLineAsync(Arg.Any<Guid>(), "PAN500", 1m)
              .Returns(Result<SaleLineDto>.Fail("POS-002",
                  "Panado 500mg is discontinued and cannot be sold."));

        var sut = CreateSut();
        await sut.InitialiseAsync();

        _view.CodeScanned += Raise.Event<EventHandler<string>>(this, "PAN500");

        _view.Received().ShowError(
            "Panado 500mg is discontinued and cannot be sold.");

        _view.Received().FocusScan();
        _view.DidNotReceive().PromptProductSearch(Arg.Any<string>());
    }

    [Fact]
    public async Task F2_opens_the_search_with_nothing_typed()
    {
        _sales.OpenAsync().Returns(Result<SaleDto>.Ok(EmptySale()));

        var sut = CreateSut();
        await sut.InitialiseAsync();

        _view.ProductSearchRequested += Raise.EventWith(this, EventArgs.Empty);

        _view.Received().PromptProductSearch(null);
    }

    [Fact]
    public async Task No_open_till_session_closes_the_screen()
    {
        _sales.OpenAsync().Returns(Result<SaleDto>.Fail("POS-013",
            "Open a till session before selling."));

        var sut = CreateSut();
        await sut.InitialiseAsync();

        _view.Received().ShowError("Open a till session before selling.");
        _view.Received().CloseView(false);
    }

    [Fact]
    public async Task Opening_a_sale_clears_the_screen_and_focuses_the_scanner()
    {
        _sales.OpenAsync().Returns(Result<SaleDto>.Ok(EmptySale()));

        var sut = CreateSut();
        await sut.InitialiseAsync();

        _view.Received().ClearSale();
        _view.Received().FocusScan();
        _view.DidNotReceive().ShowError(Arg.Any<string>());
    }

    /// <summary>
    /// The presenter decides that a supervisor is needed and asks the
    /// view to collect one. It never evaluates the rule itself.
    /// </summary>
    [Fact]
    public async Task A_discount_above_the_limit_asks_for_a_supervisor()
    {
        var sale = SaleWithOneLine();
        _sales.OpenAsync().Returns(Result<SaleDto>.Ok(sale));
        _sales.GetAsync(sale.Id).Returns(Result<SaleDto>.Ok(sale));

        _view.SelectedLineNo.Returns(1);
        _view.PromptDiscount(Arg.Any<decimal>()).Returns(5m);

        var supervisor = Guid.NewGuid();
        _view.PromptSupervisor(Arg.Any<string>()).Returns(supervisor);

        _permissions.Check("pos.sale.discount.above-limit")
                    .Returns(PermissionOutcome.RequiresSupervisor);

        _sales.ApplyLineDiscountAsync(sale.Id, 1, 5m, supervisor)
              .Returns(Result.Ok());

        var sut = CreateSut();
        await sut.InitialiseAsync();

        _view.LineDiscountRequested += Raise.EventWith(this, EventArgs.Empty);

        _view.Received().PromptSupervisor(
            "This discount is above your limit.");

        await _sales.Received().ApplyLineDiscountAsync(sale.Id, 1, 5m,
                                                       supervisor);
    }

    [Fact]
    public async Task A_declined_supervisor_leaves_the_discount_unapplied()
    {
        var sale = SaleWithOneLine();
        _sales.OpenAsync().Returns(Result<SaleDto>.Ok(sale));
        _sales.GetAsync(sale.Id).Returns(Result<SaleDto>.Ok(sale));

        _view.SelectedLineNo.Returns(1);
        _view.PromptDiscount(Arg.Any<decimal>()).Returns(5m);
        _view.PromptSupervisor(Arg.Any<string>()).Returns((Guid?)null);

        _permissions.Check("pos.sale.discount.above-limit")
                    .Returns(PermissionOutcome.RequiresSupervisor);

        var sut = CreateSut();
        await sut.InitialiseAsync();

        _view.LineDiscountRequested += Raise.EventWith(this, EventArgs.Empty);

        _view.Received().ShowError("Discount not applied.");

        await _sales.DidNotReceive().ApplyLineDiscountAsync(
            Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<decimal>(),
            Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Taking_payment_on_an_empty_sale_is_refused()
    {
        _sales.OpenAsync().Returns(Result<SaleDto>.Ok(EmptySale()));

        var sut = CreateSut();
        await sut.InitialiseAsync();

        _view.PaymentRequested += Raise.EventWith(this, EventArgs.Empty);

        _view.Received().ShowError(
            "Add at least one item before taking payment.");

        _view.DidNotReceive().PromptPayment(Arg.Any<decimal>());
    }

    /// <summary>
    /// Finalise, then print. The other way round leaves a printed
    /// receipt for a sale that never committed (Section 20.2).
    /// </summary>
    [Fact]
    public async Task A_completed_sale_prints_and_opens_the_drawer_for_cash()
    {
        var sale = SaleWithOneLine();
        _sales.OpenAsync().Returns(Result<SaleDto>.Ok(sale));
        _sales.GetAsync(sale.Id).Returns(Result<SaleDto>.Ok(sale));

        var payments = new[] { new PaymentDto("Cash", 24m, null) };
        _view.PromptPayment(24m).Returns(payments);

        _sales.FinaliseAsync(sale.Id, Arg.Any<IReadOnlyList<PaymentDto>>())
              .Returns(Result<FinaliseDto>.Ok(
                  new FinaliseDto("R-001-000001", 24m)));

        _receipts.PrintAsync(Arg.Any<SaleDto>(),
                Arg.Any<ReceiptHeaderDto>(), Arg.Any<CancellationToken>())
            .Returns(PrintOutcome.Printed);

        var sut = CreateSut();
        await sut.InitialiseAsync();

        _view.PaymentRequested += Raise.EventWith(this, EventArgs.Empty);

        _view.Received().ShowDocumentNumber("R-001-000001");
        await _drawer.Received().OpenAsync(
            Arg.Any<CancellationToken>());
        await _receipts.Received().PrintAsync(Arg.Any<SaleDto>(),
            Arg.Any<ReceiptHeaderDto>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The hourglass belongs over work, not over a person.
    /// </summary>
    /// <remarks>
    /// GuardAsync raises the busy cursor for as long as its action runs,
    /// and the payment dialog used to be inside that action - so the
    /// cashier counted the cash, chose a tender and typed a reference
    /// with a wait cursor sitting over the screen. It reads as an
    /// application that has hung, and it is the first thing anybody
    /// reports.
    /// </remarks>
    [Fact]
    public async Task The_busy_cursor_is_not_held_over_the_payment_dialog()
    {
        var sale = SaleWithOneLine();
        _sales.OpenAsync().Returns(Result<SaleDto>.Ok(sale));
        _sales.GetAsync(sale.Id).Returns(Result<SaleDto>.Ok(sale));

        _view.PromptPayment(24m)
             .Returns(new[] { new PaymentDto("Cash", 24m, null) });

        _sales.FinaliseAsync(sale.Id, Arg.Any<IReadOnlyList<PaymentDto>>())
              .Returns(Result<FinaliseDto>.Ok(
                  new FinaliseDto("R-001-000001", 24m)));

        _receipts.PrintAsync(Arg.Any<SaleDto>(),
                Arg.Any<ReceiptHeaderDto>(), Arg.Any<CancellationToken>())
            .Returns(PrintOutcome.Printed);

        var sut = CreateSut();
        await sut.InitialiseAsync();

        // Opening the sale has its own busy period; this is about what
        // happens from the moment Pay is pressed.
        _view.ClearReceivedCalls();

        _view.PaymentRequested += Raise.EventWith(this, EventArgs.Empty);

        // Received.InOrder wants the whole filtered sequence to match,
        // and finalising then opening the next sale raises the cursor
        // twice. What matters is only which of the two came first.
        var calls = _view.ReceivedCalls()
            .Select(c => c.GetMethodInfo().Name)
            .Where(name => name is "PromptPayment" or "SetBusy")
            .ToList();

        calls.Should().NotBeEmpty();

        calls[0].Should().Be("PromptPayment",
            "the dialog goes up before the hourglass, not under it");
    }

    /// <remarks>
    /// Nothing is asked of the services until the cashier has answered
    /// the dialog, so cancelling it costs a round trip to nowhere.
    /// </remarks>
    [Fact]
    public async Task Cancelling_the_payment_dialog_finalises_nothing()
    {
        var sale = SaleWithOneLine();
        _sales.OpenAsync().Returns(Result<SaleDto>.Ok(sale));
        _sales.GetAsync(sale.Id).Returns(Result<SaleDto>.Ok(sale));

        _view.PromptPayment(24m).Returns((IReadOnlyList<PaymentDto>?)null);

        var sut = CreateSut();
        await sut.InitialiseAsync();
        _view.ClearReceivedCalls();

        _view.PaymentRequested += Raise.EventWith(this, EventArgs.Empty);

        await _sales.DidNotReceive().FinaliseAsync(Arg.Any<Guid>(),
            Arg.Any<IReadOnlyList<PaymentDto>>());

        _view.DidNotReceive().SetBusy(true);
    }

    /// <summary>
    /// POS-014: a printer fault must degrade rather than stop a sale
    /// that has already taken the money.
    /// </summary>
    [Fact]
    public async Task A_failed_receipt_does_not_undo_the_sale()
    {
        var sale = SaleWithOneLine();
        _sales.OpenAsync().Returns(Result<SaleDto>.Ok(sale));
        _sales.GetAsync(sale.Id).Returns(Result<SaleDto>.Ok(sale));

        _view.PromptPayment(24m)
             .Returns(new[] { new PaymentDto("Cash", 24m, null) });

        _sales.FinaliseAsync(sale.Id, Arg.Any<IReadOnlyList<PaymentDto>>())
              .Returns(Result<FinaliseDto>.Ok(
                  new FinaliseDto("R-001-000001", 24m)));

        _receipts.PrintAsync(Arg.Any<SaleDto>(),
                Arg.Any<ReceiptHeaderDto>(), Arg.Any<CancellationToken>())
            .Returns(PrintOutcome.Failed);

        var sut = CreateSut();
        await sut.InitialiseAsync();

        _view.PaymentRequested += Raise.EventWith(this, EventArgs.Empty);

        _view.Received().ShowDocumentNumber("R-001-000001");
        _view.Received().ShowError(Arg.Is<string>(m =>
            m.Contains("did not print", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Voiding_a_line_without_a_reason_does_nothing()
    {
        var sale = SaleWithOneLine();
        _sales.OpenAsync().Returns(Result<SaleDto>.Ok(sale));

        _view.SelectedLineNo.Returns(1);
        _view.PromptVoidReason().Returns((string?)null);

        var sut = CreateSut();
        await sut.InitialiseAsync();

        _view.VoidLineRequested += Raise.EventWith(this, EventArgs.Empty);

        await _sales.DidNotReceive().VoidLineAsync(
            Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<string>());
    }

    private static SaleDto EmptySale() => new(
        Guid.NewGuid(), "", Array.Empty<SaleLineDto>(),
        new SaleTotalsDto(0, 0, 0, 0, 0, 0));

    private static SaleDto SaleWithOneLine()
    {
        var line = new SaleLineDto(1, Guid.NewGuid(),
            "Paracetamol 500mg", 1m, 24m, 0m, "A", 24m, null, null,
            false, false);

        return new SaleDto(Guid.NewGuid(), "", new[] { line },
            new SaleTotalsDto(24m, 0m, 3.13m, 24m, 0m, 0m));
    }
}
