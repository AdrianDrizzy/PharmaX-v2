using System.Globalization;
using PharmaX.Application.Suppliers.Dtos;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Suppliers;

public partial class frmGoodsReceipt : PhxFormBase, IGoodsReceiptView
{
    private readonly GoodsReceiptPresenter? _presenter;
    private readonly ViewFactory? _views;

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.GoodsReceive;

    /// Designer only. Never called at runtime.
    public frmGoodsReceipt()
    {
        InitializeComponent();

        btnChooseSupplier.Click += (_, _) =>
            SupplierRequested?.Invoke(this, EventArgs.Empty);
        btnChooseOrder.Click += (_, _) =>
            OrderRequested?.Invoke(this, EventArgs.Empty);
        btnPost.Click += (_, _) => PostRequested?.Invoke(this, EventArgs.Empty);
        btnClose.Click += (_, _) =>
            CloseRequested?.Invoke(this, EventArgs.Empty);

        btnAddLine.Click += (_, _) => AddCaptureRow();
        btnRemoveLine.Click += (_, _) => RemoveCaptureRow();

        // Previewing on every committed cell is what turns a typed code
        // into a description while the operator is still on the line.
        grdCapture.CellEndEdit += (_, _) =>
            LinesChanged?.Invoke(this, EventArgs.Empty);

        txtInvoiceTotal.Leave += (_, _) =>
            LinesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// Runtime. Resolved by the container.
    public frmGoodsReceipt(GoodsReceiptPresenter presenter,
                           ViewFactory views) : this()
    {
        _presenter = presenter;
        _views = views;
        _presenter.Attach(this);
    }

    public Guid? SupplierId { get; private set; }
    public Guid? PurchaseOrderId { get; private set; }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        SeatSplitter();

        dtpReceivedOn.Value = DateTime.Today;
        AddCaptureRow();

        if (_presenter is not null) await _presenter.InitialiseAsync();
    }

    public GoodsReceiptEditDto Collect() => new(
        SupplierId: SupplierId ?? Guid.Empty,
        PurchaseOrderId: PurchaseOrderId,
        SupplierInvoiceNumber:
            string.IsNullOrWhiteSpace(txtInvoiceNumber.Text)
                ? null
                : txtInvoiceNumber.Text.Trim(),
        ReceivedOn: DateOnly.FromDateTime(dtpReceivedOn.Value.Date),
        InvoiceTotal: txtInvoiceTotal.Amount,
        Lines: CollectLines());

    private IReadOnlyList<GoodsReceiptLineEditDto> CollectLines()
    {
        var lines = new List<GoodsReceiptLineEditDto>();

        foreach (DataGridViewRow row in grdCapture.Rows)
        {
            if (row.IsNewRow) continue;

            var code = row.Cells["Code"].Value?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(code)) continue;

            var batch = row.Cells["Batch"].Value?.ToString()?.Trim();
            var expiryText = row.Cells["Expiry"].Value?.ToString()?.Trim();

            // The order's own product, as long as the operator has not
            // typed a different code over it; then the service checks the
            // new code against the order line instead.
            var orderedCode = row.Tag as string;
            var productId = row.Cells["ProductId"].Value is Guid id
                            && string.Equals(orderedCode, code,
                                             StringComparison.OrdinalIgnoreCase)
                ? id
                : (Guid?)null;

            lines.Add(new GoodsReceiptLineEditDto(
                ProductCode: code,
                Quantity: ToDecimal(row.Cells["Qty"].Value),
                UnitCost: ToDecimal(row.Cells["Cost"].Value),
                BatchNumber: string.IsNullOrWhiteSpace(batch) ? null : batch,
                ExpiryDate: ToExpiry(expiryText),
                PurchaseOrderLineNo:
                    row.Cells["OrderLine"].Value is int lineNo
                        ? lineNo
                        : null,
                ProductId: productId));
        }

