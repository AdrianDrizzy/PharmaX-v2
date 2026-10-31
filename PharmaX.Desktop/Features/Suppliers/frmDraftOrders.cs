using System.Globalization;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Reporting;
using PharmaX.Application.Suppliers;
using PharmaX.Application.Suppliers.Dtos;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Suppliers;

/// <summary>
/// The draft purchase orders - the stock assistant's, at sign-in - to look
/// over, export for the supplier, and send.
/// </summary>
/// <remarks>
/// A draft reaches nobody. The buyer changes the quantities here, exports
/// the order to PDF or Excel to email it, and presses Send to supplier,
/// which marks it sent so goods receipt can work against it. Discarding a
/// draft cancels it, with the reason recorded.
/// </remarks>
public sealed class frmDraftOrders : PhxFormBase
{
    private readonly IPurchasingService? _purchasing;
    private readonly IPdfWriter? _pdf;
    private readonly IWorkbookWriter? _workbook;
    private readonly ICurrentUser? _user;

    private readonly ComboBox _orders = new();
    private readonly Label _particulars = new();
    private readonly PhxGrid _lines = new();
    private readonly Label _total = new();
    private readonly Label _note = new();
    private readonly Button _pdfButton = new();
    private readonly Button _excelButton = new();
    private readonly Button _send = new();
    private readonly Button _discard = new();
    private readonly Button _discardSeveral = new();
    private readonly Button _close = new();

    private IReadOnlyList<PurchaseOrderDto> _drafts = Array.Empty<PurchaseOrderDto>();

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.PurchaseOrderRaise;

    /// Designer only. Never called at runtime.
    public frmDraftOrders()
    {
        Name = "frmDraftOrders";
        Text = "PharmaX - Draft purchase orders";

        var header = new Panel { Dock = DockStyle.Top, Height = 76 };

        header.Controls.Add(new Label
        {
            AutoSize = true, Location = new Point(12, 14), Text = "Draft order"
        });

        _orders.DropDownStyle = ComboBoxStyle.DropDownList;
        _orders.Location = new Point(100, 10);
        _orders.Name = "cboOrders";
        _orders.Size = new Size(520, 26);
        _orders.SelectedIndexChanged += (_, _) => ShowOrder();

        _particulars.AutoSize = false;
        _particulars.Location = new Point(12, 44);
        _particulars.Name = "lblParticulars";
        _particulars.Size = new Size(740, 24);
        _particulars.ForeColor = PhxTheme.Muted;

        header.Controls.AddRange(new Control[] { _orders, _particulars });

        _lines.Dock = DockStyle.Fill;
        _lines.Name = "grdLines";
        _lines.ReadOnly = false;
        _lines.AllowUserToAddRows = false;
        _lines.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _lines.AddColumn("Line", "#", 40);
        _lines.AddColumn("Code", "Code", 100);
        _lines.AddColumn("Product", "Product", 300);
        _lines.AddColumn("Qty", "Qty", 70, "0.###", true);
        _lines.AddColumn("Cost", "Unit cost", 80, "N2", true);
        _lines.AddColumn("Total", "Total", 90, "N2", true);

        foreach (DataGridViewColumn column in _lines.Columns)
            column.ReadOnly = column.Name != "Qty";

        _lines.CellEndEdit += (_, _) => Retotal();

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 110 };

        _total.AutoSize = false;
        _total.Font = new Font("Consolas", 12F, FontStyle.Bold);
        _total.Location = new Point(520, 6);
        _total.Name = "lblTotal";
        _total.Size = new Size(240, 26);
        _total.TextAlign = ContentAlignment.MiddleRight;

        _note.AutoSize = false;
        _note.Location = new Point(12, 6);
        _note.Name = "lblNote";
        _note.Size = new Size(500, 40);
        _note.Text = "Nothing has been sent. Change a quantity (0 takes the line off), "
                     + "export the order for the supplier, then send it.";

        _pdfButton.Location = new Point(12, 64);
        _pdfButton.Name = "btnPdf";
        _pdfButton.Size = new Size(110, 32);
        _pdfButton.Text = "Export &PDF";

        _excelButton.Location = new Point(128, 64);
        _excelButton.Name = "btnExcel";
        _excelButton.Size = new Size(110, 32);
        _excelButton.Text = "Export &Excel";

        _send.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        _send.Location = new Point(250, 64);
        _send.Name = "btnSend";
        _send.Size = new Size(150, 32);
        _send.Text = "&Send to supplier";

        _discard.Location = new Point(406, 64);
        _discard.Name = "btnDiscard";
        _discard.Size = new Size(110, 32);
        _discard.Text = "&Discard draft";

