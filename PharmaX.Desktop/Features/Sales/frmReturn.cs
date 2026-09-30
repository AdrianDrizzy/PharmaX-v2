using System.Globalization;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Application.Security;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Features.Login;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Sales;

public partial class frmReturn : PhxFormBase, IReturnView
{
    private readonly ReturnPresenter? _presenter;
    private readonly IAuthenticationService? _auth;

    private ReturnableSaleDto? _sale;

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.ReturnAuthorise;

    /// Designer only. Never called at runtime.
    public frmReturn()
    {
        InitializeComponent();

        btnFind.Click += (_, _) => FindRequested?.Invoke(this, EventArgs.Empty);
        btnPost.Click += (_, _) => PostRequested?.Invoke(this, EventArgs.Empty);
        btnClose.Click += (_, _) =>
            CloseRequested?.Invoke(this, EventArgs.Empty);

        txtDocumentNumber.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;

            FindRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            e.SuppressKeyPress = true;
        };

        grdLines.CellEndEdit += (_, _) =>
            LinesChanged?.Invoke(this, EventArgs.Empty);

        grdLines.CellValidating += OnReturningValidating;

        // Leaving, or looking up another sale, is never held up by a
        // figure on this one.
        btnClose.CausesValidation = false;
        btnFind.CausesValidation = false;
        txtDocumentNumber.CausesValidation = false;

