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
    private readonly IPosView _view = Substitute.For<IPosView>();

    private readonly ReceiptHeaderFactory _headers =
        new(new PharmaX.Application.Abstractions.PharmaXOptions(),
            new Fakes.FakeCurrentUser(), new Fakes.FakeClock(),
            new PharmaX.Desktop.Mvp.Branding());

    private PosPresenter CreateSut()
    {
        var presenter = new PosPresenter(_sales, _permissions, _receipts,
                                         _drawer, _headers);
        presenter.Attach(_view);
        return presenter;
    }

    [Fact]
    public async Task Unknown_barcode_shows_an_error_and_returns_focus()
    {
        _sales.OpenAsync().Returns(Result<SaleDto>.Ok(EmptySale()));
        _sales.AddLineAsync(Arg.Any<Guid>(), "9999", 1m)
              .Returns(Result<SaleLineDto>.Fail("POS-001",
                  "No product for code 9999."));

        var sut = CreateSut();
        await sut.InitialiseAsync();

        _view.CodeScanned += Raise.Event<EventHandler<string>>(this, "9999");

        _view.Received().ShowError("No product for code 9999.");
        _view.Received().FocusScan();
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

        _receipts.Print(Arg.Any<SaleDto>(), Arg.Any<ReceiptHeaderDto>())
                 .Returns(PrintOutcome.Printed);

        var sut = CreateSut();
        await sut.InitialiseAsync();

        _view.PaymentRequested += Raise.EventWith(this, EventArgs.Empty);

        _view.Received().ShowDocumentNumber("R-001-000001");
        _drawer.Received().Open();
        _receipts.Received().Print(Arg.Any<SaleDto>(),
                                   Arg.Any<ReceiptHeaderDto>());
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

        _receipts.Print(Arg.Any<SaleDto>(), Arg.Any<ReceiptHeaderDto>())
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
