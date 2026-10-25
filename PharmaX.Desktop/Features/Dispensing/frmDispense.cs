using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using PharmaX.Application.Dispensing.Dtos;
using PharmaX.Application.Reference;
using PharmaX.Application.Security;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Features.Login;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Dispensing;

/// <summary>
/// Structurally the same as frmPOS, with one addition that changes the
/// flow: clinical warnings must be acknowledged, and a hard stop or a
/// controlled medicine needs a pharmacist's authority at the moment of
/// issue (DSP-006) - the operator's own, where they hold it.
/// </summary>
public partial class frmDispense : PhxFormBase, IDispenseView
{
    private readonly DispensePresenter? _presenter;
    private readonly IAuthenticationService? _auth;
    private readonly ViewFactory? _views;
    private readonly IServiceScopeFactory? _scopes;
    private IReadOnlyDictionary<string, string> _dosage = DosageShorthand.Catalogue();

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.ScriptDispense;

    /// Designer only. Never called at runtime.
    public frmDispense()
    {
        InitializeComponent();

        scanBar.CodeEntered += (_, code) => ProductScanned?.Invoke(this, code);
        btnFindScript.Click += (_, _) =>
            ScriptRequested?.Invoke(this, EventArgs.Empty);
        btnQuote.Click += (_, _) =>
            QuoteRequested?.Invoke(this, EventArgs.Empty);
        btnIssue.Click += (_, _) =>
            IssueRequested?.Invoke(this, EventArgs.Empty);
        btnReprint.Click += (_, _) =>
            LabelReprintRequested?.Invoke(this, EventArgs.Empty);
        btnRemove.Click += (_, _) =>
            RemoveItemRequested?.Invoke(this, EventArgs.Empty);
        btnChange.Click += (_, _) =>
        {
            _chargeOnly = false;
            ChangeItemRequested?.Invoke(this, EventArgs.Empty);
        };
        btnClose.Click += (_, _) =>
            CloseRequested?.Invoke(this, EventArgs.Empty);
        btnSuspended.Click += (_, _) =>
            SuspendedRequested?.Invoke(this, EventArgs.Empty);

        // Click rather than CheckedChanged: only the operator's own tick
        // is saved, not the presenter setting the box as the screen opens.
        chkLabels.Click += (_, _) =>
        {
            ApplyLabelChoice();
            ShowStage(_stage);
            PrintLabelsChanged?.Invoke(this, EventArgs.Empty);
        };
        chkTrailer.Click += (_, _) =>
            PrintTrailerChanged?.Invoke(this, EventArgs.Empty);

        // An item is changed by double-clicking it and taken off with
        // Delete, as a list is everywhere else. Double-clicking one of its
        // amounts goes straight to the charge.
        grdItems.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0) return;

