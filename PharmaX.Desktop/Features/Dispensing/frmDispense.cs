using System.Globalization;
using PharmaX.Application.Dispensing.Dtos;
using PharmaX.Application.Security;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Features.Login;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Dispensing;

/// <summary>
/// Structurally the same as frmPOS, with one addition that changes the
/// flow: clinical warnings must be acknowledged, and a scheduled item
/// requires a pharmacist to authenticate at the moment of issue
/// (DSP-006).
/// </summary>
public partial class frmDispense : PhxFormBase, IDispenseView
{
    private readonly DispensePresenter? _presenter;
    private readonly IAuthenticationService? _auth;
    private readonly ViewFactory? _views;

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.ScriptAuthorise;

    /// Designer only. Never called at runtime.
    public frmDispense()
    {
        InitializeComponent();

        scanBar.CodeEntered += (_, code) => ProductScanned?.Invoke(this, code);
        btnFindScript.Click += (_, _) =>
            ScriptRequested?.Invoke(this, EventArgs.Empty);
        btnIssue.Click += (_, _) =>
            IssueRequested?.Invoke(this, EventArgs.Empty);
        btnReprint.Click += (_, _) =>
            LabelReprintRequested?.Invoke(this, EventArgs.Empty);
        btnClose.Click += (_, _) =>
            CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// Runtime. Resolved by the container.
    public frmDispense(DispensePresenter presenter,
                       IAuthenticationService auth, ViewFactory views)
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
        SeatSplitter();
        if (_presenter is not null) await _presenter.InitialiseAsync();
    }

    public void ShowPatient(PatientSummaryDto patient)
    {
        ArgumentNullException.ThrowIfNull(patient);

        lblPatient.Text = string.Format(CultureInfo.CurrentCulture,
            "{0}   file {1}{2}", patient.FullName, patient.FileNumber,
            patient.Age is int age ? $"   age {age}" : "");

        // Allergies are the first thing a pharmacist looks for, so they
        // go on the header rather than behind a tab.
        lblAllergies.Text = patient.Allergies.Count == 0
            ? "No recorded allergies"
            : "Allergies: " + string.Join(", ", patient.Allergies);

        lblAllergies.ForeColor = patient.Allergies.Count == 0
            ? SystemColors.ControlText
            : Color.FromArgb(168, 32, 32);
    }

    public void ShowScript(ScriptDto script)
    {
        ArgumentNullException.ThrowIfNull(script);

        lblScript.Text = string.Format(CultureInfo.CurrentCulture,
            "Script {0}   {1}   prescribed {2:dd/MM/yyyy}   "
            + "{3} repeat(s) left",
            script.ScriptNumber, script.PrescriberName,
            script.PrescribedOn, script.RepeatsRemaining);
    }

    public void ShowItems(IReadOnlyList<DispensingLineDto> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        grdItems.Rows.Clear();

        foreach (var i in items)
            grdItems.Rows.Add(i.LineNo, i.Description, i.Quantity,
                i.Directions, i.BatchNumber ?? "", i.Tariff,
                i.Contribution, i.CoPayment);
    }

    public void ShowWarnings(IReadOnlyList<ClinicalWarningDto> warnings)
    {
        ArgumentNullException.ThrowIfNull(warnings);

        lstWarnings.Items.Clear();

        foreach (var w in warnings)
            lstWarnings.Items.Add(string.Format(CultureInfo.CurrentCulture,
                "{0}{1}  {2}",
                w.IsAcknowledged ? "[done] " : "",
                w.Severity.ToUpperInvariant(), w.Message));

        lstWarnings.BackColor = warnings.Any(w => w.IsHardStop
                                                  && !w.IsAcknowledged)
            ? Color.FromArgb(255, 226, 226)
            : SystemColors.Window;
    }

    public void ShowCoPayment(decimal tariff, decimal contribution,
                              decimal coPayment)
    {
        lblTariffValue.Text = Format.Amount(tariff);
        lblContributionValue.Text = Format.Amount(contribution);

        // What the patient actually pays carries the symbol.
        lblCoPaymentValue.Text = Format.Money(coPayment);
    }

    // ---- Prompts ---------------------------------------------------------

    public Guid? PromptPatientSearch()
    {
        if (_views is null) return null;

        // The patient list doubles as the picker, so a pharmacist who
        // cannot find the patient can add them without leaving the flow.
        using var picker = _views.Create<Patients.frmPatientList>();
        picker.UseAsPicker();

        return picker.ShowDialog(this) == DialogResult.OK
            ? picker.SelectedId
            : null;
    }

    public Guid? PromptScriptSearch(Guid patientId)
    {
        if (_views is null) return null;

        // Likewise the script list: capturing the script the patient has
        // just handed over is the normal case, not the exception.
        using var picker = _views.Create<frmScriptList>();
        picker.LoadForPatient(patientId, asPicker: true);

        return picker.ShowDialog(this) == DialogResult.OK
            ? picker.SelectedScriptId
            : null;
    }

    public DirectionsDto? PromptDirections(Guid productId)
    {
        var quantity = frmNumberPrompt.Ask(this, "Quantity",
            "Quantity to dispense", 1m, decimals: 3);

        if (quantity is null) return null;

        // Mandatory: a label without directions cannot be dispensed
        // (DSP-004), so a blank answer cancels the item.
        var directions = frmTextPrompt.Ask(this, "Directions",
            "Directions as they will print on the label");

        return string.IsNullOrWhiteSpace(directions)
            ? null
            : new DirectionsDto(quantity.Value, directions);
    }

    public Guid? PromptPharmacistAuthorisation(string reasonShown)
    {
        if (_auth is null) return null;

        using var dialog = new frmSupervisorPrompt(_auth, reasonShown,
                                                   "Pharmacist");

        return dialog.ShowDialog(this) == DialogResult.OK
            ? dialog.AuthoriserId
            : null;
    }

    public string? PromptWarningOverride(ClinicalWarningDto warning)
    {
        ArgumentNullException.ThrowIfNull(warning);

        return frmTextPrompt.Ask(this, "Clinical warning",
            $"{warning.Message}\r\n\r\nWhy is this being overridden?");
    }

    public event EventHandler? PatientRequested;
    public event EventHandler? ScriptRequested;
    public event EventHandler<string>? ProductScanned;
    public event EventHandler? IssueRequested;
    public event EventHandler? LabelReprintRequested;
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
        splMain.SplitterDistance = Math.Min(340,
            Math.Max(60, (splMain.Orientation == Orientation.Vertical
                ? splMain.Width : splMain.Height) - 120));
}
