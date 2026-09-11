using System.Globalization;
using PharmaX.Application.Patients.Dtos;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Patients;

public partial class frmPatientEdit : PhxFormBase, IPatientEditView
{
    private static readonly string[] Severities =
        { "Mild", "Moderate", "Severe" };

    private readonly PatientEditPresenter? _presenter;

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.ScriptAuthorise;

    /// Designer only. Never called at runtime.
    public frmPatientEdit()
    {
        InitializeComponent();

        btnSave.Click += (_, _) => SaveRequested?.Invoke(this, EventArgs.Empty);
        btnCancel.Click += (_, _) =>
            CancelRequested?.Invoke(this, EventArgs.Empty);

        btnAddAllergy.Click += (_, _) => AddAllergyRow();
        btnRemoveAllergy.Click += (_, _) => RemoveAllergyRow();
    }

    /// Runtime. Resolved by the container.
    public frmPatientEdit(PatientEditPresenter presenter) : this()
    {
        _presenter = presenter;
        _presenter.Attach(this);
    }

    public Guid? PatientId { get; private set; }

    /// <summary>
    /// Called by the list screen before ShowDialog. Null means a new
    /// patient, whose file number the service allocates.
    /// </summary>
    public void LoadPatient(Guid? id)
    {
        PatientId = id;
        Text = id is null ? "PharmaX - New patient" : "PharmaX - Patient";
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_presenter is not null) await _presenter.InitialiseAsync();
    }

    public void ShowPatient(PatientDto patient)
    {
        ArgumentNullException.ThrowIfNull(patient);

        lblFileNumberValue.Text = patient.FileNumber;
        txtSurname.Text = patient.Surname;
        txtFirstNames.Text = patient.FirstNames;
        txtIdNumber.Text = patient.IdNumber ?? "";
        txtTelephone.Text = patient.Telephone ?? "";
        txtEmail.Text = patient.Email ?? "";

        chkHasDateOfBirth.Checked = patient.DateOfBirth is not null;

        if (patient.DateOfBirth is DateOnly born)
            dtpDateOfBirth.Value = born.ToDateTime(TimeOnly.MinValue);

        txtWeight.Amount = patient.WeightKg;
        chkPregnant.Checked = patient.IsPregnant;
        chkBreastfeeding.Checked = patient.IsBreastfeeding;
        txtNotes.Text = patient.ClinicalNotes ?? "";

        txtChronicConditions.Text =
            string.Join(Environment.NewLine, patient.ChronicConditions);

        grdAllergies.Rows.Clear();

        foreach (var allergy in patient.Allergies)
            grdAllergies.Rows.Add(allergy.Substance, allergy.Severity,
                                  allergy.Reaction ?? "");

        lblAge.Text = patient.Age is int age
            ? string.Format(CultureInfo.CurrentCulture, "age {0}", age)
            : "";
    }

    public PatientEditDto Collect() => new(
        Surname: txtSurname.Text.Trim(),
        FirstNames: txtFirstNames.Text.Trim(),
        IdNumber: Blank(txtIdNumber.Text),
        DateOfBirth: chkHasDateOfBirth.Checked
            ? DateOnly.FromDateTime(dtpDateOfBirth.Value.Date)
            : null,
        Telephone: Blank(txtTelephone.Text),
        Email: Blank(txtEmail.Text),
        WeightKg: txtWeight.Amount,
        IsPregnant: chkPregnant.Checked,
        IsBreastfeeding: chkBreastfeeding.Checked,
        ClinicalNotes: Blank(txtNotes.Text),
        Allergies: CollectAllergies(),
        ChronicConditions: txtChronicConditions.Lines
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList());

    private IReadOnlyList<AllergyDto> CollectAllergies()
    {
        var allergies = new List<AllergyDto>();

        foreach (DataGridViewRow row in grdAllergies.Rows)
        {
            if (row.IsNewRow) continue;

            var substance = row.Cells["Substance"].Value?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(substance)) continue;

            // Defaults to the middle severity rather than the mildest: an
            // unspecified allergy should not be the one that gets waved
            // through.
            var severity = row.Cells["Severity"].Value?.ToString()
                           ?? "Moderate";

            var reaction = row.Cells["Reaction"].Value?.ToString()?.Trim();

            allergies.Add(new AllergyDto(substance, severity,
                string.IsNullOrWhiteSpace(reaction) ? null : reaction));
        }

        return allergies;
    }

    private void AddAllergyRow() =>
        grdAllergies.Rows.Add("", "Moderate", "");

    private void RemoveAllergyRow()
    {
        if (grdAllergies.CurrentRow is { IsNewRow: false } row)
            grdAllergies.Rows.Remove(row);
    }

    private static string? Blank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public event EventHandler? SaveRequested;
    public event EventHandler? CancelRequested;
}
