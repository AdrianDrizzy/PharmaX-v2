using System.Globalization;
using PharmaX.Application.Sales;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Features.Inventory;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Sales;

/// <summary>
/// Point of Sale's Quotation: a priced list for a customer who has not
/// bought anything.
/// </summary>
/// <remarks>
/// Reads like the till on purpose - the same scan bar, the same
/// find-by-name - because the person using it is the person who uses the
/// till. It moves no stock and takes no money, and the service keeps it
/// that way.
/// </remarks>
public partial class frmQuotation : PhxFormBase
{
    private readonly IQuotationService? _quotations;
    private readonly ViewFactory? _views;
    private readonly IDocumentPrinter? _printer;
    private readonly ReceiptHeaderFactory? _headers;
    private readonly Application.Reporting.IWorkbookWriter? _workbooks;
    private readonly Application.Abstractions.ICurrentUser? _user;

    private QuotationDto? _current;

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.SaleCreate;

    // Escape closes the screen, which is what it should do here: there
    // is nothing to void.
    protected override bool ClosesOnEscape => true;

    /// Designer only. Never called at runtime.
    public frmQuotation()
    {
        InitializeComponent();

        btnStart.Click += (_, _) => Operations.Enqueue(StartAsync);
        btnFind.Click += (_, _) => Operations.Enqueue(() => FindAsync());
        btnPrint.Click += (_, _) => Operations.Enqueue(PrintAsync);
        btnExport.Click += (_, _) => Export();
        btnOpen.Click += (_, _) => Operations.Enqueue(OpenExistingAsync);
        btnRemove.Click += (_, _) => Operations.Enqueue(RemoveAsync);
        btnIssue.Click += (_, _) => Operations.Enqueue(IssueAsync);
        btnClose.Click += (_, _) => CloseView(true);

        scanBar.CodeEntered += (_, code) => Operations.Enqueue(() => AddAsync(code));

        ShowNoQuotation();
    }

    /// Runtime. Resolved by the container.
    public frmQuotation(IQuotationService quotations, ViewFactory views,
        IDocumentPrinter printer, ReceiptHeaderFactory headers,
        Application.Reporting.IWorkbookWriter workbooks,
        Application.Abstractions.ICurrentUser user,
        Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopes)
        : this()
    {
        _quotations = quotations;
        _views = views;
        _printer = printer;
        _headers = headers;
        _workbooks = workbooks;
        _user = user;

        // A few words of a name offer the matching products under the scan
        // bar, as at the till. Without it a name typed slowly was sent as
        // a code a third of a second after the typing paused.
        scanBar.SuggestFrom(Suggest.Products(scopes, overTheCounter: true));
    }

    private void ShowNoQuotation()
    {
        lblNumber.Text = "Not started";
        lblTotalValue.Text = Format.Money(0m);
        lblValid.Text = "Name the customer, then start the quotation.";

        scanBar.Enabled = false;
        btnFind.Enabled = false;
        btnRemove.Enabled = false;
        btnIssue.Enabled = false;
        btnPrint.Enabled = false;
        btnExport.Enabled = false;
    }

    /// <summary>
    /// Prints the quotation on this till's printer, under the pharmacy's
    /// fiscal header, and says honestly whether it printed.
    /// </summary>
    private Task PrintAsync() => GuardAsync(async () =>
    {
        if (_current is null || _printer is null || _headers is null) return;

        var result = await _printer.PrintAsync(
            QuotationDocument.For(_current), _headers.Build());

        if (result.Printed)
            ShowInfo(result.Describe($"Quotation {_current.QuotationNumber}"));
        else
            ShowError(result.Describe($"Quotation {_current.QuotationNumber}"));
    });

    /// The lines as a workbook, for a customer who asked for it by email.
    private void Export()
    {
        if (_current is null || _workbooks is null || _user is null) return;

        var saved = GridExport.Offer(this, _workbooks, grdLines,
            $"Quotation {_current.QuotationNumber}",
            $"{_current.CustomerName}, valid until "
            + $"{_current.ValidUntil:dd/MM/yyyy}", _user.Username);

        if (saved is not null) ShowSaved(saved);
    }

    /// <summary>
    /// A quotation already issued, found by the number on it, to read or
    /// print again. Converting it to a sale happens at the till.
    /// </summary>
    private Task OpenExistingAsync() => GuardAsync(async () =>
    {
        if (_quotations is null) return;

        var number = frmTextPrompt.Ask(this, "Open quotation",
            "Quotation number (on the customer's copy)",
            hint: "e.g. Q-001-000001");

        if (string.IsNullOrWhiteSpace(number)) return;

        var result = await _quotations.FindAsync(number);

        if (result.IsFailure)
        {
            ShowError(result.Error.Message);
            return;
        }

        txtCustomer.Text = result.Value.CustomerName;
        txtCustomer.ReadOnly = true;
        btnStart.Enabled = false;

        Show(result.Value);
    });

    private Task StartAsync() => GuardAsync(async () =>
    {
        if (_quotations is null) return;

        var result = await _quotations.OpenAsync(txtCustomer.Text.Trim());

        if (result.IsFailure)
        {
            ShowError(result.Error.Message);
            return;
        }

        Show(result.Value);

        txtCustomer.ReadOnly = true;
        btnStart.Enabled = false;
        scanBar.TakeFocus();
    });

