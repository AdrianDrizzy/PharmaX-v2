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
                  ViewFactory views,
                  Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopes)
        : this()
    {
        _presenter = presenter;
        _auth = auth;
        _views = views;

        // Typing a name offers the matching products under the scan bar,
        // so an item is rung up from its first few letters.
        scanBar.SuggestFrom(Suggest.Products(scopes, overTheCounter: true));
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

    /// The till's own map (PosKeyMap), which cashiers already know.
    protected override bool AssignsFunctionKeys => false;

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
            case PosKeyMap.Customer:
                CustomerRequested?.Invoke(this, EventArgs.Empty); break;
            case PosKeyMap.Quantity:
                QuantityRequested?.Invoke(this, EventArgs.Empty); break;
            case PosKeyMap.LineDiscount:
                LineDiscountRequested?.Invoke(this, EventArgs.Empty); break;
            case PosKeyMap.Payment:
                PaymentRequested?.Invoke(this, EventArgs.Empty); break;
            case PosKeyMap.Hold:
                HoldRequested?.Invoke(this, EventArgs.Empty); break;
            case PosKeyMap.Recall:
                RecallRequested?.Invoke(this, EventArgs.Empty); break;
            case PosKeyMap.Quotation:
                QuotationRequested?.Invoke(this, EventArgs.Empty); break;
            case PosKeyMap.Reprint:
                ReprintRequested?.Invoke(this, EventArgs.Empty); break;
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

    public void ShowRatedPricing(bool rated) => lblRated.Visible = rated;

    public void ShowDocumentNumber(string documentNumber) =>
        lblLastDocument.Text = $"Last: {documentNumber}";

    public void ShowChange(decimal change)
    {
        if (change <= 0m) return;

        // Beside the last document number, in the place the cashier looks
        // while counting the change out, and left there until the next
        // sale is paid for.
        lblLastDocument.Text += $"   -   Change {Format.Money(change)}";
    }

    public Guid? PromptHeldSale(IReadOnlyList<HeldSaleDto> held)
    {
        ArgumentNullException.ThrowIfNull(held);

        using var picker = new frmHeldSales(held);
        return picker.ShowDialog(this) == DialogResult.OK
            ? picker.Chosen
            : null;
    }

    public string? PromptQuotationNumber() =>
        frmTextPrompt.Ask(this, "Quotation",
            "Quotation number (on the customer's copy)");

    public string? PromptReceiptNumber(string? last) =>
        frmTextPrompt.Ask(this, "Reprint receipt",
            last is null
                ? "Receipt number to print again (Inv RefNo on the receipt)"
                : "Receipt number to print again - Enter for the last one",
            last);

    public void ClearSale()
    {
        grdLines.Rows.Clear();
        scanBar.Reset();

        // A collection already knows whose script it is, and ClearSale
        // runs after CollectFor has said so. Resetting the customer here
        // put "Cash sale" over the patient's name on every collection.
        if (_collecting) return;

        ShowCustomer("", "", ratedPricing: false);
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

        // Whether the co-payment rests on a tariff is the lines' to say,
        // once they arrive. It used to say RATED PRICING for every script
        // collected, a private patient's at the shelf price included.
        lblCustomer.Text = patientName;
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

    public Application.Patients.AccountDto? PromptAccount()
    {
        if (_views is null) return null;

        using var picker = _views.Create<Features.Patients.frmAccountPicker>();

        return picker.ShowDialog(this) == DialogResult.OK
            ? picker.Chosen
            : null;
    }

    public decimal? PromptQuantity(decimal current) =>
        frmNumberPrompt.Ask(this, "Quantity", "Quantity for this line",
                            current, decimals: 3);

    public decimal? PromptDiscount(decimal lineTotal) =>
        frmNumberPrompt.AskAmountOrPercent(this, "Discount",
            $"How much off {Format.Money(lineTotal)}? An amount, or 10%",
            lineTotal);

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
    public event EventHandler? CustomerRequested;
    public event EventHandler? QuantityRequested;
    public event EventHandler? LineDiscountRequested;
    public event EventHandler? VoidLineRequested;
    public event EventHandler? PaymentRequested;
    public event EventHandler? HoldRequested;
    public event EventHandler? RecallRequested;
    public event EventHandler? QuotationRequested;
    public event EventHandler? ReprintRequested;
    public event EventHandler? CloseRequested;
}
