using System.Globalization;
using PharmaX.Application.Patients.Dtos;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Patients;

/// <summary>
/// Serves twice: as the maintenance screen off the Dispensary menu, and
/// as the patient picker the dispensing screen opens. One screen rather
/// than two, because a pharmacist searching for a patient wants the same
/// list either way - and often wants to add a new one mid-search.
/// </summary>
public partial class frmPatientList : PhxFormBase, IPatientListView
{
    private readonly PatientListPresenter? _presenter;
    private readonly ViewFactory? _views;

    private readonly System.Windows.Forms.Timer _debounce =
        new() { Interval = 250 };

    /// The matching patients, offered under the search box - surname
    /// first, with the file, telephone and address that tell two of the
    /// same name apart.
    private readonly SearchSuggestions _suggestions;

    /// The pages of the matches, under the list.
    private readonly PagerBar _pager = new("pager") { Empty = "No patients match" };

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.PatientMaintain;

    /// Designer only. Never called at runtime.
    public frmPatientList()
    {
        InitializeComponent();

        // After InitializeComponent, as on the product list: docked
        // between the grid and the buttons.
        Controls.Add(_pager);
        Controls.SetChildIndex(_pager, Controls.GetChildIndex(pnlTop));
        _pager.PageRequested += (_, move) => PageRequested?.Invoke(this, move);

        _suggestions = new SearchSuggestions(txtSearch);
        _suggestions.Chosen += (_, chosen) =>
        {
            if (!Suggest.SelectRow(grdPatients, item =>
                    item is PatientListItemDto p && Equals(p.Id, chosen.Value)))
                return;

            if (IsPickerMode) ChooseRequested?.Invoke(this, EventArgs.Empty);
        };

        // An address kept over two lines reads as one on the list.
        grdPatients.CellFormatting += (_, e) =>
        {
            if (e.ColumnIndex < 0 || e.Value is not string value) return;

            if (grdPatients.Columns[e.ColumnIndex].DataPropertyName == "Address")
            {
                e.Value = Suggest.OneLine(value);
                e.FormattingApplied = true;
            }
        };

        txtSearch.TextChanged += (_, _) => Restart();
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            SearchChanged?.Invoke(this, EventArgs.Empty);
        };

        btnAdd.Click += (_, _) => AddRequested?.Invoke(this, EventArgs.Empty);
        btnEdit.Click += (_, _) => EditRequested?.Invoke(this, EventArgs.Empty);
        btnDeactivate.Click += (_, _) =>
            DeactivateRequested?.Invoke(this, EventArgs.Empty);
        chkShowInactive.CheckedChanged += (_, _) =>
            SearchChanged?.Invoke(this, EventArgs.Empty);

        // The button says what it will do to the row chosen.
        grdPatients.SelectionChanged += (_, _) =>
            btnDeactivate.Text = SelectedRow is { IsActive: false }
                ? "&Reactivate"
                : "&Deactivate";
        btnDocuments.Click += (_, _) => ShowDocuments();
        btnExport.Click += (_, _) => _presenter?.ExportAll(Export);
        btnChoose.Click += (_, _) =>
            ChooseRequested?.Invoke(this, EventArgs.Empty);
        btnClose.Click += (_, _) => CloseView(false);

