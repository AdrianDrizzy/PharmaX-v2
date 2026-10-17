using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Inventory;
using PharmaX.Application.Sales;
using PharmaX.Application.Suppliers;
using PharmaX.Application.Suppliers.Dtos;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Features.Inventory;
using PharmaX.Desktop.Features.Sales;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Suppliers;

/// <summary>
/// Raises a purchase order: a supplier, the lines, and a number.
/// </summary>
/// <remarks>
/// There was no screen for this at all. "What to order" worked the
/// suggestion out and then left it on the screen to read, and
/// PurchasingService.RaiseAsync was called only by the demonstration
/// seeder and the tests (DEF-0012). The suggestion now fills this form,
/// every line stays editable, and the order is raised, numbered and sent in
/// one act.
/// </remarks>
public sealed class frmPurchaseOrderEdit : PhxFormBase
{
    private readonly IPurchasingService? _purchasing;
    private readonly ISupplierService? _suppliers;
    private readonly IProductService? _products;
    private readonly IDocumentPrinter? _printer;
    private readonly ReceiptHeaderFactory? _headers;
    private readonly ViewFactory? _views;
    private readonly IServiceScopeFactory? _scopes;

    /// The supplier, typed a few letters at a time or chosen from the list.
    private readonly TextBox _supplier = new();
    private readonly SearchSuggestions _supplierSuggestions;

    // Set while the box is filled in for a supplier already chosen, so
    // writing its name there is not taken for typing a new one.
    private bool _showingSupplier;
    private readonly Button _chooseSupplier = new();
    private readonly DateTimePicker _expected = new();
    private readonly PhxGrid _lines = new();
    private readonly Label _total = new();
    private readonly Label _problem = new();
    private readonly Button _add = new();
    private readonly Button _remove = new();
    private readonly Button _suggest = new();
    private readonly Button _raise = new();
    private readonly Button _cancel = new();

    private Guid? _supplierId;

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.PurchaseOrderRaise;

    /// Designer only. Never called at runtime.
    public frmPurchaseOrderEdit()
    {
        Name = "frmPurchaseOrderEdit";
        Text = "PharmaX - Raise purchase order";

        var header = new Panel { Dock = DockStyle.Top, Height = 84 };

        header.Controls.Add(new Label
        {
            AutoSize = true, Location = new Point(12, 14), Text = "Supplier"
        });

        _supplier.Location = new Point(100, 10);
        _supplier.Name = "txtSupplier";
        _supplier.PlaceholderText = "Type a few letters of the name or code";
        _supplier.Size = new Size(380, 26);

        _supplierSuggestions = new SearchSuggestions(_supplier) { MinimumLength = 1 };
        _supplierSuggestions.Chosen += (_, chosen) =>
        {
            if (chosen.Value is Guid id) Operations.Enqueue(() => UseSupplierAsync(id));
        };

        // Typing over a supplier already chosen is choosing another.
        _supplier.TextChanged += (_, _) =>
        {
            if (!_showingSupplier) _supplierId = null;
        };

        // Enter on a name nobody picked from the list: the one supplier it
        // matches, or the list to choose from.
        _supplier.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter || _supplierSuggestions.HasHighlight) return;

