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

        btnStart.Click += (_, _) => _ = StartAsync();
        btnFind.Click += (_, _) => _ = FindAsync();
        btnRemove.Click += (_, _) => _ = RemoveAsync();
        btnIssue.Click += (_, _) => _ = IssueAsync();
        btnClose.Click += (_, _) => CloseView(true);

        scanBar.CodeEntered += (_, code) => _ = AddAsync(code);

        ShowNoQuotation();
    }

    /// Runtime. Resolved by the container.
    public frmQuotation(IQuotationService quotations, ViewFactory views)
        : this()
    {
        _quotations = quotations;
        _views = views;
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
    }

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
        if (picker.SelectedCode is not string code) return;

        var result = await _quotations.AddLineAsync(_current.Id, code, 1m);

        if (result.IsFailure)
        {
            ShowError(result.Error.Message);
            return;
        }

        Show(result.Value);
        scanBar.TakeFocus();
    });

    private Task RemoveAsync() => GuardAsync(async () =>
    {
        if (_quotations is null || _current is null) return;

        if (grdLines.CurrentRow?.DataBoundItem is not QuotationLineDto line)
            return;

        var result = await _quotations.RemoveLineAsync(_current.Id,
                                                       line.LineNo);

        if (result.IsFailure)
        {
            ShowError(result.Error.Message);
            return;
        }

        Show(result.Value);
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
            "Anything to add to the quotation? (optional)");

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
            "Quotation {0} issued to {1} for {2}, valid until {3:dd/MM/yyyy}.",
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
        finally
        {
            SetBusy(false);
        }
    }
}
