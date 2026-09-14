using PharmaX.Application.Sales;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Application.Security;
using PharmaX.Desktop.Mvp;
using PharmaX.Domain.Security;

namespace PharmaX.Desktop.Features.Sales;

public sealed class PosPresenter : PresenterBase<IPosView>
{
    private readonly ISaleService _sales;
    private readonly ICollectionService _collections;
    private readonly IPermissionService _permissions;
    private readonly IReceiptPrinter _receipts;
    private readonly ICashDrawer _drawer;
    private readonly ReceiptHeaderFactory _headers;

    private Guid _saleId;
    private SaleDto? _sale;

    /// <summary>
    /// Set before the screen loads where the till was opened on a script
    /// waiting to be collected rather than on an empty basket.
    /// </summary>
    private Guid? _collecting;

    public PosPresenter(ISaleService sales, ICollectionService collections,
        IPermissionService permissions, IReceiptPrinter receipts,
        ICashDrawer drawer, ReceiptHeaderFactory headers)
    {
        _sales = sales;
        _collections = collections;
        _permissions = permissions;
        _receipts = receipts;
        _drawer = drawer;
        _headers = headers;
    }

    /// <summary>
    /// Opens the till on one dispensed script instead of an empty sale.
    /// Called by the collection screen before the form loads.
    /// </summary>
    public void Collect(Guid dispensingId) => _collecting = dispensingId;

    protected override void Subscribe()
    {
        View.CodeScanned += (_, code) => _ = OnScanAsync(code);
        View.ProductSearchRequested += (_, _) => _ = OnSearchAsync(null);
        View.CustomerRequested += (_, _) => _ = OnCustomerAsync();
        View.QuantityRequested += (_, _) => _ = OnQuantityAsync();
        View.LineDiscountRequested += (_, _) => _ = OnDiscountAsync();
        View.VoidLineRequested += (_, _) => _ = OnVoidAsync();
        View.PaymentRequested += (_, _) => _ = OnPaymentAsync();
        View.HoldRequested += (_, _) => _ = OnHoldAsync();
        // False, not true. The collection screen reads this dialog's
        // result as "the patient paid", and closing the till is exactly
        // the case where they did not - so it was telling the cashier
        // the money had been taken when nothing had.
        View.CloseRequested += (_, _) => View.CloseView(false);
    }

    public override Task InitialiseAsync() =>
        _collecting is Guid dispensingId
            ? StartCollectionAsync(dispensingId)
            : StartNewSaleAsync();

    /// <summary>
    /// Loads the script's co-payment as a sale, ready to be tendered.
    /// </summary>
    /// <remarks>
    /// The lines are priced by the service from what the dispensing
    /// says the patient owes, so there is nothing to scan and nothing
    /// for the cashier to key: the till opens with the amount already
    /// on it and the only thing left is the money. A failure here -
    /// most likely another till having taken it a second earlier - is
    /// reported and the screen closes rather than sitting open on a
    /// sale the cashier cannot complete.
    /// </remarks>
    private Task StartCollectionAsync(Guid dispensingId) =>
        GuardAsync(async () =>
        {
            var result = await _collections.StartAsync(dispensingId);

            if (result.IsFailure)
            {
                View.ShowError(result.Error.Message);
                View.CloseView(false);
                return;
            }

            _sale = result.Value;
            _saleId = _sale.Id;

            View.ClearSale();
            View.ShowLines(_sale.Lines);
            View.ShowTotals(_sale.Totals);
            View.FocusPayment();
        });