            e.Handled = e.SuppressKeyPress = true;
            if (_supplierId is null) Operations.Enqueue(ResolveSupplierAsync);
        };

        _chooseSupplier.Location = new Point(490, 8);
        _chooseSupplier.Name = "btnChooseSupplier";
        _chooseSupplier.Size = new Size(110, 28);
        _chooseSupplier.Text = "&Supplier...";

        header.Controls.Add(new Label
        {
            AutoSize = true, Location = new Point(12, 50), Text = "Expected"
        });

        _expected.Format = DateTimePickerFormat.Short;
        _expected.Location = new Point(100, 46);
        _expected.Name = "dtpExpected";
        _expected.ShowCheckBox = true;
        _expected.Checked = false;
        _expected.Size = new Size(160, 26);

        header.Controls.AddRange(new Control[]
            { _supplier, _chooseSupplier, _expected });

        _lines.Dock = DockStyle.Fill;
        _lines.Name = "grdLines";
        _lines.ReadOnly = false;
        _lines.AllowUserToAddRows = false;
        _lines.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _lines.AddColumn("Code", "Code or name", 110);
        _lines.AddColumn("Product", "Product", 200);
        _lines.AddColumn("Qty", "Qty", 60, "0.###", true);
        _lines.AddColumn("Cost", "Unit cost", 70, "N2", true);
        _lines.AddColumn("Total", "Total", 80, "N2", true);
        _lines.Columns["Product"]!.ReadOnly = true;
        _lines.Columns["Total"]!.ReadOnly = true;
        _lines.CellEndEdit += (_, e) => OnCellEdited(e.RowIndex, e.ColumnIndex);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 96 };

        _add.Location = new Point(12, 8);
        _add.Name = "btnAddLine";
        _add.Size = new Size(100, 28);
        _add.Text = "A&dd line";

        _remove.Location = new Point(118, 8);
        _remove.Name = "btnRemoveLine";
        _remove.Size = new Size(100, 28);
        _remove.Text = "Re&move line";

        _suggest.Location = new Point(224, 8);
        _suggest.Name = "btnSuggest";
        _suggest.Size = new Size(130, 28);
        _suggest.Text = "&What to order";

        _total.AutoSize = false;
        _total.Font = new Font("Consolas", 12F, FontStyle.Bold);
        _total.Location = new Point(440, 8);
        _total.Name = "lblTotal";
        _total.Size = new Size(220, 26);
        _total.TextAlign = ContentAlignment.MiddleRight;

        _problem.AutoSize = false;
        _problem.ForeColor = PhxTheme.ProblemText;
        _problem.Location = new Point(12, 40);
        _problem.Name = "lblProblem";
        _problem.Size = new Size(640, 20);

        _raise.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        _raise.Location = new Point(12, 62);
        _raise.Name = "btnRaise";
        _raise.Size = new Size(150, 30);
        _raise.Text = "&Raise order";

        _cancel.DialogResult = DialogResult.Cancel;
        _cancel.Location = new Point(560, 62);
        _cancel.Name = "btnCancel";
        _cancel.Size = new Size(100, 30);
        _cancel.Text = "Cancel";

        footer.Controls.AddRange(new Control[]
            { _add, _remove, _suggest, _total, _problem, _raise, _cancel });

        Controls.Add(_lines);
        Controls.Add(header);
        Controls.Add(footer);

        CancelButton = _cancel;
        ClientSize = new Size(680, 520);
        StartPosition = FormStartPosition.CenterParent;

        _chooseSupplier.Click += (_, _) => ChooseSupplier();
        _add.Click += (_, _) => AddRow("", "", 1m, 0m);
        _remove.Click += (_, _) => RemoveRow();
        _suggest.Click += (_, _) => Operations.Enqueue(SuggestAsync);
        _raise.Click += (_, _) => Operations.Enqueue(RaiseAsync);
    }

    /// Runtime. Resolved by the container.
    public frmPurchaseOrderEdit(IPurchasingService purchasing,
        ISupplierService suppliers, IProductService products,
        IDocumentPrinter printer, ReceiptHeaderFactory headers,
        ViewFactory views, IServiceScopeFactory scopes) : this()
    {
        _purchasing = purchasing;
        _suppliers = suppliers;
        _products = products;
        _printer = printer;
        _headers = headers;
        _views = views;
        _scopes = scopes;

        // Looked up in scopes of their own, as the scan bars' are.
        _supplierSuggestions.Source = Suggest.Suppliers(scopes);
        CellSuggestions.Attach(_lines, "Code", Suggest.Products(scopes));
    }

    /// The order raised, once there is one.
    public PurchaseOrderDto? Raised { get; private set; }

    /// <summary>
    /// Starts the form from the reorder suggestion, every line editable.
    /// </summary>
    public void Prefill(IReadOnlyList<PurchaseOrderLineDto> suggestion)
    {
        ArgumentNullException.ThrowIfNull(suggestion);

        _lines.Rows.Clear();

        foreach (var line in suggestion)
            AddRow(line.ProductCode, line.Description, line.QuantityOrdered,
                   line.UnitCost);

        Retotal();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_lines.Rows.Count == 0) AddRow("", "", 1m, 0m);
        Retotal();
    }

    private void AddRow(string code, string description, decimal quantity,
                        decimal cost)
    {
        _lines.Rows.Add(code, description, quantity, cost, quantity * cost);
        Retotal();
    }

    private void RemoveRow()
    {
        if (_lines.CurrentRow is not { } row) return;

        _lines.Rows.Remove(row);
        Retotal();
    }

    private void ChooseSupplier()
    {
        if (_views is null) return;

        using var picker = _views.Create<frmSupplierList>();
        picker.UseAsPicker();

        if (picker.ShowDialog(this) != DialogResult.OK
            || picker.SelectedId is not Guid id)
            return;

        Operations.Enqueue(() => UseSupplierAsync(id));
    }

    private async Task UseSupplierAsync(Guid id)
    {
        if (_suppliers is null) return;

        var supplier = await _suppliers.GetAsync(id);

        if (supplier.IsFailure)
        {
            ShowError(supplier.Error.Message);
            return;
        }

        _showingSupplier = true;

        try
        {
            _supplier.Text = $"{supplier.Value.Name}  ({supplier.Value.Code})";
        }
        finally
        {
            _showingSupplier = false;
        }

        _supplierId = id;
        _supplierSuggestions.Hide();
        _problem.Text = "";

        // The supplier's own lead time, until somebody picks a date.
        if (!_expected.Checked)
            _expected.Value = DateTime.Today.AddDays(
                supplier.Value.LeadTimeDays);
    }

    /// <summary>
    /// A supplier typed and not picked: the one it matches, or the list
    /// to choose from when it matches none or several.
    /// </summary>
    private async Task ResolveSupplierAsync()
    {
        if (_scopes is null || string.IsNullOrWhiteSpace(_supplier.Text)) return;

        var matches = await Suggest.Suppliers(_scopes)(_supplier.Text.Trim(),
                                                        CancellationToken.None);

        if (matches.Count == 1 && matches[0].Value is Guid id)
            await UseSupplierAsync(id);
        else
            ChooseSupplier();
    }

    /// <summary>
    /// A typed code - or a few words of the name - is looked up at once,
    /// so the operator sees the product and its cost price while still on
    /// the line.
    /// </summary>
    private void OnCellEdited(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0) return;

        var row = _lines.Rows[rowIndex];

        if (_lines.Columns[columnIndex].Name == "Code")
        {
            var code = row.Cells["Code"].Value?.ToString()?.Trim() ?? "";
            Operations.Enqueue(() => LookUpAsync(row, code));
            return;
        }

        Retotal();
    }

    private async Task LookUpAsync(DataGridViewRow row, string code)
    {
        if (_products is null || _scopes is null || string.IsNullOrWhiteSpace(code))
            return;

        var (match, matches) = await Suggest.ResolveProductAsync(_scopes, code);

        // A name that fits several: the list, opened on those words.
        if (match is null && matches > 1 && _views is not null
            && frmProductList.Pick(_views, this, code) is string picked)
            (match, _) = await Suggest.ResolveProductAsync(_scopes, picked);

        if (match is null)
        {
            row.Cells["Product"].Value = "";
            _problem.Text = matches > 1
                ? $"\"{code}\" fits {matches} products. Type more of the name, or pick one."
                : $"No product has the code or name \"{code}\".";
            return;
        }

        _problem.Text = "";
        row.Cells["Code"].Value = match.Code;
        row.Cells["Product"].Value = match.Description;

        var detail = await _products.GetAsync(match.Id);
        if (detail.IsSuccess
            && ToDecimal(row.Cells["Cost"].Value) == 0m)
            row.Cells["Cost"].Value = detail.Value.CostPrice;

        Retotal();
    }

    private async Task SuggestAsync()
    {
        if (_purchasing is null) return;

        var result = await _purchasing.SuggestAsync();

        if (result.IsFailure)
        {
            ShowError(result.Error.Message);
            return;
        }

        if (result.Value.Count == 0)
        {
            ShowInfo("Nothing is below its reorder level.");
            return;
        }

        Prefill(result.Value);
    }

    private async Task RaiseAsync()
    {
        if (_purchasing is null) return;

        _lines.EndEdit();

        if (_supplierId is not Guid supplierId)
        {
            _problem.Text = "Choose the supplier first.";
            return;
        }

        var lines = Collect();

        if (lines.Count == 0)
        {
            _problem.Text = "Add at least one line.";
            return;
        }

        var result = await _purchasing.RaiseAsync(new PurchaseOrderEditDto(
            supplierId,
            _expected.Checked
                ? DateOnly.FromDateTime(_expected.Value.Date)
                : null,
            lines));

        if (result.IsFailure)
        {
            _problem.Text = result.Error.Message;
            ShowError(result.Error.Message);
            return;
        }

        Raised = result.Value;

        var message = string.Format(CultureInfo.CurrentCulture,
            "Order {0} raised to {1} for {2}, {3} line(s).",
            Raised.OrderNumber, Raised.SupplierName,
            Format.Money(Raised.Total), Raised.Lines.Count);

        if (_printer is not null && _headers is not null
            && Confirm(message + "\n\nPrint it now?"))
        {
            var printed = await _printer.PrintAsync(Document(Raised),
                                                    _headers.Build());

            if (printed.Printed) ShowInfo(printed.Describe(
                $"Order {Raised.OrderNumber}"));
            else ShowError(printed.Describe($"Order {Raised.OrderNumber}"));
        }

        CloseView(true);
    }

    private List<PurchaseOrderLineEditDto> Collect()
    {
        var lines = new List<PurchaseOrderLineEditDto>();

        foreach (DataGridViewRow row in _lines.Rows)
        {
            var code = row.Cells["Code"].Value?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(code)) continue;

            lines.Add(new PurchaseOrderLineEditDto(code,
                ToDecimal(row.Cells["Qty"].Value),
                ToDecimal(row.Cells["Cost"].Value)));
        }

        return lines;
    }

    private void Retotal()
    {
        var total = 0m;

        foreach (DataGridViewRow row in _lines.Rows)
        {
            var line = ToDecimal(row.Cells["Qty"].Value)
                       * ToDecimal(row.Cells["Cost"].Value);
            row.Cells["Total"].Value = line;
            total += line;
        }

        _total.Text = Format.Money(total);
    }

    /// <summary>
    /// The order as it prints, for the supplier's rep or the fax.
    /// </summary>
    internal static PrintableDocument Document(PurchaseOrderDto order)
    {
        var culture = CultureInfo.CurrentCulture;
        var lines = new List<string>
        {
            $"Purchase order {order.OrderNumber}",
            $"To {order.SupplierName}",
            string.Format(culture, "Ordered {0:dd/MM/yyyy}  expected {1:dd/MM/yyyy}",
                order.OrderedOn, order.ExpectedOn),
            new string('-', 42)
        };

        foreach (var line in order.Lines)
        {
            // The whole description, over as many lines as it takes: it is
            // what the supplier reads to know what to send (ADM-OBS-07).
            lines.AddRange(SlipLayout.Described(
                string.Format(culture, "{0,-10} ", line.ProductCode),
                line.Description));
            lines.Add(string.Format(culture, "{0,14:0.###} x {1,10:N2} = {2,12:N2}",
                line.QuantityOrdered, line.UnitCost, line.LineTotal));
        }

        lines.Add(new string('-', 42));
        lines.Add(string.Format(culture, "{0,-26}{1,16:N2}", "TOTAL",
            order.Total));

        return new PrintableDocument($"Purchase order {order.OrderNumber}",
                                     lines);
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
}