        grdPatients.DoubleClick += (_, _) =>
        {
            if (IsPickerMode) ChooseRequested?.Invoke(this, EventArgs.Empty);
            else EditRequested?.Invoke(this, EventArgs.Empty);
        };
    }

    /// Runtime. Resolved by the container.
    public frmPatientList(PatientListPresenter presenter, ViewFactory views,
        Application.Reporting.IWorkbookWriter workbooks,
        Application.Abstractions.ICurrentUser user,
        Application.Security.IPermissionService permissions)
        : this()
    {
        _presenter = presenter;
        _views = views;
        _workbooks = workbooks;
        _user = user;
        _permissions = permissions;
        _presenter.Attach(this);
    }

    private readonly Application.Security.IPermissionService? _permissions;
    private readonly Application.Reporting.IWorkbookWriter? _workbooks;
    private readonly Application.Abstractions.ICurrentUser? _user;

    /// <summary>
    /// Every patient the search matches to a workbook, not only the page
    /// on screen, in the columns the grid shows.
    /// </summary>
    private void Export(IReadOnlyList<object> rows)
    {
        if (_workbooks is null || _user is null) return;   // designer

        var saved = GridExport.Offer(this, _workbooks, grdPatients, "Patients",
            string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0}, {1:N0} rows",
                SearchText.Length == 0 ? "All patients" : $"\"{SearchText}\"", rows.Count),
            _user.Username, rows);

        if (saved is not null) ShowSaved(saved);
        else if (rows.Count == 0)
            ShowInfo("There is nothing to export.");
    }

    /// <summary>
    /// Opens the chosen patient's scanned documents.
    /// </summary>
    /// <remarks>
    /// Straight from this screen rather than through the presenter: it
    /// opens a window and changes nothing, and routing it through the
    /// presenter would only teach the presenter about a second form.
    /// </remarks>
    private void ShowDocuments()
    {
        if (_views is null) return;

        if (SelectedRow is not PatientListItemDto patient)
        {
            ShowInfo("Choose a patient first.");
            return;
        }

        using var documents = _views.Create<frmPatientAttachments>();
        documents.LoadForPatient(patient.Id, patient.FullName);
        documents.ShowDialog(this);
    }

    private PatientListItemDto? SelectedRow =>
        grdPatients.CurrentRow?.DataBoundItem as PatientListItemDto;

    public bool IsPickerMode { get; private set; }

    /// <summary>
    /// Called before ShowDialog by a screen that wants to pick a patient
    /// rather than maintain the list.
    /// </summary>
    /// <param name="term">A name to start the search with.</param>
    public void UseAsPicker(string? term = null)
    {
        if (!string.IsNullOrWhiteSpace(term)) txtSearch.Text = term.Trim();

        IsPickerMode = true;
        Text = "PharmaX - Find patient";
        btnChoose.Visible = true;
        AcceptButton = btnChoose;
        btnDeactivate.Visible = false;
        btnExport.Visible = false;

        // A deactivated patient is not picked for a new script or sale.
        chkShowInactive.Visible = false;
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Here, not in the constructor: the check reads the database. The picker never deactivates.
        if (_permissions is not null)
            btnDeactivate.Visible = !IsPickerMode && Application.Security.PermissionServiceExtensions
                .Has(_permissions, Domain.Security.PermissionCode.PatientDeactivate);
        if (_presenter is not null) await _presenter.InitialiseAsync();
        txtSearch.Focus();
    }

    private void Restart()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    public string SearchText => txtSearch.Text.Trim();

    public Guid? SelectedId => grdPatients.CurrentRow?.DataBoundItem
        is PatientListItemDto dto ? dto.Id : null;

    public bool? SelectedIsActive => SelectedRow?.IsActive;

    public IReadOnlyList<PatientListItemDto> SelectedPatients =>
        grdPatients.SelectedInOrder()
            .Select(r => r.DataBoundItem)
            .OfType<PatientListItemDto>()
            .ToList();

    public bool IncludeInactive => chkShowInactive.Checked && !IsPickerMode;

    public void ShowPatients(IReadOnlyList<PatientListItemDto> patients)
    {
        ArgumentNullException.ThrowIfNull(patients);

        grdPatients.DataSource = patients.ToList();

        foreach (DataGridViewRow row in grdPatients.Rows)
        {
            if (row.DataBoundItem is not PatientListItemDto patient) continue;

            // An allergy flag on the list is the earliest point a
            // pharmacist can see it, which is where it belongs.
            if (patient.HasAllergies)
                row.DefaultCellStyle.BackColor = PhxTheme.Attention;

            if (!patient.IsActive)
                row.DefaultCellStyle.ForeColor = PhxTheme.Muted;
        }

        _suggestions.Offer(patients.Select(Suggest.Patient));
    }

    public void ShowPage(int page, int pageSize, int shown, int total) =>
        _pager.ShowPage(page, pageSize, shown, total);

    /// <summary>
    /// Ctrl+Page Down and Ctrl+Page Up turn the page from anywhere on the
    /// screen, as on the product list.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) =>
        _pager.Press(keyData) || base.ProcessCmdKey(ref msg, keyData);

    public event EventHandler? SearchChanged;
    public event EventHandler<PageMove>? PageRequested;
    public event EventHandler? AddRequested;
    public event EventHandler? EditRequested;
    public event EventHandler? DeactivateRequested;
    public event EventHandler? ChooseRequested;
}
