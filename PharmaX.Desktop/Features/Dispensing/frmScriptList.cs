using PharmaX.Application.Dispensing.Dtos;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Dispensing;

/// <summary>
/// A patient's scripts. Serves as script maintenance off the Dispensary
/// menu and as the script picker the dispensing screen opens, for the
/// same reason the patient list does: a pharmacist choosing a script
/// often needs to capture a new one first.
/// </summary>
public partial class frmScriptList : PhxFormBase, IScriptListView
{
    private readonly ScriptListPresenter? _presenter;

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.ScriptCapture;

    /// Read by those who capture scripts and those who dispense them.
    protected override bool MayOpen(
        Application.Security.IPermissionService permissions) =>
        Application.Security.PermissionServiceExtensions.HasAny(permissions,
            Domain.Security.PermissionCode.ScriptCapture,
            Domain.Security.PermissionCode.ScriptDispense);

    /// Designer only. Never called at runtime.
    public frmScriptList()
    {
        InitializeComponent();

        btnCapture.Click += (_, _) =>
            CaptureRequested?.Invoke(this, EventArgs.Empty);
        btnEditScript.Click += (_, _) =>
            EditScriptRequested?.Invoke(this, EventArgs.Empty);
        btnCancelScript.Click += (_, _) =>
            CancelScriptRequested?.Invoke(this, EventArgs.Empty);
        btnDeleteScript.Click += (_, _) =>
            DeleteScriptRequested?.Invoke(this, EventArgs.Empty);
        chkShowCancelled.CheckedChanged += (_, _) =>
        {
            GridPager.Of(grdScripts)?.Restart();
            ShowMatching();
        };
        btnRefresh.Click += (_, _) =>
            RefreshRequested?.Invoke(this, EventArgs.Empty);
        btnChoose.Click += (_, _) =>
            ChooseRequested?.Invoke(this, EventArgs.Empty);
        btnQuote.Click += (_, _) =>
            QuoteRequested?.Invoke(this, EventArgs.Empty);
        btnUseAgain.Click += (_, _) =>
            UseAgainRequested?.Invoke(this, EventArgs.Empty);
        btnClose.Click += (_, _) => CloseView(false);

        grdScripts.DoubleClick += (_, _) =>
        {
            if (IsPickerMode) ChooseRequested?.Invoke(this, EventArgs.Empty);
            else if (btnEditScript.Visible)
                EditScriptRequested?.Invoke(this, EventArgs.Empty);
        };

        // Every word narrows the scripts, and the matches come up under the
        // box, so a script is found by its number, a medicine on it, its
        // doctor or its date.
        _suggestions = new SearchSuggestions(txtSearch) { MinimumLength = 1 };
        _suggestions.Chosen += (_, chosen) =>
        {
            if (chosen.Value is not Guid id) return;

            SelectScript(id);
            if (IsPickerMode) ChooseRequested?.Invoke(this, EventArgs.Empty);
        };

        txtSearch.TextChanged += (_, _) =>
        {
            GridPager.Of(grdScripts)?.Restart();
            ShowMatching();
        };
    }

    private readonly SearchSuggestions _suggestions;

    /// Every script the patient has, before the search narrows them.
    private IReadOnlyList<ScriptDto> _all = Array.Empty<ScriptDto>();

    /// Runtime. Resolved by the container.
    public frmScriptList(ScriptListPresenter presenter,
        Application.Security.IPermissionService permissions) : this()
    {
        _permissions = permissions;
        _presenter = presenter;
        _presenter.Attach(this);
    }

    private readonly Application.Security.IPermissionService? _permissions;

    /// <summary>
    /// Capturing a script and cancelling one are separate authorities from
    /// reading them; each button is there for those who hold it. Read when
    /// the window opens, not when it is built.
    /// </summary>
    private void ApplyPermissions()
    {
        if (_permissions is null) return;

        if (!Application.Security.PermissionServiceExtensions.Has(
                _permissions, Domain.Security.PermissionCode.ScriptCapture))
        {
            btnCapture.Visible = false;
            btnEditScript.Visible = false;
            btnUseAgain.Visible = false;
        }

        if (!Application.Security.PermissionServiceExtensions.Has(
                _permissions, Domain.Security.PermissionCode.ScriptReverse))
            btnCancelScript.Visible = false;

        btnDeleteScript.Visible = Application.Security.PermissionServiceExtensions.Has(
            _permissions, Domain.Security.PermissionCode.ScriptRemove);
    }

