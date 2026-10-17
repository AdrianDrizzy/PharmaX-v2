using Microsoft.Extensions.DependencyInjection;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Inventory.Dtos;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Dispensing;

/// <summary>
/// Captures a script as the prescriber wrote it. Deliberately separate
/// from dispensing: what was prescribed and what was handed over are two
/// different records, and a generic substitution changes only the second.
/// </summary>
public partial class frmScriptEdit : PhxFormBase, IScriptEditView
{
    private readonly ScriptEditPresenter? _presenter;
    private readonly IServiceScopeFactory? _scopes;
    private readonly SearchSuggestions? _suggestions;

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.ScriptCapture;

    /// Designer only. Never called at runtime.
    public frmScriptEdit()
    {
        InitializeComponent();

        btnSave.Click += (_, _) => SaveRequested?.Invoke(this, EventArgs.Empty);
        btnCancel.Click += (_, _) =>
            CancelRequested?.Invoke(this, EventArgs.Empty);

        // Add starts a line and goes to the product box to fill it.
        btnAddItem.Click += (_, _) =>
        {
            AddRow("", 1m, "", true);
            txtFindProduct.Focus();
        };

        grdItems.CellEndEdit += async (_, e) =>
        {
            var row = grdItems.Rows[e.RowIndex];

            switch (grdItems.Columns[e.ColumnIndex].Name)
            {
                case "ProductCode":
                    await DescribeAsync(row);
                    break;
                case "Quantity" or "PackUnits" or "PackPrice":
                    ShowCharge(row);
                    break;
            }
        };

        // A number where a number goes, said at once rather than at Save.
        grdItems.CellValidating += (_, e) =>
        {
            var column = grdItems.Columns[e.ColumnIndex].Name;
            if (column is not ("Quantity" or "PackUnits" or "PackPrice")) return;

            var text = e.FormattedValue?.ToString()?.Trim() ?? "";
            var problem = text.Length == 0
                ? null
                : !decimal.TryParse(text, System.Globalization.NumberStyles.Number,
                        System.Globalization.CultureInfo.CurrentCulture, out var value)
                    ? "Type a number."
                    : column != "PackPrice" && value <= 0m
                        ? (column == "Quantity" ? "The quantity" : "The pack")
                          + " must be more than nothing."
                        : value < 0m
                            ? "A price cannot be less than nothing."
                            : null;

            grdItems.Rows[e.RowIndex].ErrorText = problem ?? "";
            e.Cancel = problem is not null;
        };
        btnRemoveItem.Click += (_, _) =>
        {
            if (grdItems.CurrentRow is { IsNewRow: false } row)
                grdItems.Rows.Remove(row);
        };
        btnFromHistory.Click += (_, _) =>
            HistoryRequested?.Invoke(this, EventArgs.Empty);

        chkHasValidUntil.CheckedChanged += (_, _) =>
            dtpValidUntil.Enabled = chkHasValidUntil.Checked;
    }

    /// Runtime. Resolved by the container.
    private SearchSuggestions? _prescribers;

    /// "Practice 40525 - 0292..." gives 40525.
    private static string? PracticeNumber(string? detail)
    {
        const string marker = "Practice ";
        if (detail is null || !detail.StartsWith(marker, StringComparison.Ordinal)) return null;

        var number = detail[marker.Length..].Split(' ')[0];
        return number.Length > 30 ? number[..30] : number;
    }

