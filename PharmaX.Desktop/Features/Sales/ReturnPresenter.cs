using PharmaX.Application.Sales;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Sales;

public interface IReturnView : IView
{
    string DocumentNumber { get; }
    string Kind { get; }

    void ShowSale(ReturnableSaleDto sale);
    void ShowTotals(decimal credit, decimal refunded);
    void ClearSale();

    IReadOnlyList<ReturnLineEditDto> CollectLines();

    string? PromptReason();
    Guid? PromptSupervisor(string reason);
    IReadOnlyList<PaymentDto>? PromptRefund(decimal amountDue,
        IReadOnlyList<string> tendersUsed);

    event EventHandler? FindRequested;
    event EventHandler? LinesChanged;
    event EventHandler? PostRequested;
    event EventHandler? CloseRequested;
}

/// <summary>
/// A refund is money leaving the drawer against goods the pharmacy has
/// already sold, so the flow is deliberately slower than a sale: find
/// the receipt, decide what may go back on the shelf, give a reason, get
/// a supervisor, then refund.
/// </summary>
public sealed class ReturnPresenter : PresenterBase<IReturnView>
{
    private readonly IReturnService _returns;
    private readonly IReceiptPrinter _receipts;
    private readonly ReceiptHeaderFactory _headers;

    private ReturnableSaleDto? _sale;

    public ReturnPresenter(IReturnService returns,
        IReceiptPrinter receipts, ReceiptHeaderFactory headers)
    {
        _returns = returns;
        _receipts = receipts;
        _headers = headers;
    }

    protected override void Subscribe()
    {
        View.FindRequested += (_, _) => _ = FindAsync();
        View.LinesChanged += (_, _) => Retotal();
        View.PostRequested += (_, _) => _ = PostAsync();
        View.CloseRequested += (_, _) => View.CloseView(true);
    }

    private Task FindAsync() => GuardAsync(async () =>
    {
        var result = await _returns.FindSaleAsync(View.DocumentNumber);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            View.ClearSale();
            _sale = null;
            return;
        }

        _sale = result.Value;

        if (_sale.Lines.All(l => l.Returnable <= 0m))
        {
            View.ShowInfo(
                "Everything on that sale has already been returned or "
                + "voided, so there is nothing left to take back.");
        }

        View.ShowSale(_sale);
        Retotal();
    });

    private void Retotal()
    {
        if (_sale is null)
        {
            View.ShowTotals(0m, 0m);
            return;
        }

        var credit = View.CollectLines()
            .Join(_sale.Lines, e => e.OriginalLineNo, l => l.LineNo,
                  (e, l) => e.Quantity * l.UnitPrice)
            .Sum();

        View.ShowTotals(credit, 0m);
    }

    private Task PostAsync() => GuardAsync(async () =>
    {
        if (_sale is null)
        {
            View.ShowError("Find the original sale first.");
            return;
        }

        var lines = View.CollectLines();

        if (lines.Count == 0)
        {
            View.ShowError("Choose at least one line to return.");
            return;
        }

        var credit = lines
            .Join(_sale.Lines, e => e.OriginalLineNo, l => l.LineNo,
                  (e, l) => e.Quantity * l.UnitPrice)
            .Sum();

        // The reason comes before the supervisor: the supervisor is
        // being asked to authorise something specific, and cannot do
        // that if nobody has said what it is.
        var reason = View.PromptReason();

        if (string.IsNullOrWhiteSpace(reason))
        {
            View.ShowError("A return needs a reason.");
            return;
        }

        var authoriser = View.PromptSupervisor(
            $"Authorise a refund of {credit:N2} against "
            + $"{_sale.DocumentNumber}?");

        if (authoriser is null)
        {
            View.ShowError("Not refunded: no supervisor authorisation.");
            return;
        }

        // The tenders the customer originally paid with are offered
        // first: refunding cash on a card sale is how a till ends up
        // short with a matching card slip nobody can explain.
        var refunds = View.PromptRefund(credit, _sale.TendersUsed);

        if (refunds is null) return;

        var result = await _returns.CreateAsync(new ReturnEditDto(
            SaleId: _sale.SaleId,
            Kind: View.Kind,
            Reason: reason,
            AuthorisedBy: authoriser.Value,
            Lines: lines,
            Refunds: refunds));

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        var written = result.Value.WriteOffValue;

        View.ShowInfo(
            $"Credit note {result.Value.DocumentNumber} for "
            + $"{result.Value.Total:N2}. "
            + $"{result.Value.RestockedLines} line(s) back on the shelf"
            + (written > 0m ? $", {written:N2} written off." : "."));

        View.ClearSale();
        _sale = null;
        Retotal();
    });
}
