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
        // box, so a script is found by its number, its doctor or its date.
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
    }

    public Guid? PatientId { get; private set; }
    public bool IsPickerMode { get; private set; }

    /// Called before ShowDialog: a script list only means anything in
    /// the context of one patient.
    public void LoadForPatient(Guid patientId, bool asPicker = false)
    {
        PatientId = patientId;
        IsPickerMode = asPicker;

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
        ShowMatching();
    }

    private static string Date(DateOnly date) =>
        date.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.CurrentCulture);

    /// The scripts with every word of the search, a page at a time.
    private void ShowMatching()
    {
        var search = txtSearch.Text.Trim();
        var scripts = _all
            .Where(s => Application.Abstractions.SearchTerms.Matches(search,
                s.ScriptNumber, s.PrescriberName, Date(s.PrescribedOn), s.Status))
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
            ? $"{_all.Count} script(s)"
            : $"{scripts.Count} of {_all.Count} script(s)";

        _suggestions.Offer(scripts.Select(s => new Suggestion(s.Id, s.ScriptNumber,
            Suggest.Details(s.PrescriberName, Date(s.PrescribedOn), s.Status))));
    }

    public void SelectScript(Guid scriptId) =>
        Suggest.SelectRow(grdScripts,
            item => item is ScriptDto script && script.Id == scriptId);

    public string? PromptCancelReason() =>
        frmTextPrompt.Ask(this, "Cancel script",
            "Why is this script being cancelled? The script is kept and "
            + "marked cancelled; it is never deleted.",
            hint: "e.g. Prescriber wrote a new script");

    public event EventHandler? RefreshRequested;
    public event EventHandler? CaptureRequested;
    public event EventHandler? EditScriptRequested;
    public event EventHandler? CancelScriptRequested;
    public event EventHandler? ChooseRequested;
    public event EventHandler? QuoteRequested;
    public event EventHandler? UseAgainRequested;
}