        return lines;
    }

    public void ShowSupplier(string description)
    {
        lblSupplierValue.Text = description;

        // The presenter looked the supplier up, so the identifier it
        // resolved is the one to keep.
        SupplierId = _pendingSupplierId;
    }

    private Guid? _pendingSupplierId;

    public void ShowOrder(PurchaseOrderDto? order)
    {
        PurchaseOrderId = order?.Id;

        lblOrderValue.Text = order is null
            ? "(none)"
            : string.Format(CultureInfo.CurrentCulture,
                "{0}  ordered {1:dd/MM/yyyy}  {2} line(s) outstanding",
                order.OrderNumber, order.OrderedOn,
                order.Lines.Count(l => l.Outstanding > 0m));

        if (order is null) return;

        // Pre-fill from what is still outstanding on the order: the
        // delivery is usually what was ordered, and typing it twice is
        // how quantities get mistyped.
        grdCapture.Rows.Clear();

        // The product's own code in the code column and its description
        // beside it. The description used to go in the code column, which
        // no code ever matched, so nothing from an order could be posted
        // until every line was re-keyed (DEF-0013).
        foreach (var line in order.Lines.Where(l => l.Outstanding > 0m))
        {
            var index = grdCapture.Rows.Add(line.ProductCode,
                line.Description, line.Outstanding, line.UnitCost, "", "",
                line.LineNo, line.ProductId);

            grdCapture.Rows[index].Tag = line.ProductCode;
        }

        if (grdCapture.Rows.Count == 0) AddCaptureRow();

        LinesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ShowProblem(string? problem) =>
        lblProblem.Text = problem ?? "";

    public void ShowProducts(IReadOnlyList<GoodsReceiptProductDto?> products)
    {
        ArgumentNullException.ThrowIfNull(products);

        // The capture rows learn what their codes name, in order: the
        // service answers a line per captured line, as Collect sent them.
        var captured = grdCapture.Rows.Cast<DataGridViewRow>()
            .Where(r => !r.IsNewRow
                        && !string.IsNullOrWhiteSpace(
                            r.Cells["Code"].Value?.ToString()))
            .ToList();

        for (var i = 0; i < captured.Count && i < products.Count; i++)
        {
            var row = captured[i];
            var product = products[i];

            // Blank for a code that names nothing, never the product the
            // line used to name.
            row.Cells["Product"].Value = product?.Description ?? "";

            // A code typed on a line of its own brings the product's cost
            // price, once, as it does on a purchase order, and a different
            // code brings its own in place of it. A line from an order keeps
            // the cost it was ordered at, and a cost the operator typed - to
            // nothing, for bonus stock - stays.
            var cost = row.Cells["Cost"];

            if (product is null || row.Tag is not null
                || cost.Tag is BroughtCost same
                   && string.Equals(same.Code, product.Code,
                                    StringComparison.OrdinalIgnoreCase))
                continue;

            var current = ToDecimal(cost.Value);
            var untouched = current == 0m
                            || cost.Tag is BroughtCost { Cost: decimal was }
                               && was == current;

            cost.Tag = new BroughtCost(product.Code,
                untouched ? product.CostPrice : null);

            if (untouched) cost.Value = product.CostPrice;
        }
    }

    /// The cost a line's product brought with it, to tell it from one the
    /// operator typed; null where the cost on the line is the operator's.
    private sealed record BroughtCost(string Code, decimal? Cost);

    public void ShowLines(IReadOnlyList<GoodsReceiptLineDto> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        grdPreview.Rows.Clear();

        foreach (var line in lines)
            grdPreview.Rows.Add(line.LineNo, line.Description, line.Quantity,
                line.UnitCost, line.LineTotal, line.BatchNumber ?? "",
                line.ExpiryDate?.ToString("MM/yyyy",
                    CultureInfo.CurrentCulture) ?? "");

        btnPost.Enabled = lines.Count > 0 && SupplierId is not null;
    }

    public void ShowTotals(decimal captured, decimal? invoiced,
                           decimal? variance)
    {
        lblCapturedValue.Text = Format.Money(captured);

        lblVarianceValue.Text = variance is decimal difference
            ? Format.Money(difference)
            : "";

        // Amber rather than red: the delivery note and the invoice
        // disagreeing is a question, not an error.
        lblVarianceValue.BackColor = variance is decimal d && d != 0m
            ? Color.FromArgb(255, 244, 214)
            : Color.Transparent;
    }

    public void ClearCapture()
    {
        grdCapture.Rows.Clear();
        AddCaptureRow();
        txtInvoiceNumber.Clear();
        txtInvoiceTotal.Clear();
        PurchaseOrderId = null;
        lblOrderValue.Text = "(none)";
    }

    public Guid? PromptSupplier()
    {
        if (_views is null) return null;

        using var picker = _views.Create<frmSupplierList>();
        picker.UseAsPicker();

        if (picker.ShowDialog(this) != DialogResult.OK) return null;

        _pendingSupplierId = picker.SelectedId;
        return _pendingSupplierId;
    }

    public Guid? PromptOrder(Guid supplierId)
    {
        if (_views is null) return null;

        using var picker = _views.Create<frmPurchaseOrderList>();
        picker.LoadForSupplier(supplierId, asPicker: true);

        return picker.ShowDialog(this) == DialogResult.OK
            ? picker.SelectedId
            : null;
    }

    private void AddCaptureRow() =>
        grdCapture.Rows.Add("", "", 1m, 0m, "", "", null, null);

    private void RemoveCaptureRow()
    {
        if (grdCapture.CurrentRow is { IsNewRow: false } row)
        {
            grdCapture.Rows.Remove(row);
            if (grdCapture.Rows.Count == 0) AddCaptureRow();
            LinesChanged?.Invoke(this, EventArgs.Empty);
        }
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

    /// <summary>
    /// Expiry is typed as MM/yyyy, because that is what is printed on a
    /// medicine pack. It resolves to the last day of that month, which
    /// is what the printed date means.
    /// </summary>
    private static DateOnly? ToExpiry(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        if (DateOnly.TryParseExact(text, new[] { "MM/yyyy", "MM/yy" },
                CultureInfo.CurrentCulture, DateTimeStyles.None,
                out var monthOnly))
            return new DateOnly(monthOnly.Year, monthOnly.Month,
                DateTime.DaysInMonth(monthOnly.Year, monthOnly.Month));

        return DateOnly.TryParse(text, CultureInfo.CurrentCulture,
                DateTimeStyles.None, out var full)
            ? full
            : null;
    }

    public event EventHandler? SupplierRequested;
    public event EventHandler? OrderRequested;
    public event EventHandler? LinesChanged;
    public event EventHandler? PostRequested;
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Sets the split the designer asked for, once the window is its
    /// real size.
    /// </summary>
    /// <remarks>
    /// A SplitContainer clamps SplitterDistance to the width it has at
    /// the time, and in a hand-written designer that is the default
    /// 150 pixels. The clamped value then scales up with the window and
    /// lands nowhere near what was asked for - here it left the report
    /// tree filling four fifths of the screen and the parameters
    /// squeezed off the edge.
    /// </remarks>
    private void SeatSplitter() =>
        splMain.SplitterDistance = Math.Min(220,
            Math.Max(60, (splMain.Orientation == Orientation.Vertical
                ? splMain.Width : splMain.Height) - 120));
}
