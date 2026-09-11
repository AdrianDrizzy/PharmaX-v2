using System.Globalization;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Application.Security;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Features.Inventory;
using PharmaX.Desktop.Features.Login;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Sales;

public partial class frmPOS : PhxFormBase, IPosView
{
    private readonly PosPresenter? _presenter;
    private readonly IAuthenticationService? _auth;
    private readonly ViewFactory? _views;

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.SaleCreate;

    /// Designer only. Never called at runtime.
    public frmPOS()
    {
        InitializeComponent();

        scanBar.CodeEntered += (_, code) => CodeScanned?.Invoke(this, code);
        KeyDown += OnFunctionKey;
    }

    /// Runtime. Resolved by the container.
    public frmPOS(PosPresenter presenter, IAuthenticationService auth,
                  ViewFactory views)
        : this()
    {
        _presenter = presenter;
        _auth = auth;
        _views = views;
        _presenter.Attach(this);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_presenter is not null) await _presenter.InitialiseAsync();
    }

    // Escape voids the current line here, so the screen must not also
    // treat it as "back out".
    protected override bool ClosesOnEscape => false;

    /// <summary>
    /// One handler for the whole function bar. The keys themselves live
    /// in PosKeyMap so that the labels on the buttons and the keys that
    /// fire them cannot drift apart.
    /// </summary>
    private void OnFunctionKey(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case PosKeyMap.Search:
                ProductSearchRequested?.Invoke(this, EventArgs.Empty); break;
            case PosKeyMap.Quantity:
                QuantityRequested?.Invoke(this, EventArgs.Empty); break;
            case PosKeyMap.LineDiscount:
                LineDiscountRequested?.Invoke(this, EventArgs.Empty); break;
            case PosKeyMap.Payment:
                PaymentRequested?.Invoke(this, EventArgs.Empty); break;
            case PosKeyMap.Hold:
                HoldRequested?.Invoke(this, EventArgs.Empty); break;
            case PosKeyMap.Close:
                CloseRequested?.Invoke(this, EventArgs.Empty); break;
            case PosKeyMap.VoidLine:
                VoidLineRequested?.Invoke(this, EventArgs.Empty); break;
            default:
                return;
        }

        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    // ---- IPosView --------------------------------------------------------

    public void ShowLines(IReadOnlyList<SaleLineDto> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var selected = SelectedLineNo;

        grdLines.Rows.Clear();

        foreach (var l in lines)
        {
            var index = grdLines.Rows.Add(l.LineNo, l.Description,
                l.BatchNumber ?? "", l.Quantity, l.UnitPrice, l.Discount,
                l.TaxCode, l.LineTotal);

            if (l.IsVoided)
            {
                // Struck through rather than removed: the operator needs
                // to see that the line was voided, and so does the audit.
                var row = grdLines.Rows[index];
                row.DefaultCellStyle.ForeColor = SystemColors.GrayText;
                row.DefaultCellStyle.Font =
                    new Font(grdLines.Font, FontStyle.Strikeout);
            }
        }

        Reselect(selected);
    }

    private void Reselect(int? lineNo)
    {
        if (lineNo is null) return;

        foreach (DataGridViewRow row in grdLines.Rows)
        {
            if (row.Cells["LineNo"].Value is int value && value == lineNo)
            {
                row.Selected = true;
                grdLines.CurrentCell = row.Cells["Description"];
                return;
            }
        }
    }

    public void ShowTotals(SaleTotalsDto totals)
    {
        ArgumentNullException.ThrowIfNull(totals);

        lblSubtotalValue.Text = Format.Amount(totals.Subtotal);
        lblDiscountValue.Text = Format.Amount(totals.Discount);
        lblVatValue.Text = Format.Amount(totals.VatTotal);
        lblTenderedValue.Text = Format.Amount(totals.Tendered);
        lblChangeValue.Text = Format.Amount(totals.Change);

        // The total carries the symbol: it is the figure the customer
        // is about to be asked for.
        lblTotalValue.Text = Format.Money(totals.Total);
    }

    public void ShowCustomer(string name, string accountInfo,
                             bool ratedPricing)
    {
        lblCustomer.Text = string.IsNullOrWhiteSpace(name)
            ? "Cash sale"
            : name;

        lblAccount.Text = accountInfo;
        lblRated.Visible = ratedPricing;
    }

    public void ShowDocumentNumber(string documentNumber) =>
        lblLastDocument.Text = $"Last: {documentNumber}";

    public void ClearSale()
    {
        grdLines.Rows.Clear();
        ShowCustomer("", "", ratedPricing: false);
        scanBar.Reset();
    }

    public void FocusScan() => scanBar.TakeFocus();

    public int? SelectedLineNo =>
        grdLines.CurrentRow?.Cells["LineNo"].Value as int?;

    // ---- Prompts ---------------------------------------------------------

    public decimal? PromptQuantity(decimal current) =>
        frmNumberPrompt.Ask(this, "Quantity", "Quantity for this line",
                            current, decimals: 3);

    public decimal? PromptDiscount(decimal lineTotal) =>
        frmNumberPrompt.Ask(this, "Discount",
            $"How much off {Format.Money(lineTotal)}?", 0m, decimals: 2);

    public Guid? PromptSupervisor(string reason)
    {
        if (_auth is null) return null;

        using var dialog = new frmSupervisorPrompt(_auth, reason);
        return dialog.ShowDialog(this) == DialogResult.OK
            ? dialog.AuthoriserId
            : null;
    }

    public IReadOnlyList<PaymentDto>? PromptPayment(decimal amountDue)
    {
        using var dialog = new frmPayment(amountDue);
        return dialog.ShowDialog(this) == DialogResult.OK
            ? dialog.Payments
            : null;
    }

    public string? PromptVoidReason() =>
        frmTextPrompt.Ask(this, "Void line",
                          "Why is this line being voided?");

    /// <summary>
    /// The product list, in picker mode, rather than a lookup grid built
    /// a second time here: it already searches on code and description,
    /// pages, and shows price and stock on hand, which is exactly what
    /// the operator needs to see before selling something.
    /// </summary>
    public string? PromptProductSearch(string? term)
    {
        if (_views is null) return null;

        using var picker = _views.Create<frmProductList>();
        picker.UseAsPicker(term);

        return picker.ShowDialog(this) == DialogResult.OK
            ? picker.SelectedCode
            : null;
    }

    // ---- Events ----------------------------------------------------------

    public event EventHandler<string>? CodeScanned;
    public event EventHandler? ProductSearchRequested;
    public event EventHandler? QuantityRequested;
    public event EventHandler? LineDiscountRequested;
    public event EventHandler? VoidLineRequested;
    public event EventHandler? PaymentRequested;
    public event EventHandler? HoldRequested;
    public event EventHandler? CloseRequested;
}
