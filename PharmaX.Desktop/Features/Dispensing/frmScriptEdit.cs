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

    public void LoadForPatient(Guid patientId) => PatientId = patientId;

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
        Items: CollectItems());

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