        // The drafts are picked one at a time above, so several are
        // chosen in a list of their own.
        _discardSeveral.Location = new Point(522, 64);
        _discardSeveral.Name = "btnDiscardSeveral";
        _discardSeveral.Size = new Size(130, 32);
        _discardSeveral.Text = "Discard se&veral...";

        _close.DialogResult = DialogResult.Cancel;
        _close.Location = new Point(660, 64);
        _close.Name = "btnClose";
        _close.Size = new Size(100, 32);
        _close.Text = "Close";

        footer.Controls.AddRange(new Control[]
            { _total, _note, _pdfButton, _excelButton, _send, _discard, _discardSeveral, _close });

        Controls.Add(_lines);
        Controls.Add(header);
        Controls.Add(footer);

        CancelButton = _close;
        ClientSize = new Size(780, 560);
        StartPosition = FormStartPosition.CenterParent;

        _pdfButton.Click += (_, _) => Export(pdf: true);
        _excelButton.Click += (_, _) => Export(pdf: false);
        _send.Click += (_, _) => Operations.Enqueue(SendAsync);
        _discard.Click += (_, _) => Operations.Enqueue(DiscardAsync);
        _discardSeveral.Click += (_, _) => Operations.Enqueue(DiscardSeveralAsync);

        // Opened beside the shell rather than as a dialog, where a button's
        // DialogResult alone does not close the window.
        _close.Click += (_, _) => Close();
    }

    /// Runtime. Resolved by the container.
    public frmDraftOrders(IPurchasingService purchasing, IPdfWriter pdf,
        IWorkbookWriter workbook, ICurrentUser user) : this()
    {
        _purchasing = purchasing;
        _pdf = pdf;
        _workbook = workbook;
        _user = user;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Operations.Enqueue(() => LoadDraftsAsync(null));
    }

    private PurchaseOrderDto? Current =>
        _orders.SelectedIndex >= 0 && _orders.SelectedIndex < _drafts.Count
            ? _drafts[_orders.SelectedIndex]
            : null;

    private async Task LoadDraftsAsync(Guid? select)
    {
        if (_purchasing is null) return;

        var drafts = await _purchasing.DraftsAsync();

        if (drafts.IsFailure)
        {
            ShowError(drafts.Error.Message);
            return;
        }

        _drafts = drafts.Value;
        _orders.Items.Clear();

        foreach (var order in _drafts)
            _orders.Items.Add(Describe(order));

        var at = _drafts.ToList().FindIndex(o => o.Id == select);
        _orders.SelectedIndex = _drafts.Count == 0 ? -1 : Math.Max(at, 0);

        if (_drafts.Count == 0) ShowOrder();
    }

    private void ShowOrder()
    {
        _lines.Rows.Clear();

        var order = Current;
        var any = order is not null;

        _pdfButton.Enabled = _excelButton.Enabled = _send.Enabled = _discard.Enabled = _discardSeveral.Enabled = any;

        if (order is null)
        {
            _particulars.Text = "No draft orders are waiting.";
            _total.Text = "";
            return;
        }

        _particulars.Text = string.Format(CultureInfo.CurrentCulture,
            "To {0}   |   drafted {1:dd/MM/yyyy}   |   not yet sent",
            order.SupplierName, order.OrderedOn);

        foreach (var line in order.Lines)
            _lines.Rows.Add(line.LineNo, line.ProductCode, line.Description,
                line.QuantityOrdered, line.UnitCost, line.LineTotal);

        Retotal();
    }

    private void Retotal()
    {
        var total = 0m;

        foreach (DataGridViewRow row in _lines.Rows)
        {
            var line = Quantity(row) * ToDecimal(row.Cells["Cost"].Value);
            row.Cells["Total"].Value = line;
            total += line;
        }

        _total.Text = Format.Money(total);
    }

    /// The order as it stands on screen, quantities as edited.
    private PurchaseOrderDto? OnScreen()
    {
        _lines.EndEdit();

        if (Current is not { } order) return null;

        var lines = _lines.Rows.Cast<DataGridViewRow>()
            .Select(row => (Row: row, Qty: Quantity(row)))
            .Where(x => x.Qty > 0m)
            .Select(x => order.Lines.Single(l => l.LineNo == (int)x.Row.Cells["Line"].Value!) with
            {
                QuantityOrdered = x.Qty,
                Outstanding = x.Qty,
                LineTotal = x.Qty * ToDecimal(x.Row.Cells["Cost"].Value)
            })
            .ToList();

        return order with { Lines = lines, Total = lines.Sum(l => l.LineTotal) };
    }

    private void Export(bool pdf)
    {
        if (OnScreen() is not { } order || _pdf is null || _workbook is null) return;

        using var save = new SaveFileDialog
        {
            DefaultExt = pdf ? "pdf" : "xlsx",
            FileName = $"Purchase order {order.OrderNumber} {order.SupplierName}",
            Filter = pdf ? "PDF document (*.pdf)|*.pdf" : "Excel workbook (*.xlsx)|*.xlsx",
            OverwritePrompt = true,
            Title = "Export the purchase order",
            InitialDirectory = GridExport.Downloads
        };

        if (save.ShowDialog(this) != DialogResult.OK) return;

        var report = PurchaseOrderReport.Build(order, _user?.Username ?? "", DateTime.Now);

        try
        {
            if (pdf) _pdf.WriteTo(report, save.FileName);
            else _workbook.WriteTo(report, save.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError($"{Path.GetFileName(save.FileName)} could not be saved: {ex.Message}"
                      + "\n\nClose it if it is open, or choose another name.");
            return;
        }

        ShowSaved(save.FileName);
    }

    private async Task SendAsync()
    {
        if (_purchasing is null || OnScreen() is not { } order) return;

        if (order.Lines.Count == 0)
        {
            ShowError("Every line is at nought. Discard the draft instead.");
            return;
        }

        if (!Confirm(string.Format(CultureInfo.CurrentCulture,
                "Mark order {0} as sent to {1}, {2} item(s) for {3}?\n\n"
                + "PharmaX does not email it: send the exported PDF or Excel "
                + "to the supplier yourself.",
                order.OrderNumber, order.SupplierName, order.Lines.Count,
                Format.Money(order.Total))))
            return;

        var quantities = _lines.Rows.Cast<DataGridViewRow>()
            .ToDictionary(row => (int)row.Cells["Line"].Value!, Quantity);

        var sent = await _purchasing.SendDraftAsync(order.Id, quantities);

        if (sent.IsFailure)
        {
            ShowError(sent.Error.Message);
            return;
        }

        ShowInfo($"Order {sent.Value.OrderNumber} is marked as sent to "
                 + $"{sent.Value.SupplierName}. It is now on the open orders, "
                 + "for goods receipt.");

        await LoadDraftsAsync(null);
    }

    private Task DiscardAsync() =>
        Current is { } order ? DiscardAsync(new[] { order }) : Task.CompletedTask;

    private Task DiscardSeveralAsync()
    {
        if (Current is not { } current) return Task.CompletedTask;

        // The one on screen first, so it is already picked.
        var choices = _drafts.OrderBy(o => o.Id != current.Id)
            .Select(o => (Describe(o), o))
            .ToList();

        var picked = frmChoicePrompt.AskMany(this, "Discard draft orders",
            "The draft orders to discard (Ctrl or Shift for several):", choices);

        return DiscardAsync(picked);
    }

    /// Each through the service's own cancel, so its checks still apply;
    /// one refused leaves the rest discarded.
    private async Task DiscardAsync(IReadOnlyList<PurchaseOrderDto> orders)
    {
        if (_purchasing is null || orders.Count == 0) return;

        if (!Confirm(orders.Count == 1
                ? $"Discard draft order {orders[0].OrderNumber} to {orders[0].SupplierName}?"
                : $"Discard {orders.Count} draft orders?"))
            return;

        var failed = new List<string>();

        foreach (var order in orders)
        {
            var cancelled = await _purchasing.CancelAsync(order.Id,
                $"Draft discarded by {_user?.Username}");

            if (cancelled.IsFailure)
                failed.Add(orders.Count == 1
                    ? cancelled.Error.Message
                    : $"{order.OrderNumber}: {cancelled.Error.Message}");
        }

        if (failed.Count > 0) ShowError(string.Join("\n", failed));
        if (failed.Count == orders.Count) return;

        await LoadDraftsAsync(null);
    }

    private static string Describe(PurchaseOrderDto order) =>
        string.Format(CultureInfo.CurrentCulture, "{0} - {1} ({2} item(s))",
            order.OrderNumber, order.SupplierName, order.Lines.Count);

    private static decimal Quantity(DataGridViewRow row) =>
        Math.Max(0m, ToDecimal(row.Cells["Qty"].Value));

    private static decimal ToDecimal(object? value) =>
        value switch
        {
            decimal d => d,
            int i => i,
            null => 0m,
            _ => decimal.TryParse(value.ToString(), NumberStyles.Number,
                    CultureInfo.CurrentCulture, out var parsed)
                ? parsed
                : 0m
        };
}
