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
            new PharmaX.Application.Abstractions.PharmacyProfile(
                new PharmaX.Application.Abstractions.PharmaXOptions()),
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

    /// <summary>
    /// A collected script says RATED PRICING only when its co-payment rests
    /// on a medical aid tariff. It said so for every collection, a private
    /// patient's at the shelf price included.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_collection_is_badged_rated_only_when_its_lines_are(
        bool rated)
    {
        var dispensingId = Guid.NewGuid();
        var line = new SaleLineDto(1, Guid.NewGuid(),
            "Metformin 850mg tablets 60s x 30", 1m, 77.38m, 0m, "A", 77.38m,
            null, null, rated, false);
        var sale = new SaleDto(Guid.NewGuid(), "", new[] { line },
            new SaleTotalsDto(77.38m, 0m, 10.09m, 77.38m, 0m, 0m));

        _collections.StartAsync(dispensingId).Returns(Result<SaleDto>.Ok(sale));

        var sut = CreateSut();
        sut.Collect(dispensingId);
        await sut.InitialiseAsync();

        _view.Received().ShowRatedPricing(rated);
        _view.DidNotReceive().ShowRatedPricing(!rated);
    }

    /// <remarks>
    /// The till window used to remember only the sale it had finalised
    /// itself, so a till closed and opened again had nothing to reprint
    /// (OCT-DEF-05). It now offers the last receipt of the till's session.
    /// </remarks>
    [Fact]
    public async Task A_reopened_till_offers_its_last_receipt_for_a_reprint()
    {
        var receipt = SaleWithOneLine() with { DocumentNumber = "S-001-000005" };

        _sales.OpenAsync().Returns(Result<SaleDto>.Ok(EmptySale()));
        _sales.LastReceiptNumberAsync().Returns(Result<string>.Ok("S-001-000005"));
        _view.PromptReceiptNumber("S-001-000005").Returns("S-001-000005");
        _sales.FindReceiptAsync("S-001-000005").Returns(Result<SaleDto>.Ok(receipt));

        _receipts.PrintAsync(Arg.Any<SaleDto>(), Arg.Any<ReceiptHeaderDto>(),
                Arg.Any<CancellationToken>())
            .Returns(PrintOutcome.Printed);

        var sut = CreateSut();
        await sut.InitialiseAsync();

        _view.ReprintRequested += Raise.EventWith(this, EventArgs.Empty);

        _view.Received().PromptReceiptNumber("S-001-000005");

        await _receipts.Received().PrintAsync(
            Arg.Is<SaleDto>(s => s.DocumentNumber == "S-001-000005"),
            Arg.Is<ReceiptHeaderDto>(h => h.IsCopy),
            Arg.Any<CancellationToken>());
    }

    /// <remarks>
    /// A customer back for a copy of an earlier receipt hands over its
    /// number, which the cashier types over the one offered.
    /// </remarks>
    [Fact]
    public async Task A_receipt_number_typed_over_the_offer_is_the_one_reprinted()
    {
        var earlier = SaleWithOneLine() with { DocumentNumber = "S-001-000002" };

        _sales.OpenAsync().Returns(Result<SaleDto>.Ok(EmptySale()));
        _sales.LastReceiptNumberAsync().Returns(Result<string>.Ok("S-001-000005"));
        _view.PromptReceiptNumber("S-001-000005").Returns("s-001-000002");
        _sales.FindReceiptAsync("s-001-000002").Returns(Result<SaleDto>.Ok(earlier));

        _receipts.PrintAsync(Arg.Any<SaleDto>(), Arg.Any<ReceiptHeaderDto>(),
                Arg.Any<CancellationToken>())
            .Returns(PrintOutcome.Printed);

        var sut = CreateSut();
        await sut.InitialiseAsync();

        _view.ReprintRequested += Raise.EventWith(this, EventArgs.Empty);

        await _receipts.Received().PrintAsync(
            Arg.Is<SaleDto>(s => s.DocumentNumber == "S-001-000002"),
            Arg.Is<ReceiptHeaderDto>(h => h.IsCopy),
            Arg.Any<CancellationToken>());
    }

    /// <remarks>
    /// A copy is a copy of the original: its time and its cashier, not
    /// the time it was reprinted and whoever reprinted it.
    /// </remarks>
    [Fact]
    public async Task A_copy_carries_the_original_time_and_cashier()
    {
        var soldAt = new DateTime(2026, 9, 30, 7, 15, 0, DateTimeKind.Utc);
        var earlier = SaleWithOneLine() with
        {
            DocumentNumber = "S-001-000002",
            OccurredAtUtc = soldAt,
            OperatorName = "joanne.m"
        };

        _sales.OpenAsync().Returns(Result<SaleDto>.Ok(EmptySale()));
        _sales.LastReceiptNumberAsync().Returns(Result<string>.Ok("S-001-000002"));
        _view.PromptReceiptNumber("S-001-000002").Returns("S-001-000002");
        _sales.FindReceiptAsync("S-001-000002").Returns(Result<SaleDto>.Ok(earlier));

        _receipts.PrintAsync(Arg.Any<SaleDto>(), Arg.Any<ReceiptHeaderDto>(),
                Arg.Any<CancellationToken>())
            .Returns(PrintOutcome.Printed);

        var sut = CreateSut();
        await sut.InitialiseAsync();

        _view.ReprintRequested += Raise.EventWith(this, EventArgs.Empty);

        await _receipts.Received().PrintAsync(Arg.Any<SaleDto>(),
            Arg.Is<ReceiptHeaderDto>(h => h.IsCopy
                && h.PrintedAtLocal == soldAt.ToLocalTime()
                && h.OperatorName == "joanne.m"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_receipt_that_cannot_be_found_is_reported_and_nothing_prints()
    {
        _sales.OpenAsync().Returns(Result<SaleDto>.Ok(EmptySale()));
        _sales.LastReceiptNumberAsync().Returns(Result<string>.Fail("POS-025",
            "No sale has been completed in this till session yet."));
        _view.PromptReceiptNumber(null).Returns("S-001-999999");
        _sales.FindReceiptAsync("S-001-999999").Returns(Result<SaleDto>.Fail(
            "POS-024", "There is no receipt S-001-999999."));

        var sut = CreateSut();
        await sut.InitialiseAsync();

        _view.ReprintRequested += Raise.EventWith(this, EventArgs.Empty);

        _view.Received().PromptReceiptNumber(null);
        _view.Received().ShowError("There is no receipt S-001-999999.");

        await _receipts.DidNotReceive().PrintAsync(Arg.Any<SaleDto>(),
            Arg.Any<ReceiptHeaderDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Backing_out_of_the_reprint_prints_nothing()
    {
        _sales.OpenAsync().Returns(Result<SaleDto>.Ok(EmptySale()));
        _sales.LastReceiptNumberAsync().Returns(Result<string>.Ok("S-001-000005"));
        _view.PromptReceiptNumber("S-001-000005").Returns((string?)null);

        var sut = CreateSut();
        await sut.InitialiseAsync();

        _view.ReprintRequested += Raise.EventWith(this, EventArgs.Empty);

        await _sales.DidNotReceive().FindReceiptAsync(Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await _receipts.DidNotReceive().PrintAsync(Arg.Any<SaleDto>(),
            Arg.Any<ReceiptHeaderDto>(), Arg.Any<CancellationToken>());
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
