using PharmaX.Application.Dispensing;
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

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.ScriptCapture;

    /// Designer only. Never called at runtime.
    public frmScriptEdit()
    {
        InitializeComponent();

        btnSave.Click += (_, _) => SaveRequested?.Invoke(this, EventArgs.Empty);
        btnCancel.Click += (_, _) =>
            CancelRequested?.Invoke(this, EventArgs.Empty);

        btnAddItem.Click += (_, _) => grdItems.Rows.Add("", 1m, "", true);
        btnRemoveItem.Click += (_, _) =>
        {
            if (grdItems.CurrentRow is { IsNewRow: false } row)
                grdItems.Rows.Remove(row);
        };

        chkHasValidUntil.CheckedChanged += (_, _) =>
            dtpValidUntil.Enabled = chkHasValidUntil.Checked;
    }

    /// Runtime. Resolved by the container.
    public frmScriptEdit(ScriptEditPresenter presenter) : this()
    {
        _presenter = presenter;
        _presenter.Attach(this);
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
            grdItems.Rows.Add(line.ProductCode ?? "", line.Quantity, "", true);
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
            grdItems.Rows.Add(item.ProductCode ?? "", item.Quantity,
                              item.Directions, item.AllowSubstitution);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_presenter is not null) await _presenter.InitialiseAsync();

        // One blank line to start, so the common case of a one-item
        // script needs no button press.
        if (grdItems.Rows.Count == 0) grdItems.Rows.Add("", 1m, "", true);
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
