using System.Globalization;
using PharmaX.Application.Patients.Dtos;
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

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.PatientMaintain;

    /// Designer only. Never called at runtime.
    public frmPatientList()
    {
        InitializeComponent();

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
        btnDocuments.Click += (_, _) => ShowDocuments();
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
    public frmPatientList(PatientListPresenter presenter, ViewFactory views)
        : this()
    {
        _presenter = presenter;
        _views = views;
        _presenter.Attach(this);
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
    public void UseAsPicker()
    {
        IsPickerMode = true;
        Text = "PharmaX - Find patient";
        btnChoose.Visible = true;
        AcceptButton = btnChoose;
        btnDeactivate.Visible = false;
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
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
                row.DefaultCellStyle.BackColor = Color.FromArgb(255, 244, 214);

            if (!patient.IsActive)
                row.DefaultCellStyle.ForeColor = SystemColors.GrayText;
        }
    }

    public void ShowCount(int count) =>
        lblCount.Text = string.Format(CultureInfo.CurrentCulture,
            "{0:N0} patient(s)", count);

    public event EventHandler? SearchChanged;
    public event EventHandler? AddRequested;
    public event EventHandler? EditRequested;
    public event EventHandler? DeactivateRequested;
    public event EventHandler? ChooseRequested;
}