    private Task StartNewSaleAsync() => GuardAsync(async () =>
    {
        var result = await _sales.OpenAsync();

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            View.CloseView(false);
            return;
        }

        _sale = result.Value;
        _saleId = _sale.Id;
        View.ClearSale();
        View.ShowTotals(_sale.Totals);
        View.FocusScan();
    });

    /// <summary>
    /// One box for both ways of finding an item. Anything typed or
    /// scanned is tried as a code or barcode first, because that is what
    /// the great majority of entries at a till are.
    /// </summary>
    /// <remarks>
    /// Only a code that matched nothing falls through to the search:
    /// POS-001 is "no product for that code", whereas a discontinued
    /// product or a batch with no stock did identify something and must
    /// still be reported rather than quietly reopened as a search.
    /// </remarks>
    private Task OnScanAsync(string code) => GuardAsync(async () =>
    {
        var result = await _sales.AddLineAsync(_saleId, code, quantity: 1m);

        if (result.IsFailure)
        {
            if (string.Equals(result.Error.Code, "POS-001",
                              StringComparison.Ordinal))
            {
                await SearchAsync(code);
                return;
            }

            View.ShowError(result.Error.Message);
            View.FocusScan();
            return;
        }

        await RefreshAsync();
    });

    /// <summary>
    /// Puts the sale on a customer's account.
    /// </summary>
    /// <remarks>
    /// The account is what makes the Account tender mean anything: a
    /// sale tendered to an account with none chosen is refused at
    /// finalise, and was silently allowed before accounts existed.
    /// </remarks>
    private Task OnCustomerAsync() => GuardAsync(async () =>
    {
        if (View.PromptAccount() is not Application.Patients.AccountDto
            chosen)
        {
            View.FocusScan();
            return;
        }

        var result = await _sales.AssignAccountAsync(_saleId, chosen.Id);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            View.FocusScan();
            return;
        }

        var account = result.Value;

        // What the cashier needs on screen is how much room is left on
        // the account, because that is what decides whether the sale can
        // go on it at all.
        View.ShowCustomer(account.Name,
            string.Format(System.Globalization.CultureInfo.CurrentCulture,
                "Account {0}   owes {1}   {2} available",
                account.AccountNumber, Format.Money(account.Balance),
                Format.Money(account.Available)),
            ratedPricing: false);

        View.FocusScan();
    });

    private Task OnSearchAsync(string? term) =>
        GuardAsync(() => SearchAsync(term));

    /// <summary>
    /// Finds a product by name and sells it by its code, so a searched
    /// item and a scanned one reach the domain by the same route.
    /// </summary>
    private async Task SearchAsync(string? term)
    {
        var code = View.PromptProductSearch(term);

        // Cancelled. The operator has already seen what did and did not
        // match, so an error on top of that would only be noise.
        if (string.IsNullOrWhiteSpace(code))
        {
            View.FocusScan();
            return;
        }

        var result = await _sales.AddLineAsync(_saleId, code, quantity: 1m);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            View.FocusScan();
            return;
        }

        await RefreshAsync();
    }

    private Task OnQuantityAsync() => GuardAsync(async () =>
    {
        if (View.SelectedLineNo is not int lineNo) return;

        var line = _sale!.Lines.Single(l => l.LineNo == lineNo);
        var quantity = View.PromptQuantity(line.Quantity);
        if (quantity is null) return;

        var result = await _sales.SetLineQuantityAsync(
            _saleId, lineNo, quantity.Value);

        if (result.IsFailure) View.ShowError(result.Error.Message);

        await RefreshAsync();
    });

    private Task OnDiscountAsync() => GuardAsync(async () =>
    {
        if (View.SelectedLineNo is not int lineNo) return;

        var line = _sale!.Lines.Single(l => l.LineNo == lineNo);
        var amount = View.PromptDiscount(line.LineTotal);
        if (amount is null) return;

        Guid? authoriser = null;

        // The presenter decides that a supervisor is needed and asks the
        // view to collect one. It never evaluates the rule itself.
        if (_permissions.Check(PermissionCode.SaleDiscountAboveLimit)
                == PermissionOutcome.RequiresSupervisor)
        {
            authoriser = View.PromptSupervisor(
                "This discount is above your limit.");

            if (authoriser is null)
            {
                View.ShowError("Discount not applied.");
                return;
            }
        }

        var result = await _sales.ApplyLineDiscountAsync(
            _saleId, lineNo, amount.Value, authoriser);

        if (result.IsFailure) View.ShowError(result.Error.Message);
        await RefreshAsync();
    });

    private Task OnVoidAsync() => GuardAsync(async () =>
    {
        if (View.SelectedLineNo is not int lineNo) return;

        var reason = View.PromptVoidReason();
        if (string.IsNullOrWhiteSpace(reason)) return;

        var result = await _sales.VoidLineAsync(_saleId, lineNo, reason);
        if (result.IsFailure) View.ShowError(result.Error.Message);

        await RefreshAsync();
    });

    private Task OnHoldAsync() => GuardAsync(() =>
    {
        // Parked rather than cancelled: the basket stays open in the
        // database and the next sale starts clean over the top of it.
        View.ShowInfo("Sale held. Recall it from the till session screen.");
        return StartNewSaleAsync();
    });

    private Task OnPaymentAsync() => GuardAsync(async () =>
    {
        if (_sale is null || _sale.Lines.Count == 0)
        {
            View.ShowError("Add at least one item before taking payment.");
            return;
        }

        var payments = View.PromptPayment(_sale.Totals.Total);
        if (payments is null) return;

        var result = await _sales.FinaliseAsync(_saleId, payments);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.ShowDocumentNumber(result.Value.DocumentNumber);

        // Finalise, then print. The other way round leaves a printed
        // receipt for a sale that never committed, which is the worse of
        // the two failures (Section 20.2).
        await PrintAsync(payments);

        // A collection is one patient's script and it is now paid for.
        // Handing the cashier a fresh empty basket would leave them one
        // window away from the queue with the next patient already
        // waiting in it, so the till closes and the queue comes back.
        if (_collecting is not null)
        {
            View.CloseView(true);
            return;
        }

        await StartNewSaleAsync();
    });

    private async Task PrintAsync(IReadOnlyList<PaymentDto> payments)
    {
        var final = await _sales.GetAsync(_saleId);
        if (final.IsFailure) return;

        if (payments.Any(p => string.Equals(p.TenderType, "Cash",
                StringComparison.OrdinalIgnoreCase)))
            _drawer.Open();

        var outcome = _receipts.Print(final.Value, _headers.Build());

        if (outcome != PrintOutcome.Printed)
            View.ShowError(
                "The sale is complete but the receipt did not print. "
                + "Reprint it from the till session screen.");
    }

    private async Task RefreshAsync()
    {
        var result = await _sales.GetAsync(_saleId);
        if (result.IsFailure) return;

        _sale = result.Value;
        View.ShowLines(_sale.Lines);
        View.ShowTotals(_sale.Totals);
        View.FocusScan();
    }
}