        // A credit note credits value without goods coming back, so
        // nothing on one can be restocked. The grid says so rather than
        // letting the operator tick a box the service will ignore.
        cboKind.SelectedIndexChanged += (_, _) => ApplyKindToGrid();
    }

    /// Runtime. Resolved by the container.
    public frmReturn(ReturnPresenter presenter, IAuthenticationService auth)
        : this()
    {
        _presenter = presenter;
        _auth = auth;
        _presenter.Attach(this);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        cboKind.Items.AddRange(new object[] { "GoodsReturn", "CreditNote" });
        cboKind.SelectedIndex = 0;

        if (_presenter is not null) await _presenter.InitialiseAsync();

        txtDocumentNumber.Focus();
    }

    public string DocumentNumber => txtDocumentNumber.Text.Trim();

    public string Kind => cboKind.SelectedItem?.ToString() ?? "GoodsReturn";

    public void ShowSale(ReturnableSaleDto sale)
    {
        ArgumentNullException.ThrowIfNull(sale);

        _sale = sale;
        var culture = CultureInfo.CurrentCulture;

        lblSaleValue.Text = string.Format(Format.Culture,
            "{0}   {1}   total {2}   paid by {3}",
            sale.DocumentNumber,
            Format.DateTimeShort(sale.OccurredAtUtc.ToLocalTime()),
            Format.Money(sale.Total),
            string.Join(", ", sale.TendersUsed));

        grdLines.Rows.Clear();

        foreach (var line in sale.Lines)
        {
            var index = grdLines.Rows.Add(line.LineNo, line.Description,
                line.QuantitySold, line.AlreadyReturned, line.Returnable,
                0m, line.UnitPrice, line.Returnable > 0m, "");

            var row = grdLines.Rows[index];

            // Nothing returnable means nothing to do on this row, so it
            // reads as unavailable rather than as an editable line.
            if (line.Returnable <= 0m)
            {
                row.ReadOnly = true;
                row.DefaultCellStyle.ForeColor = SystemColors.GrayText;
            }
            else if (line.IsExpired)
            {
                // Expired stock can be credited but never restocked, so
                // the tick is off and cannot be turned on.
                row.Cells["Restock"].Value = false;
                row.Cells["Restock"].ReadOnly = true;
                PhxTheme.Flag(row, PhxTheme.Problem);
                row.Cells["Reason"].Value = "Expired - credit only";
            }
        }

        ApplyKindToGrid();
    }

    private void ApplyKindToGrid()
    {
        var creditNote = string.Equals(Kind, "CreditNote",
                                       StringComparison.Ordinal);

        foreach (DataGridViewRow row in grdLines.Rows)
        {
            if (row.IsNewRow) continue;

            if (creditNote)
            {
                row.Cells["Restock"].Value = false;
                row.Cells["Restock"].ReadOnly = true;
            }
            else if (_sale?.Lines.FirstOrDefault(l =>
                         row.Cells["LineNo"].Value is int n
                         && l.LineNo == n) is ReturnableLineDto line)
            {
                row.Cells["Restock"].ReadOnly =
                    line.IsExpired || line.Returnable <= 0m;
            }
        }

        lblKindHint.Text = creditNote
            ? "A credit note credits value only. Nothing goes back on the "
              + "shelf."
            : "Goods returned. Tick the lines fit to sell again.";
    }

    public void ShowTotals(decimal credit, decimal refunded)
    {
        lblCreditValue.Text = Format.Money(credit);
        btnPost.Enabled = credit > 0m && _sale is not null;
    }

    public void ClearSale()
    {
        _sale = null;
        grdLines.Rows.Clear();
        lblSaleValue.Text = "(no sale found yet)";
        txtDocumentNumber.Clear();
        txtDocumentNumber.Focus();
    }

    public IReadOnlyList<ReturnLineEditDto> CollectLines()
    {
        var lines = new List<ReturnLineEditDto>();

        foreach (DataGridViewRow row in grdLines.Rows)
        {
            if (row.IsNewRow) continue;
            if (row.Cells["LineNo"].Value is not int lineNo) continue;

            var quantity = ToDecimal(row.Cells["Return"].Value);
            if (quantity <= 0m) continue;

            var reason = row.Cells["Reason"].Value?.ToString()?.Trim();

            lines.Add(new ReturnLineEditDto(
                OriginalLineNo: lineNo,
                Quantity: quantity,
                RestockToShelf: row.Cells["Restock"].Value is true,
                LineReason: string.IsNullOrWhiteSpace(reason)
                    ? null
                    : reason));
        }

        return lines;
    }

    public string? PromptReason() =>
        frmTextPrompt.Ask(this, "Reason for return",
            "Why is this coming back? Without a reason the pattern of "
            + "returns tells a manager nothing.");

    public Guid? PromptSupervisor(string reason)
    {
        if (_auth is null) return null;

        using var dialog = new frmSupervisorPrompt(_auth, reason);

        return dialog.ShowDialog(this) == DialogResult.OK
            ? dialog.AuthoriserId
            : null;
    }

    public IReadOnlyList<PaymentDto>? PromptRefund(decimal amountDue,
        IReadOnlyList<string> tendersUsed)
    {
        using var dialog = new frmPayment(amountDue);

        dialog.UseAsRefund(tendersUsed);

        return dialog.ShowDialog(this) == DialogResult.OK
            ? dialog.Payments
            : null;
    }

    /// <summary>
    /// Holds the Returning figure to what can come back, as it is typed.
    /// </summary>
    /// <remarks>
    /// Four against three sold was taken, priced, a reason and a refund
    /// collected, and only then refused (ADM-OBS-10). The cell now says
    /// what the limit is and stays open until it is met. The service
    /// still checks, because a screen is not the only way in.
    /// </remarks>
    private void OnReturningValidating(object? sender,
                                       DataGridViewCellValidatingEventArgs e)
    {
        if (e.RowIndex < 0
            || grdLines.Columns[e.ColumnIndex].Name != "Return") return;

        var row = grdLines.Rows[e.RowIndex];
        var typed = e.FormattedValue?.ToString()?.Trim() ?? "";

        row.ErrorText = ReturningProblem(typed,
            ToDecimal(row.Cells["Returnable"].Value)) ?? "";

        e.Cancel = row.ErrorText.Length > 0;
    }

    /// Why a Returning figure cannot stand, or null where it can.
    internal static string? ReturningProblem(string typed, decimal returnable)
    {
        if (typed.Length == 0) return null;

        if (!decimal.TryParse(typed, NumberStyles.Number,
                CultureInfo.CurrentCulture, out var quantity))
            return "Type how many are coming back.";

        if (quantity < 0m) return "A return cannot be less than none.";

        return quantity > returnable
            ? string.Format(CultureInfo.CurrentCulture,
                "Only {0:0.###} can come back on this line.", returnable)
            : null;
    }

    private static decimal ToDecimal(object? value) =>
        value switch
        {
            decimal d => d,
            null => 0m,
            _ => decimal.TryParse(value.ToString(), NumberStyles.Number,
                    CultureInfo.CurrentCulture, out var parsed)
                ? parsed
                : 0m
        };

    public event EventHandler? FindRequested;
    public event EventHandler? LinesChanged;
    public event EventHandler? PostRequested;
    public event EventHandler? CloseRequested;
}
