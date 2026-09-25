using PharmaX.Application.Sales;
using PharmaX.Domain.Security;
using PharmaX.Application.Security;
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
    private readonly IDocumentPrinter _printer;
    private readonly ReceiptHeaderFactory _headers;
    private readonly IPermissionService _permissions;

    private ReturnableSaleDto? _sale;

    public ReturnPresenter(IReturnService returns,
        IDocumentPrinter printer, ReceiptHeaderFactory headers,
        IPermissionService permissions)
    {
        _returns = returns;
        _printer = printer;
        _headers = headers;
        _permissions = permissions;
    }

    protected override void Subscribe()
    {
        View.FindRequested += (_, _) => Enqueue(FindAsync);
        View.LinesChanged += (_, _) => Retotal();
        View.PostRequested += (_, _) => Enqueue(PostAsync);
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

        // A supervisor's credentials only where the operator does not
        // hold the authority themselves; a manager refunding at the till
        // is not asked to authorise their own work.
        Guid? authoriser = null;

        if (_permissions.Check(PermissionCode.ReturnAuthorise)
            != PermissionOutcome.Granted)
        {
            authoriser = View.PromptSupervisor(
                $"Authorise a refund of {credit:N2} against "
                + $"{_sale.DocumentNumber}?");

            if (authoriser is null)
            {
                View.ShowError("Not refunded: no supervisor authorisation.");
                return;
            }
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
            AuthorisedBy: authoriser ?? Guid.Empty,
            Lines: lines,
            Refunds: refunds));

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        var written = result.Value.WriteOffValue;

        // The customer's copy of the credit note, printed and reported
        // honestly: a refund that happened with a slip that did not is
        // still a refund.
        var printed = await _printer.PrintAsync(
            CreditNote(result.Value, refunds), _headers.Build());

        View.ShowInfo(
            $"Credit note {result.Value.DocumentNumber} for "
            + $"{result.Value.Total:N2}. "
            + $"{result.Value.RestockedLines} line(s) back on the shelf"
            + (written > 0m ? $", {written:N2} written off." : ".")
            + Environment.NewLine + Environment.NewLine
            + printed.Describe("The credit note"));

        View.ClearSale();
        _sale = null;
        Retotal();
    });

    /// <summary>
    /// The credit note as it prints: what came back, against which sale,
    /// and how the money went back to the customer.
    /// </summary>
    private static PrintableDocument CreditNote(ReturnDto credit,
        IReadOnlyList<PaymentDto> refunds)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        var lines = new List<string>
        {
            $"Credit note {credit.DocumentNumber}",
            $"Against sale {credit.OriginalDocumentNumber}",
            new string('-', 42)
        };

        foreach (var line in credit.Lines)
            lines.Add(string.Format(culture, "{0,5:0.##} {1,-24}{2,11:N2}",
                line.Quantity,
                line.Description.Length <= 24
                    ? line.Description
                    : line.Description[..24],
                line.LineTotal));

        lines.Add(new string('-', 42));
        lines.Add(string.Format(culture, "{0,-30}{1,12:N2}", "Refunded",
            credit.Total));

        foreach (var refund in refunds)
            lines.Add(string.Format(culture, "  {0,-28}{1,12:N2}",
                refund.TenderType, refund.Amount));

        lines.Add("");
        lines.Add($"Reason: {credit.Reason}");

        return new PrintableDocument($"Credit note {credit.DocumentNumber}",
                                     lines);
    }
}