    public frmScriptEdit(ScriptEditPresenter presenter,
                         IServiceScopeFactory scopes) : this()
    {
        _presenter = presenter;
        _scopes = scopes;

        // A few words of the name are enough: the matches come up under
        // the box with their code, pack, price, stock and schedule, Down
        // and Enter take one, and it goes onto the script. Codes nobody
        // remembers were the only way in, so a pharmacist taking down a
        // script looked each one up on another screen first.
        _suggestions = new SearchSuggestions(txtFindProduct,
            Suggest.ProductsInFull(scopes))
        {
            Wanted = Suggest.IsName
        };

        _suggestions.Chosen += (_, chosen) =>
        {
            if (chosen.Value is ProductListItemDto product) AddProduct(product);
        };

        // The prescriber from the list the imported doctors came into:
        // a few letters of the name or the practice number, and both
        // boxes are filled. Typing a name not on the list still works.
        _prescribers = new SearchSuggestions(txtPrescriber, Suggest.Prescribers(scopes));
        _prescribers.Chosen += (_, chosen) =>
        {
            if (chosen.Value is not Domain.Reference.Prescriber prescriber) return;

            txtPrescriber.Text = prescriber.Name;
            txtPrescriberNumber.Text = PracticeNumber(prescriber.Detail) ?? "";
        };

        // After the suggestions, which take Enter first when one is
        // highlighted: otherwise Enter puts on the code that was typed.
        txtFindProduct.PreviewKeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && txtFindProduct.Text.Trim().Length > 0)
                e.IsInputKey = true;
        };

        txtFindProduct.KeyDown += async (_, e) =>
        {
            if (e.Handled || e.KeyCode != Keys.Enter) return;

            var typed = txtFindProduct.Text.Trim();
            if (typed.Length == 0) return;

            e.Handled = true;
            e.SuppressKeyPress = true;

            var (product, matches) = await Suggest.ResolveProductAsync(scopes, typed);

            if (product is not null)
                AddProduct(product);
            else
                ShowError(matches > 1
                    ? $"\"{typed}\" fits {matches} products. Type more of the "
                      + "name, or choose one from the list under the box."
                    : $"No product has the code or name \"{typed}\".");
        };

        // The line's own code cell takes a name too, a few words at a time.
        CellSuggestions.Attach(grdItems, "ProductCode", Suggest.Products(scopes));

        _presenter.Attach(this);
    }

    /// <summary>
    /// Puts a product on the script: into the empty line the screen opens
    /// with, or onto a new one, with its name and details beside it, and
    /// the cursor on its quantity.
    /// </summary>
    private void AddProduct(ProductListItemDto product)
    {
        var row = grdItems.Rows.Cast<DataGridViewRow>()
            .FirstOrDefault(r => string.IsNullOrWhiteSpace(
                r.Cells["ProductCode"].Value?.ToString()))
            ?? AddRow("", 1m, "", true);

        row.Cells["ProductCode"].Value = product.Code;
        Describe(row, product, resetPrice: true);

        txtFindProduct.Clear();

        grdItems.CurrentCell = row.Cells["Quantity"];
        grdItems.Focus();
        grdItems.BeginEdit(true);
    }

    /// <summary>
    /// One line of the script, its cells set by name. A pack and price
    /// left null are filled with the product's own once it is known.
    /// </summary>
    private DataGridViewRow AddRow(string code, decimal quantity,
                                   string directions, bool allowSubstitution,
                                   decimal? packUnits = null,
                                   decimal? packPrice = null)
    {
        var row = grdItems.Rows[grdItems.Rows.Add()];

        row.Cells["ProductCode"].Value = code;
        row.Cells["Quantity"].Value = quantity;
        row.Cells["PackUnits"].Value = packUnits;
        row.Cells["PackPrice"].Value = packPrice;
        row.Cells["Directions"].Value = directions;
        row.Cells["AllowSubstitution"].Value = allowSubstitution;

        return row;
    }

    /// <param name="resetPrice">
    /// A different product on the line: its own pack and price replace
    /// whatever the line had, which was set for the product before.
    /// </param>
    private static void Describe(DataGridViewRow row, ProductListItemDto? product,
                                 int matches = 0, bool resetPrice = false)
    {
        row.Cells["Product"].Value = product?.Description
            ?? (string.IsNullOrWhiteSpace(row.Cells["ProductCode"].Value?.ToString())
                ? ""
                : matches > 1
                    ? $"{matches} products fit - type more of the name"
                    : "No product with this code or name");

        // The price has its own column now, so the details are the pack
        // as shelved and the stock.
        row.Cells["Details"].Value = product is null
            ? ""
            : Suggest.Details(product.PackSize,
                  $"{Format.Quantity(product.QuantityOnHand)} on hand",
                  product.Schedule >= 3
                      ? $"schedule {product.Schedule}"
                      : "over the counter");

        row.Cells["Product"].Style.ForeColor = product is null
            ? PhxTheme.ProblemText
            : PhxTheme.Ink;

        if (resetPrice || row.Tag is ProductListItemDto earlier
                          && product is not null && earlier.Id != product.Id)
        {
            row.Cells["PackUnits"].Value = null;
            row.Cells["PackPrice"].Value = null;
        }

        row.Tag = product;

        if (product is not null)
        {
            row.Cells["PackUnits"].Value ??= product.UnitsPerPack is decimal units
                                             && units > 0m ? units : 1m;
            row.Cells["PackPrice"].Value ??= product.RetailPrice;
        }

        ShowCharge(row);
    }

    /// <summary>What the line comes to: price x quantity / pack.</summary>
    private static void ShowCharge(DataGridViewRow row)
    {
        var quantity = ToNumber(row.Cells["Quantity"].Value);
        var pack = ToNumber(row.Cells["PackUnits"].Value);
        var price = ToNumber(row.Cells["PackPrice"].Value);

        row.Cells["Charge"].Value = quantity is decimal q && pack is decimal p
                                    && p > 0m && price is decimal each
            ? Math.Round(each * q / p, 2, MidpointRounding.AwayFromZero)
            : null;
    }

    /// <summary>
    /// The name and details of whatever a line holds: a code typed into the
    /// grid, a few words of a name - put right to the product's code - or a
    /// code brought from a quote or an earlier capture.
    /// </summary>
    private async Task DescribeAsync(DataGridViewRow row)
    {
        if (_scopes is null || row.DataGridView is null) return;

        var code = row.Cells["ProductCode"].Value?.ToString()?.Trim() ?? "";

        if (code.Length == 0)
        {
            Describe(row, null);
            return;
        }

        var (product, matches) = await Suggest.ResolveProductAsync(_scopes, code);

        // Typed over again while the answer was coming.
        if (row.DataGridView is null
            || !string.Equals(row.Cells["ProductCode"].Value?.ToString()?.Trim(),
                              code, StringComparison.Ordinal))
            return;

        if (product is not null) row.Cells["ProductCode"].Value = product.Code;
        Describe(row, product, matches);
    }

    public Guid PatientId { get; private set; }

    public Guid? ScriptId { get; private set; }

    public void LoadForPatient(Guid patientId) => PatientId = patientId;

    /// The dispensary quote the script is being captured from, if any.
    private Guid? _quotationId;

    /// <summary>
    /// The script captured, once it is saved: read by the quote that
    /// opened this screen, to hand on to dispensing.
    /// </summary>
    public Guid? CapturedScriptId { get; private set; }

    public void ScriptCaptured(Guid scriptId) => CapturedScriptId = scriptId;

    /// <summary>
    /// Captures the script a dispensary quote became: its items and
    /// quantities already on, so only the prescriber and the directions
    /// are left to take down. Called before ShowDialog.
    /// </summary>
    public void LoadFromQuote(Guid patientId,
        Application.Sales.QuotationDto quote)
    {
        ArgumentNullException.ThrowIfNull(quote);

        PatientId = patientId;
        _quotationId = quote.Id;
        Text = $"PharmaX - Capture script from quote {quote.QuotationNumber}";

        grdItems.Rows.Clear();

        foreach (var line in quote.Lines)
            AddRow(line.ProductCode ?? "", line.Quantity, "", true);
    }

    /// <summary>
    /// A new script drafted from an earlier one: its prescriber and items,
    /// dated today, to change before it is captured. Called before
    /// ShowDialog.
    /// </summary>
    public void LoadDraft(Guid patientId, ScriptEditDto draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        PatientId = patientId;
        Text = "PharmaX - New script from an earlier one";

        txtPrescriber.Text = draft.PrescriberName;
        txtPrescriberNumber.Text = draft.PrescriberNumber ?? "";
        dtpPrescribedOn.Value = draft.PrescribedOn.ToDateTime(TimeOnly.MinValue);
        chkHasValidUntil.Checked = false;
        numRepeats.Value = Math.Min(numRepeats.Maximum,
            Math.Max(numRepeats.Minimum, draft.RepeatsAuthorised));

        grdItems.Rows.Clear();

        foreach (var item in draft.Items)
            AddRow(item.ProductCode, item.Quantity, item.Directions,
                   item.AllowSubstitution, item.PackUnits, item.PackPrice);
    }

    public IReadOnlyList<SuppliedItemDto> PickFromHistory(
        IReadOnlyList<SuppliedItemDto> supplied)
    {
        ArgumentNullException.ThrowIfNull(supplied);

        return frmChoicePrompt.AskMany(this, "Items from history",
            "Pick what goes on this script (Ctrl+click for more than one).",
            supplied.Select(s => (
                $"{s.On:dd MMM yyyy}   {s.Description}   x{s.Quantity:0.###}   "
                + $"{s.Directions}   (script {s.ScriptNumber})", s)).ToList());
    }

    public async void AddItems(IReadOnlyList<ScriptItemEditDto> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        // Into the empty line the screen opens with first.
        foreach (DataGridViewRow blank in grdItems.Rows.Cast<DataGridViewRow>()
                     .Where(r => !r.IsNewRow && string.IsNullOrWhiteSpace(
                         r.Cells["ProductCode"].Value?.ToString()))
                     .ToList())
            grdItems.Rows.Remove(blank);

        var added = items.Select(item => AddRow(item.ProductCode, item.Quantity,
            item.Directions, item.AllowSubstitution, item.PackUnits,
            item.PackPrice)).ToList();

        foreach (var row in added) await DescribeAsync(row);
    }

    /// <summary>
    /// Opens a captured script for correcting. Called before ShowDialog.
    /// </summary>
    public void LoadScript(Guid patientId, Guid scriptId)
    {
        PatientId = patientId;
        ScriptId = scriptId;
        Text = "PharmaX - Correct script";
    }

    public void ShowScript(ScriptDetailDto script)
    {
        ArgumentNullException.ThrowIfNull(script);

        Text = $"PharmaX - Correct script {script.Script.ScriptNumber}";

        txtPrescriber.Text = script.Script.PrescriberName;
        txtPrescriberNumber.Text = script.PrescriberNumber ?? "";
        dtpPrescribedOn.Value = script.Script.PrescribedOn
            .ToDateTime(TimeOnly.MinValue);

        chkHasValidUntil.Checked = script.Script.ValidUntil is not null;

        if (script.Script.ValidUntil is DateOnly until)
            dtpValidUntil.Value = until.ToDateTime(TimeOnly.MinValue);

        numRepeats.Value = Math.Min(numRepeats.Maximum,
            Math.Max(numRepeats.Minimum, script.Script.RepeatsAuthorised));

        grdItems.Rows.Clear();

        foreach (var item in script.Items)
            AddRow(item.ProductCode ?? "", item.Quantity, item.Directions,
                   item.AllowSubstitution, item.PackUnits, item.PackPrice);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_presenter is not null) await _presenter.InitialiseAsync();

        // One blank line to start, so the common case of a one-item
        // script needs no button press.
        if (grdItems.Rows.Count == 0) AddRow("", 1m, "", true);

        // Items brought from a quote or an earlier capture, described.
        foreach (DataGridViewRow row in grdItems.Rows)
            await DescribeAsync(row);
    }

    public void ShowPatient(string description) =>
        lblPatient.Text = description;

    public ScriptEditDto Collect() => new(
        PatientId: PatientId,
        PrescriberName: txtPrescriber.Text.Trim(),
        PrescriberNumber: string.IsNullOrWhiteSpace(txtPrescriberNumber.Text)
            ? null
            : txtPrescriberNumber.Text.Trim(),
        PrescribedOn: DateOnly.FromDateTime(dtpPrescribedOn.Value.Date),
        ValidUntil: chkHasValidUntil.Checked
            ? DateOnly.FromDateTime(dtpValidUntil.Value.Date)
            : null,
        RepeatsAuthorised: (int)numRepeats.Value,
        Items: CollectItems(),
        QuotationId: _quotationId);

    private IReadOnlyList<ScriptItemEditDto> CollectItems()
    {
        var items = new List<ScriptItemEditDto>();

        foreach (DataGridViewRow row in grdItems.Rows)
        {
            if (row.IsNewRow) continue;

            var code = row.Cells["ProductCode"].Value?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(code)) continue;

            // Left to the service to refuse: a blank quantity or blank
            // directions is a message the operator needs to see, not a
            // row the screen silently drops.
            // A pack and price the same as the product's are left to the
            // service to recognise as no change.
            items.Add(new ScriptItemEditDto(
                ProductCode: code,
                Quantity: ToDecimal(row.Cells["Quantity"].Value),
                Directions: row.Cells["Directions"].Value?.ToString()?.Trim()
                            ?? "",
                AllowSubstitution:
                    row.Cells["AllowSubstitution"].Value is not bool allow
                    || allow,
                PackUnits: ToNumber(row.Cells["PackUnits"].Value),
                PackPrice: ToNumber(row.Cells["PackPrice"].Value)));
        }

        return items;
    }

    private static decimal ToDecimal(object? value) => ToNumber(value) ?? 0m;

    /// A cell's number, or null where it is blank or not a number.
    private static decimal? ToNumber(object? value) =>
        value switch
        {
            decimal d => d,
            null => null,
            _ => decimal.TryParse(value.ToString(),
                    System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.CurrentCulture,
                    out var parsed) ? parsed : null
        };

    public event EventHandler? SaveRequested;
    public event EventHandler? CancelRequested;
    public event EventHandler? HistoryRequested;
}