            _chargeOnly = e.ColumnIndex >= 0
                && grdItems.Columns[e.ColumnIndex].Name is "Tariff" or "CoPayment";
            ChangeItemRequested?.Invoke(this, EventArgs.Empty);
        };

        grdItems.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Delete || !btnRemove.Enabled) return;

            e.Handled = true;
            RemoveItemRequested?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>
    /// Closing with the window's own button goes the same way as Close,
    /// so a dispensing begun here is set aside first.
    /// </summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (_presenter is { HasUnissuedDispensing: true }
            && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            CloseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        base.OnFormClosing(e);
    }

    /// Runtime. Resolved by the container.
    public frmDispense(DispensePresenter presenter,
                       IAuthenticationService auth, ViewFactory views,
                       IServiceScopeFactory scopes)
        : this()
    {
        _presenter = presenter;
        _auth = auth;
        _views = views;
        _scopes = scopes;

        // A product added by name is offered from its first few letters.
        scanBar.SuggestFrom(Suggest.Products(scopes));
        _presenter.Attach(this);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        SeatSplitter();
    }

    /// <summary>
    /// Once the screen is up, so the patient and script pickers it opens
    /// with sit over a window that has been drawn.
    /// </summary>
    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await LoadDosageAsync();
        if (_presenter is not null) await _presenter.InitialiseAsync();
    }

    /// <summary>
    /// The Dosage Codes list, so "T2 TDS PRN" is written out before the
    /// pharmacist confirms it. The standard codes stand if the list cannot
    /// be read.
    /// </summary>
    private async Task LoadDosageAsync()
    {
        if (_scopes is null) return;

        try
        {
            using var scope = _scopes.CreateScope();
            _dosage = await scope.ServiceProvider
                .GetRequiredService<IReferenceRepository>()
                .CatalogueAsync();
        }
#pragma warning disable CA1031 // The standard codes still expand.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
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
            ? PhxTheme.Ink
            : PhxTheme.ProblemText;

        lblConditions.Text = patient.ChronicConditions.Count == 0
            ? ""
            : "Chronic: " + string.Join(", ", patient.ChronicConditions);
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
            grdItems.Rows.Add(i.LineNo,
                i.Owed is { } owed ? $"{i.Description}  (to follow {owed:0.###})" : i.Description,
                i.Quantity,
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
            ? PhxTheme.Problem
            : PhxTheme.Field;
    }

    public void ShowCoPayment(decimal tariff, decimal contribution,
                              decimal coPayment)
    {
        lblTariffValue.Text = Format.Amount(tariff);
        lblContributionValue.Text = Format.Amount(contribution);

        // What the patient actually pays carries the symbol.
        lblCoPaymentValue.Text = Format.Money(coPayment);
    }

    private DispenseStage _stage = DispenseStage.NoScript;

    public void ShowStage(DispenseStage stage)
    {
        _stage = stage;

        lblNext.Text = stage switch
        {
            DispenseStage.NoScript =>
                $"Press {KeyOf(btnFindScript)}Find script to choose the "
                + "patient and the script.",
            DispenseStage.NoItems =>
                "Scan or type the first item in the Product box above.",
            _ when chkLabels.Checked =>
                "Check each item - double-click one to change it, or its "
                + "Tariff to change the charge; Delete to take it off - then "
                + $"press {KeyOf(btnIssue)}Issue to print the labels.",
            _ => "Check each item - double-click one to change it, or its "
                 + "Tariff to change the charge; Delete to take it off - then "
                 + $"press {KeyOf(btnIssue)}Issue. No labels will print."
        };

        btnIssue.Enabled = stage == DispenseStage.Ready;
        btnChange.Enabled = stage == DispenseStage.Ready;
        btnRemove.Enabled = stage == DispenseStage.Ready;

        // Enter in the product box adds an item, so it must not also be
        // the Issue button. With no script yet, Enter follows the hint.
        AcceptButton = stage == DispenseStage.NoScript ? btnFindScript : null;

        if (stage == DispenseStage.NoScript) btnFindScript.Focus();
        else scanBar.TakeFocus();
    }

    /// The function key on a button, as the hint names it: "F6 ".
    private string KeyOf(Button button) =>
        FunctionKeyMap.Where(pair => pair.Value == button)
                      .Select(pair => FunctionKeys.Name(pair.Key) + " ")
                      .FirstOrDefault() ?? "";

    public bool PrintLabels
    {
        get => chkLabels.Checked;
        set
        {
            chkLabels.Checked = value;
            ApplyLabelChoice();
        }
    }

    public bool PrintTrailer
    {
        get => chkTrailer.Checked;
        set => chkTrailer.Checked = value;
    }

    /// The trailer is a label, so it follows the labels tick.
    private void ApplyLabelChoice() => chkTrailer.Enabled = chkLabels.Checked;

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

    public (Guid PatientId, Guid ScriptId)? PromptQuote()
    {
        if (_views is null) return null;

        using var quote = _views.Create<frmDispensaryQuote>();

        return quote.ShowDialog(this) == DialogResult.OK
               && quote.CreatedForPatientId is Guid patientId
               && quote.CreatedScriptId is Guid scriptId
            ? (patientId, scriptId)
            : null;
    }

    public (Guid PatientId, Guid ScriptId)? StartWith { get; private set; }

    /// <summary>
    /// Opens on a script already chosen - one just captured from a quote.
    /// Called before ShowDialog.
    /// </summary>
    public void BeginOn(Guid patientId, Guid scriptId) =>
        StartWith = (patientId, scriptId);

    public int? SelectedLineNo =>
        grdItems.CurrentRow?.Cells["LineNo"].Value is int lineNo
            ? lineNo
            : null;

    public DirectionsDto? PromptDirections(ItemProposalDto proposal) =>
        frmItemPrompt.Ask(this, proposal, _dosage);

    public void ShowLabelScreen()
    {
        if (_views is null) return;

        using var labels = _views.Create<frmLabels>();
        labels.ShowDialog(this);
    }

    public Guid? PromptPharmacistAuthorisation(string reasonShown)
    {
        if (_auth is null) return null;

        using var dialog = new frmSupervisorPrompt(_auth, reasonShown,
            Domain.Security.PermissionCode.ScriptAuthorise);

        return dialog.ShowDialog(this) == DialogResult.OK
            ? dialog.AuthoriserId
            : null;
    }

    public string? PromptWarningOverride(ClinicalWarningDto warning)
    {
        ArgumentNullException.ThrowIfNull(warning);

        return frmTextPrompt.Ask(this, "Clinical warning",
            $"{warning.Message}\r\n\r\nWhy is this being overridden?",
            hint: "e.g. Prescriber confirmed the dose by phone");
    }

    public event EventHandler? PatientRequested;
    public event EventHandler? ScriptRequested;
    public event EventHandler? QuoteRequested;
    public event EventHandler<string>? ProductScanned;
    public event EventHandler? IssueRequested;
    public event EventHandler? LabelReprintRequested;
    public event EventHandler? RemoveItemRequested;
    public event EventHandler? ChangeItemRequested;

    /// The charge double-clicked, rather than the item or Change item.
    private bool _chargeOnly;

    public ItemChange.Answer? PromptItemChange(
        IReadOnlyList<DispensingLineDto> items, int? lineNo) =>
        ItemChange.Ask(this, items, lineNo, issued: false, _chargeOnly);

    public Guid? PromptChargeAuthorisation(string reasonShown)
    {
        if (_auth is null) return null;

        using var dialog = new frmSupervisorPrompt(_auth, reasonShown,
            Domain.Security.PermissionCode.DispensingChargeAdjust);

        return dialog.ShowDialog(this) == DialogResult.OK
            ? dialog.AuthoriserId
            : null;
    }
    public event EventHandler? CloseRequested;
    public event EventHandler? SuspendedRequested;
    public event EventHandler? PrintLabelsChanged;
    public event EventHandler? PrintTrailerChanged;

    public (Guid PatientId, Guid ScriptId)? PromptSuspended(
        IReadOnlyList<SuspendedDispensingDto> kept) =>
        frmChoicePrompt.Ask(this, "Suspended scripts", "The script to finish",
            kept.Select(k => ($"{k.Patient}  -  script {k.ScriptNumber}  ({k.Items} items)",
                              (k.PatientId, k.ScriptId))).ToList());

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
