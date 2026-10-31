using System.Globalization;
using PharmaX.Application.Patients.Dtos;
using PharmaX.Application.Reference;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Patients;

public partial class frmPatientEdit : PhxFormBase, IPatientEditView
{
    private static readonly string[] Severities =
        { "Mild", "Moderate", "Severe" };

    private readonly PatientEditPresenter? _presenter;

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.PatientMaintain;

    /// Designer only. Never called at runtime.
    public frmPatientEdit()
    {
        InitializeComponent();

        // The medical aid by any word of its name or code.
        PharmaX.Desktop.Controls.WordFilter.Attach(cboScheme);

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
    /// Fills the scheme list. Called before ShowPatient, so that the
    /// patient's own scheme has something to be selected in.
    /// </summary>
    public void ShowSchemes(IReadOnlyList<ReferenceItemDto> schemes)
    {
        ArgumentNullException.ThrowIfNull(schemes);

        cboScheme.Items.Clear();

        // The empty row is the point: a cash patient is the default, and
        // a list with no way back to "nobody pays this" would trap the
        // first patient somebody set a scheme on by mistake.
        cboScheme.Items.Add(new SchemeChoice(null, "(none - cash patient)"));

        foreach (var scheme in schemes)
            cboScheme.Items.Add(new SchemeChoice(scheme.Id,
                $"{scheme.Code}  {scheme.Name}"));

        cboScheme.SelectedIndex = 0;
    }

    private void SelectScheme(Guid? schemeId)
    {
        for (var i = 0; i < cboScheme.Items.Count; i++)
        {
            if (cboScheme.Items[i] is SchemeChoice choice
                && choice.Id == schemeId)
            {
                cboScheme.SelectedIndex = i;
                return;
            }
        }

        // A scheme that has since been removed from the list leaves the
        // patient on "none" rather than on somebody else's scheme.
        if (cboScheme.Items.Count > 0) cboScheme.SelectedIndex = 0;
    }

    private Guid? ChosenScheme =>
        cboScheme.SelectedItem is SchemeChoice choice ? choice.Id : null;

    private sealed record SchemeChoice(Guid? Id, string Text)
    {
        public override string ToString() => Text;
    }

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
        txtSurname.Focus();

        // "John Peter Moyo": the last word is the surname, the rest the
        // first names, which is how the staff say a name aloud.
        if (PatientId is null && !string.IsNullOrWhiteSpace(_name))
        {
            var words = _name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            txtSurname.Text = words[^1];
            txtFirstNames.Text = string.Join(' ', words[..^1]);
            txtIdNumber.Focus();
        }
    }

    private string? _name;

    /// The new patient's name, from a request to Ask PharmaX.
    public void PrefillName(string? name) => _name = name?.Trim();

    public void ShowPatient(PatientDto patient)
    {
        ArgumentNullException.ThrowIfNull(patient);

        lblFileNumberValue.Text = patient.FileNumber;
        txtSurname.Text = patient.Surname;
        txtFirstNames.Text = patient.FirstNames;
        txtIdNumber.Text = patient.IdNumber ?? "";
        txtTelephone.Text = patient.Telephone ?? "";
        txtEmail.Text = patient.Email ?? "";
        txtAddress.Text = patient.Address ?? "";

        chkHasDateOfBirth.Checked = patient.DateOfBirth is not null;

        if (patient.DateOfBirth is DateOnly born)
            dtpDateOfBirth.Value = born.ToDateTime(TimeOnly.MinValue);

        // The scheme list arrives separately, from ShowSchemes, which
        // the presenter calls first. Selecting by id rather than by name
        // so that two schemes with similar names cannot be confused.
        SelectScheme(patient.MedicalAidSchemeId);
        txtMembershipNumber.Text = patient.MembershipNumber ?? "";
        txtDependantCode.Text = patient.DependantCode ?? "";

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
            .ToList(),
        MedicalAidSchemeId: ChosenScheme,
        MembershipNumber: Blank(txtMembershipNumber.Text),
        DependantCode: Blank(txtDependantCode.Text),

        // Sent even when blank, which clears it: null would leave the
        // address as it was.
        Address: txtAddress.Text.Trim());

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
        // Every row with a selected cell, so several go at once; the
        // grid is edited cell by cell, so rows are never selected whole.
        IEnumerable<DataGridViewRow?> rows = grdAllergies.SelectedCells.Count > 0
            ? grdAllergies.SelectedCells.Cast<DataGridViewCell>()
                .Select(cell => cell.OwningRow).Distinct().ToList()
            : new[] { grdAllergies.CurrentRow };

        foreach (var row in rows)
            if (row is { IsNewRow: false }) grdAllergies.Rows.Remove(row);
    }

    private static string? Blank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public event EventHandler? SaveRequested;
    public event EventHandler? CancelRequested;
}
