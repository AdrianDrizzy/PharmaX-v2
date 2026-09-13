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
        if (!Raise(e.KeyCode)) return;

        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    /// <summary>
    /// Raises whatever a function key stands for, and says whether it
    /// stood for anything. Shared by the keyboard and by the buttons on
    /// the bar, so pressing F6 and pressing Pay cannot come to mean two
    /// different things.
    /// </summary>
    private bool Raise(Keys key)
    {
        // Everything but paying and closing is meaningless on a script
        // the pharmacist has already priced, and a key that silently
        // did nothing would be worse than one that says why.
        if (_collecting
            && key is not (PosKeyMap.Payment or PosKeyMap.Close))
        {
            ShowInfo("This is a dispensed script. Take the payment, or "
                     + "send the patient back to the pharmacist to change "
                     + "it.");
            return true;
        }

        switch (key)
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
                return false;
        }

        return true;
    }

    /// <summary>
    /// A button on the function bar was pressed.
    /// </summary>
    /// <remarks>
    /// The caret goes straight back to the scan bar afterwards. The next
    /// thing that happens at a till is almost always a barcode, and a
    /// barcode typed into whichever button still holds focus goes
    /// nowhere at all.
    /// </remarks>
    private void OnFunctionButton(object? sender, EventArgs e)
    {
        if (sender is Control { Tag: Keys key }) Raise(key);

        scanBar.TakeFocus();
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

    public void FocusScan()
    {
        // In collection mode there is nothing to scan, and dropping the
        // caret into a dead box after every refresh would leave the
        // cashier typing into nothing.
        if (_collecting) { FocusPayment(); return; }

        scanBar.TakeFocus();
    }

    private bool _collecting;

    /// <summary>
    /// Opens the till on one dispensed script instead of an empty
    /// basket. Called by the collection queue before ShowDialog.
    /// </summary>
    public void CollectFor(Guid dispensingId, string patientName)
    {
        _presenter?.Collect(dispensingId);
        ShowCollection(patientName);
    }

    private void ShowCollection(string patientName)
    {
        _collecting = true;

        Text = string.IsNullOrWhiteSpace(patientName)
            ? "PharmaX - Collect script"
            : $"PharmaX - Collect script  -  {patientName}";

        // The basket is the script's, and it is not editable here: the
        // items came off the shelf in the dispensary and what is owed
        // was worked out against the medical aid tariff. A cashier who
        // needs to change any of that sends the patient back to the
        // pharmacist, which is the correct answer.
        scanBar.Visible = false;

        foreach (var button in pnlFunctions.Controls.OfType<Button>())
        {
            if (button.Tag is not Keys key) continue;

            button.Enabled = key is PosKeyMap.Payment or PosKeyMap.Close;
        }

        lblCustomer.Text = patientName;
        lblRated.Visible = true;
    }

    public void FocusPayment()
    {
        var pay = pnlFunctions.Controls.OfType<Button>()
            .FirstOrDefault(b => b.Tag is Keys key && key == PosKeyMap.Payment);

        pay?.Focus();
    }

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