    private Task AddAsync(string code) => GuardAsync(async () =>
    {
        if (_quotations is null || _current is null) return;

        var result = await _quotations.AddLineAsync(_current.Id, code, 1m);

        if (result.IsFailure)
        {
            // The same fall-through the till uses: a code that matched
            // nothing becomes a search for what was typed.
            if (string.Equals(result.Error.Code, "POS-001",
                              StringComparison.Ordinal))
            {
                await FindAsync(code);
                return;
            }

            ShowError(result.Error.Message);
            scanBar.TakeFocus();
            return;
        }

        Show(result.Value);
        scanBar.TakeFocus();
    });

    private Task FindAsync(string? term = null) => GuardAsync(async () =>
    {
        if (_quotations is null || _current is null || _views is null) return;

        using var picker = _views.Create<frmProductList>();
        picker.UseAsPicker(term);

        if (picker.ShowDialog(this) != DialogResult.OK) return;

        var codes = picker.SelectedCodes;
        var failed = new List<string>();

        // Each one as if it were picked alone; a refusal does not stop the rest.
        foreach (var code in codes)
        {
            var result = await _quotations.AddLineAsync(_current.Id, code, 1m);

            if (result.IsFailure)
                failed.Add(codes.Count == 1
                    ? result.Error.Message
                    : $"{code}: {result.Error.Message}");
            else
                Show(result.Value);
        }

        if (failed.Count > 0)
        {
            ShowError(string.Join("\n", failed));
            if (failed.Count == codes.Count) return;
        }

        scanBar.TakeFocus();
    });

    private Task RemoveAsync() => GuardAsync(async () =>
    {
        if (_quotations is null || _current is null) return;

        // Bottom up, in case taking one off numbers the rest again.
        var lineNos = grdLines.SelectedInOrder().Select(r => r.DataBoundItem)
            .OfType<QuotationLineDto>().Select(l => l.LineNo).Reverse().ToList();

        if (lineNos.Count == 0) return;

        var failed = new List<string>();

        foreach (var lineNo in lineNos)
        {
            var result = await _quotations.RemoveLineAsync(_current.Id, lineNo);

            if (result.IsFailure)
                failed.Add(lineNos.Count == 1
                    ? result.Error.Message
                    : $"Line {lineNo}: {result.Error.Message}");
            else
                Show(result.Value);
        }

        if (failed.Count > 0) ShowError(string.Join("\n", failed));
    });

    private Task IssueAsync() => GuardAsync(async () =>
    {
        if (_quotations is null || _current is null) return;

        if (_current.Lines.Count == 0)
        {
            ShowError("Add at least one item before issuing the quotation.");
            return;
        }

        var notes = frmTextPrompt.Ask(this, "Quotation",
            "Anything to add to the quotation? (optional)",
            hint: "e.g. Stock to be ordered in");

        var result = await _quotations.IssueAsync(_current.Id, notes);

        if (result.IsFailure)
        {
            ShowError(result.Error.Message);
            return;
        }

        Show(result.Value);

        // Issued is final: the prices on the paper the customer is
        // holding are the offer.
        scanBar.Enabled = false;
        btnFind.Enabled = false;
        btnRemove.Enabled = false;
        btnIssue.Enabled = false;

        ShowInfo(string.Format(CultureInfo.CurrentCulture,
            "Quotation {0} issued to {1} for {2}, valid until {3:dd/MM/yyyy}."
            + "\n\nPrint it for the customer, or export it to send. It can "
            + "be converted to a sale at the till (F10).",
            result.Value.QuotationNumber, result.Value.CustomerName,
            Format.Money(result.Value.Total), result.Value.ValidUntil));
    });

    private void Show(QuotationDto quotation)
    {
        _current = quotation;

        lblNumber.Text = $"{quotation.QuotationNumber}  ({quotation.Status})";
        grdLines.DataSource = quotation.Lines.ToList();
        lblTotalValue.Text = Format.Money(quotation.Total);

        lblValid.Text = string.Format(CultureInfo.CurrentCulture,
            "Quoted {0:dd/MM/yyyy}, valid until {1:dd/MM/yyyy}. "
            + "No stock is reserved by a quotation.",
            quotation.QuotedOn, quotation.ValidUntil);

        var open = string.Equals(quotation.Status, "Open",
                                 StringComparison.Ordinal);

        scanBar.Enabled = open;
        btnFind.Enabled = open;
        btnRemove.Enabled = open && quotation.Lines.Count > 0;
        btnIssue.Enabled = open && quotation.Lines.Count > 0;

        // Printed and exported once it is the offer, not while it is
        // still being put together.
        btnPrint.Enabled = !open && quotation.Lines.Count > 0;
        btnExport.Enabled = quotation.Lines.Count > 0;
    }

    /// The same guard the presenters use; this screen has no presenter.
    private async Task GuardAsync(Func<Task> work)
    {
        try
        {
            SetBusy(true);
            await work();
        }
        catch (Domain.Exceptions.DomainException ex)
        {
            ShowError(ex.Message);
        }
        catch (Application.Security.PermissionDeniedException ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }
}
