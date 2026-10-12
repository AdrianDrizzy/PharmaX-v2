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
            if (grdItems.Columns[e.ColumnIndex].Name == "ProductCode")
                await DescribeAsync(grdItems.Rows[e.RowIndex]);
        };
        btnRemoveItem.Click += (_, _) =>
        {
            if (grdItems.CurrentRow is { IsNewRow: false } row)
                grdItems.Rows.Remove(row);
        };

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

        // The prescriber from the list RSI's doctors came across into:
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

            if (await Suggest.FindProductAsync(scopes, typed) is { } product)
                AddProduct(product);
            else
                ShowError($"No product with the code {typed}. Type a few "
                          + "words of its name to choose it from a list.");
        };

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
        Describe(row, product);

        txtFindProduct.Clear();

        grdItems.CurrentCell = row.Cells["Quantity"];
        grdItems.Focus();
        grdItems.BeginEdit(true);
    }

    /// One line of the script, its cells set by name.
    private DataGridViewRow AddRow(string code, decimal quantity,
                                   string directions, bool allowSubstitution)
    {
        var row = grdItems.Rows[grdItems.Rows.Add()];

        row.Cells["ProductCode"].Value = code;
        row.Cells["Quantity"].Value = quantity;
        row.Cells["Directions"].Value = directions;
        row.Cells["AllowSubstitution"].Value = allowSubstitution;

        return row;
    }

    private static void Describe(DataGridViewRow row, ProductListItemDto? product)
    {
        row.Cells["Product"].Value = product?.Description
            ?? (string.IsNullOrWhiteSpace(row.Cells["ProductCode"].Value?.ToString())
                ? ""
                : "No product with this code");

        row.Cells["Details"].Value = product is null
            ? ""
            : Suggest.ProductDetails(product, withCode: false);

        row.Cells["Product"].Style.ForeColor = product is null
            ? PhxTheme.ProblemText
            : PhxTheme.Ink;
    }

    /// <summary>
    /// The name and details of whatever code a line holds: one typed into
    /// the grid, or one brought from a quote or an earlier capture.
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

        var product = await Suggest.FindProductAsync(_scopes, code);

        // Typed over again while the answer was coming.
        if (row.DataGridView is null
            || !string.Equals(row.Cells["ProductCode"].Value?.ToString()?.Trim(),
                              code, StringComparison.Ordinal))
            return;

        Describe(row, product);
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
                   item.AllowSubstitution);
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
            items.Add(new ScriptItemEditDto(
                ProductCode: code,
                Quantity: ToDecimal(row.Cells["Quantity"].Value),
                Directions: row.Cells["Directions"].Value?.ToString()?.Trim()
                            ?? "",
                AllowSubstitution:
                    row.Cells["AllowSubstitution"].Value is not bool allow
                    || allow));
        }

        return items;
    }

    private static decimal ToDecimal(object? value) =>
        value switch
        {
            decimal d => d,
            null => 0m,
            _ => decimal.TryParse(value.ToString(),
                    System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.CurrentCulture,
                    out var parsed) ? parsed : 0m
        };

    public event EventHandler? SaveRequested;
    public event EventHandler? CancelRequested;
}