    public Guid? PatientId { get; private set; }
    public bool IsPickerMode { get; private set; }

    /// Called before ShowDialog: a script list only means anything in
    /// the context of one patient.
    public void LoadForPatient(Guid patientId, bool asPicker = false,
                               Guid? select = null)
    {
        PatientId = patientId;
        IsPickerMode = asPicker;
        _select = select;

        if (asPicker)
        {
            Text = "PharmaX - Find script";
            btnChoose.Visible = true;
            AcceptButton = btnChoose;
            btnCancelScript.Visible = false;
        }
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ApplyPermissions();
        if (_presenter is not null) await _presenter.InitialiseAsync();
        txtSearch.Focus();
    }

    public Guid? SelectedScriptId => grdScripts.CurrentRow?.DataBoundItem
        is ScriptDto dto ? dto.Id : null;

    public void ShowPatient(string description) =>
        lblPatient.Text = description;

    public void ShowScripts(IReadOnlyList<ScriptDto> scripts)
    {
        ArgumentNullException.ThrowIfNull(scripts);

        _all = scripts;

        // Opened on one script (from Day history): shown even if cancelled.
        if (_select is Guid id && scripts.Any(s => s.Id == id && s.Status == "Cancelled"))
            chkShowCancelled.Checked = true;

        ShowMatching();

        if (_select is Guid wanted) SelectScript(wanted);
        _select = null;
    }

    private Guid? _select;

    private static string Date(DateOnly date) =>
        date.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.CurrentCulture);

    /// The scripts with every word of the search, a page at a time.
    private void ShowMatching()
    {
        var search = txtSearch.Text.Trim();
        var shown = chkShowCancelled.Checked
            ? _all
            : _all.Where(s => s.Status != "Cancelled").ToList();
        var scripts = shown
            .Where(s => Application.Abstractions.SearchTerms.Matches(search,
                s.ScriptNumber, s.Items, s.PrescriberName, Date(s.PrescribedOn),
                s.Status))
            .ToList();

        GridPager.Attach(grdScripts).Show(scripts);

        foreach (DataGridViewRow row in grdScripts.Rows)
        {
            if (row.DataBoundItem is not ScriptDto script) continue;

            // Cancelled and completed scripts cannot be dispensed
            // against, so they read as unavailable rather than as
            // ordinary rows the pharmacist might pick by mistake.
            if (script.Status is "Cancelled" or "Completed"
                || script.RepeatsRemaining < 0)
                row.DefaultCellStyle.ForeColor = PhxTheme.Muted;
        }

        lblCount.Text = search.Length == 0
            ? $"{shown.Count} script(s)"
            : $"{scripts.Count} of {shown.Count} script(s)";

        // The suggestion names the medicines too, on one line.
        _suggestions.Offer(scripts.Select(s => new Suggestion(s.Id, s.ScriptNumber,
            Suggest.Details(Date(s.PrescribedOn),
                s.Items.Replace(Environment.NewLine, ", ", StringComparison.Ordinal),
                s.PrescriberName, s.Status))));
    }

    public void SelectScript(Guid scriptId) =>
        Suggest.SelectRow(grdScripts,
            item => item is ScriptDto script && script.Id == scriptId);

    public string? PromptCancelReason() =>
        frmTextPrompt.Ask(this, "Cancel script",
            "Why is this script being cancelled? The script is kept and "
            + "marked cancelled; it is never deleted.",
            hint: "e.g. Prescriber wrote a new script");

    public string? PromptDeleteReason(string scriptNumber) =>
        frmTextPrompt.Ask(this, "Delete script",
            $"Delete script {scriptNumber}? It leaves every list and cannot be "
            + "brought back here. A dispensing still open on it is set aside; "
            + "anything already issued and paid for stays in the history. "
            + "Medicine already issued and waiting on Scripts to collect stays there; delete it from that screen.",
            hint: "e.g. Captured twice by mistake");

    public event EventHandler? DeleteScriptRequested;
    public event EventHandler? RefreshRequested;
    public event EventHandler? CaptureRequested;
    public event EventHandler? EditScriptRequested;
    public event EventHandler? CancelScriptRequested;
    public event EventHandler? ChooseRequested;
    public event EventHandler? QuoteRequested;
    public event EventHandler? UseAgainRequested;
}
