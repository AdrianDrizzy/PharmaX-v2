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

    private readonly Application.Abstractions.ICurrentUser? _user;
    private readonly IReceiptParticularsService? _particulars;

    /// Runtime. Resolved by the container, which picks the fullest
    /// constructor it can satisfy.
    public PosPresenter(ISaleService sales, ICollectionService collections,
        IPermissionService permissions, IReceiptPrinter receipts,
        ICashDrawer drawer, ReceiptHeaderFactory headers,
        Application.Abstractions.ICurrentUser user,
        IReceiptParticularsService particulars,
        Application.Tills.ITillSessionService tills)
        : this(sales, collections, permissions, receipts, drawer, headers,
               user)
    {
        _particulars = particulars;
        _tills = tills;
    }

    private readonly Application.Tills.ITillSessionService? _tills;

    public PosPresenter(ISaleService sales, ICollectionService collections,
        IPermissionService permissions, IReceiptPrinter receipts,
        ICashDrawer drawer, ReceiptHeaderFactory headers,
        Application.Abstractions.ICurrentUser user)
        : this(sales, collections, permissions, receipts, drawer, headers) =>
        _user = user;

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
        View.CodeScanned += (_, code) => Enqueue(() => OnScanAsync(code));
        View.ProductSearchRequested += (_, _) => Enqueue(() => OnSearchAsync(null));
        View.CustomerRequested += (_, _) => Enqueue(OnCustomerAsync);
        View.QuantityRequested += (_, _) => Enqueue(OnQuantityAsync);
        View.LineDiscountRequested += (_, _) => Enqueue(OnDiscountAsync);
        View.VoidLineRequested += (_, _) => Enqueue(OnVoidAsync);
        View.PaymentRequested += (_, _) => Enqueue(OnPaymentAsync);
        View.HoldRequested += (_, _) => Enqueue(OnHoldAsync);
        View.RecallRequested += (_, _) => Enqueue(OnRecallAsync);
        View.QuotationRequested += (_, _) => Enqueue(OnQuotationAsync);
        View.ReprintRequested += (_, _) => Enqueue(OnReprintAsync);
        View.CloseRequested += (_, _) => Enqueue(OnCloseAsync);
        View.LockRequested += (_, _) => Enqueue(OnLockAsync);
        View.NoSaleRequested += (_, _) => Enqueue(OnNoSaleAsync);
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
            View.ShowRatedPricing(_sale.Lines.Any(l => l.IsRated));
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

        // What the cashier needs on screen is how much room is left on
        // the account, because that is what decides whether the sale can
        // go on it at all.
        ShowAccount(result.Value);

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

    /// <summary>
    /// The line F4, F5 and Esc act on. They returned without a word when
    /// none was chosen, which reads as a till that ignores its keys.
    /// </summary>
    private int? ChosenLine()
    {
        if (View.SelectedLineNo is int lineNo) return lineNo;

        View.ShowInfo(_sale is { Lines.Count: > 0 }
            ? "Choose a line on the sale first."
            : "There is nothing on this sale yet.");

        return null;
    }

    private Task OnQuantityAsync() => GuardAsync(async () =>
    {
        if (ChosenLine() is not int lineNo) return;

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
        if (ChosenLine() is not int lineNo) return;

        // Off the line before any discount, so "10%" means a tenth of
        // what the goods are priced at rather than of what is left.
        var line = _sale!.Lines.Single(l => l.LineNo == lineNo);
        var amount = View.PromptDiscount(line.UnitPrice * line.Quantity);
        if (amount is null) return;

        Guid? authoriser = null;

        // A supervisor only for a discount over the operator's own limit.
        // Every discount a cashier gave used to need one - 1.00 off a
        // 34.95 line from a cashier allowed 5% - which is an authorisation
        // the business rule does not ask for. The service and the sale
        // enforce the limit again whatever the screen decides.
        var gross = line.UnitPrice * line.Quantity;
        var overLimit = _user is null
            || amount.Value > Math.Round(
                gross * _user.DiscountLimitPercent / 100m, 2,
                MidpointRounding.AwayFromZero);

        if (overLimit
            && _permissions.Check(PermissionCode.SaleDiscountAboveLimit)
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
        if (ChosenLine() is not int lineNo) return;

        var reason = View.PromptVoidReason();
        if (string.IsNullOrWhiteSpace(reason)) return;

        var result = await _sales.VoidLineAsync(_saleId, lineNo, reason);
        if (result.IsFailure) View.ShowError(result.Error.Message);

        await RefreshAsync();
    });

    /// <summary>
    /// Parks the basket - lines, quantities, discounts and customer - so
    /// the till can serve the next person. Recalled with F9 at any till.
    /// </summary>
    private Task OnHoldAsync() => GuardAsync(async () =>
    {
        if (!HasLines)
        {
            View.ShowInfo("There is nothing on this sale to hold.");
            return;
        }

        var result = await _sales.HoldAsync(_saleId);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.ShowInfo("Sale held. Recall it with F9 on any till.");
        await StartNewSaleAsync();
    });

    /// <summary>
    /// Takes a held basket back up and carries on with it.
    /// </summary>
    private Task OnRecallAsync() => GuardAsync(async () =>
    {
        var held = await _sales.HeldAsync();

        if (held.IsFailure)
        {
            View.ShowError(held.Error.Message);
            return;
        }

        if (held.Value.Count == 0)
        {
            View.ShowInfo("No sales are held.");
            return;
        }

        if (View.PromptHeldSale(held.Value) is not Guid chosen) return;

        // What is on the till now is parked in its place rather than lost.
        var busy = HasLines;

        if (busy)
        {
            if (!View.Confirm("Hold the sale that is on the till now, and "
                              + "recall the other one?"))
                return;

            var parked = await _sales.HoldAsync(_saleId);

            if (parked.IsFailure)
            {
                View.ShowError(parked.Error.Message);
                return;
            }
        }

        var recalled = await _sales.RecallAsync(chosen,
            replacingSaleId: busy ? null : _saleId);

        if (recalled.IsFailure)
        {
            View.ShowError(recalled.Error.Message);
            return;
        }

        _sale = recalled.Value;
        _saleId = _sale.Id;

        View.ClearSale();

        // The customer comes back with the basket, re-checked: an account
        // put on hold while the sale was parked says so now rather than
        // at the payment screen.
        if (_sale.AccountId is Guid accountId)
        {
            var account = await _sales.AssignAccountAsync(_saleId, accountId);

            if (account.IsSuccess)
                ShowAccount(account.Value);
            else
                View.ShowError(account.Error.Message);
        }

        await RefreshAsync();
    });

    /// <summary>
    /// Brings a quotation the customer has brought back onto the sale, at
    /// today's prices and against today's stock.
    /// </summary>
    private Task OnQuotationAsync() => GuardAsync(async () =>
    {
        if (HasLines)
        {
            View.ShowError("Finish or hold this sale first: a quotation is "
                           + "brought onto an empty one.");
            return;
        }

        var number = View.PromptQuotationNumber();
        if (string.IsNullOrWhiteSpace(number)) return;

        var result = await _sales.ConvertQuotationAsync(_saleId, number);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        await RefreshAsync();

        var summary = $"Quotation {result.Value.QuotationNumber} for "
                      + $"{result.Value.CustomerName}: "
                      + $"{result.Value.LinesAdded} item(s) added.";

        if (result.Value.Changes.Count > 0)
            summary += Environment.NewLine + Environment.NewLine
                       + "Since it was quoted:" + Environment.NewLine
                       + string.Join(Environment.NewLine, result.Value.Changes);

        View.ShowInfo(summary);
    });

    /// The last finalised sale on this till, for a reprint.
    private Guid? _lastSaleId;

    /// <remarks>
    /// Offers the last receipt this till printed - the one this window
    /// finalised, or the last of the till's session, which survives the
    /// window being closed and opened again - and takes any other number
    /// typed over it, for a customer back for a copy of an earlier one.
    /// It used to know only the sale this window had finalised, so a
    /// reopened till had nothing to reprint (OCT-DEF-05).
    /// </remarks>
    /// The till held for the operator, the session suspended meanwhile.
    private Task OnLockAsync() => GuardAsync(async () =>
    {
        if (_tills is null || _user is null) return;

        await _tills.SuspendAsync();
        View.PromptUnlock(_user.Username);
        await _tills.ResumeAsync();
    });

    /// The drawer opened with nothing sold, and why, on the audit trail.
    private Task OnNoSaleAsync() => GuardAsync(async () =>
    {
        if (_tills is null || View.PromptNoSaleReason() is not { } reason) return;

        var logged = await _tills.NoSaleAsync(reason);

        if (logged.IsFailure)
        {
            View.ShowError(logged.Error.Message);
            return;
        }

        await _drawer.OpenAsync();
    });

    private Task OnReprintAsync() => GuardAsync(async () =>
    {
        string? last = null;

        if (_lastSaleId is Guid saleId
            && await _sales.GetAsync(saleId) is { IsSuccess: true } recent)
            last = recent.Value.DocumentNumber;
        else if (await _sales.LastReceiptNumberAsync() is { IsSuccess: true } session)
            last = session.Value;

        var number = View.PromptReceiptNumber(last);
        if (string.IsNullOrWhiteSpace(number)) return;

        var sale = await _sales.FindReceiptAsync(number);
        if (sale.IsFailure)
        {
            View.ShowError(sale.Error.Message);
            return;
        }

        // Marked as a copy, and recorded: the first one has already been
        // handed over (ADM-DEF-08). Dated and signed as the original was:
        // a copy of this morning's receipt printed this afternoon carried
        // the afternoon's time and the name of whoever reprinted it.
        var header = _headers.Build();

        var result = await _receipts.PrintAsync(
            await ForReceiptAsync(sale.Value),
            header with
            {
                IsCopy = true,
                PrintedAtLocal = sale.Value.OccurredAtUtc is DateTime sold
                    ? sold.ToLocalTime()
                    : header.PrintedAtLocal,
                OperatorName = sale.Value.OperatorName ?? header.OperatorName
            });

        if (result.Printed && _particulars is not null)
            await _particulars.RecordReprintAsync(sale.Value);

        var what = $"Copy of receipt {sale.Value.DocumentNumber}";

        if (result.Printed)
            View.ShowInfo(result.Describe(what));
        else
            View.ShowError(result.Describe(what));
    });

    /// <summary>
    /// Closes the till without leaving a basket nobody can reach.
    /// </summary>
    /// <remarks>
    /// An empty sale is abandoned quietly - opening Sell used to leave one
    /// behind every time (DEF-0030). One with items on it is held or
    /// discarded, as the cashier chooses. The dialog result is false
    /// either way: the collection screen reads it as "the patient paid",
    /// and closing the till is exactly the case where they did not.
    /// </remarks>
    private Task OnCloseAsync() => GuardAsync(async () =>
    {
        if (_collecting is null && _sale is not null
            && string.Equals(_sale.Status, "Open", StringComparison.Ordinal))
        {
            if (!HasLines)
            {
                await _sales.AbandonAsync(_saleId,
                    "Till closed with an empty sale.");
            }
            else if (View.Confirm("Hold this sale so it can be recalled "
                                  + "later?"))
            {
                var held = await _sales.HoldAsync(_saleId);
                if (held.IsFailure)
                {
                    View.ShowError(held.Error.Message);
                    return;
                }
            }
            else if (View.Confirm("Discard this sale? Nothing has been paid "
                                  + "and nothing will be kept."))
            {
                await _sales.AbandonAsync(_saleId,
                    "Discarded when the till was closed.");
            }
            else
            {
                return;
            }
        }

        View.CloseView(false);
    });

    private bool HasLines =>
        _sale is not null && _sale.Lines.Any(l => !l.IsVoided);

    private void ShowAccount(Application.Patients.AccountDto account) =>
        View.ShowCustomer(account.Name,
            string.Format(System.Globalization.CultureInfo.CurrentCulture,
                "Account {0}   owes {1}   {2} available",
                account.AccountNumber, Format.Money(account.Balance),
                Format.Money(account.Available)),
            ratedPricing: false);

    /// <summary>
    /// Takes the money, finalises, prints.
    /// </summary>
    /// <remarks>
    /// The payment dialog is put up before the busy cursor rather than
    /// under it. GuardAsync raises the hourglass for as long as its
    /// action runs, and the action used to include the dialog - so the
    /// cashier counted the cash, chose a tender and typed a reference
    /// with a wait cursor over the screen, which reads as an application
    /// that has hung. Waiting for a person is not the application being
    /// busy.
    /// </remarks>
    private Task OnPaymentAsync()
    {
        if (_sale is null || _sale.Lines.Count == 0)
        {
            View.ShowError("Add at least one item before taking payment.");
            return Task.CompletedTask;
        }

        // A pharmacist assistant makes the basket up and holds it. The
        // cashier recalls it on the till that has the drawer and takes the
        // money there, so it lands in that till's session.
        if (!_permissions.Has(PermissionCode.SaleTakePayment))
        {
            View.ShowInfo("Press F8 Hold. The cashier recalls it with F9 on "
                          + "the till with the drawer and takes the money there.");
            return Task.CompletedTask;
        }

        var payments = View.PromptPayment(_sale.Totals.Total);
        if (payments is null) return Task.CompletedTask;

        return FinaliseAsync(payments);
    }

    private Task FinaliseAsync(IReadOnlyList<PaymentDto> payments) =>
        GuardAsync(async () =>
    {
        var result = await _sales.FinaliseAsync(_saleId, payments);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.ShowDocumentNumber(result.Value.DocumentNumber);
        View.ShowChange(result.Value.Change);
        _lastSaleId = _saleId;

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
            await _drawer.OpenAsync();

        var printed = await _receipts.PrintAsync(
            await ForReceiptAsync(final.Value), _headers.Build());

        // Said honestly, with the reason and the way to put it right; a
        // printer fault never undoes the sale (POS-014).
        if (!printed.Printed)
            View.ShowError(
                "The sale is complete but the receipt did not print. "
                + (printed.Problem ?? "") + Environment.NewLine
                + Environment.NewLine
                + "Press F12 to reprint it once the printer is ready.");
        else if (printed.Problem is not null)
            View.ShowInfo(printed.Describe("The receipt"));
    }

    /// The sale with whose it is filled in, where anything can say.
    private async Task<SaleDto> ForReceiptAsync(SaleDto sale) =>
        _particulars is null
            ? sale
            : await _particulars.ForReceiptAsync(sale);

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
