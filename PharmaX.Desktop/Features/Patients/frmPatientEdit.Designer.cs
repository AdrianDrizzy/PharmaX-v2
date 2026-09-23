using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Patients;

partial class frmPatientEdit
{
    private System.ComponentModel.IContainer components = null!;
    private TabControl tabs = null!;
    private TabPage tabIdentity = null!;
    private TabPage tabClinical = null!;
    private TabPage tabMedicalAid = null!;
    private ComboBox cboScheme = null!;
    private TextBox txtMembershipNumber = null!;
    private TextBox txtDependantCode = null!;
    private Label lblFileNumber = null!;
    private Label lblFileNumberValue = null!;
    private Label lblAge = null!;
    private TextBox txtSurname = null!;
    private TextBox txtFirstNames = null!;
    private TextBox txtIdNumber = null!;
    private TextBox txtTelephone = null!;
    private TextBox txtEmail = null!;
    private CheckBox chkHasDateOfBirth = null!;
    private DateTimePicker dtpDateOfBirth = null!;
    private MoneyTextBox txtWeight = null!;
    private CheckBox chkPregnant = null!;
    private CheckBox chkBreastfeeding = null!;
    private TextBox txtNotes = null!;
    private TextBox txtChronicConditions = null!;
    private DataGridView grdAllergies = null!;
    private Button btnAddAllergy = null!;
    private Button btnRemoveAllergy = null!;
    private Button btnSave = null!;
    private Button btnCancel = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        tabs = new TabControl();
        tabIdentity = new TabPage("Identity");
        tabIdentity.Name = "tabIdentity";
        tabClinical = new TabPage("Clinical");
        tabClinical.Name = "tabClinical";
        tabMedicalAid = new TabPage("Medical aid");
        tabMedicalAid.Name = "tabMedicalAid";
        cboScheme = new ComboBox();
        cboScheme.Name = "cboScheme";
        txtMembershipNumber = new TextBox();
        txtMembershipNumber.Name = "txtMembershipNumber";
        txtDependantCode = new TextBox();
        txtDependantCode.Name = "txtDependantCode";
        lblFileNumber = new Label();
        lblFileNumberValue = new Label();
        lblAge = new Label();
        txtSurname = new TextBox();
        txtSurname.Name = "txtSurname";
        txtFirstNames = new TextBox();
        txtFirstNames.Name = "txtFirstNames";
        txtIdNumber = new TextBox();
        txtIdNumber.Name = "txtIdNumber";
        txtTelephone = new TextBox();
        txtTelephone.Name = "txtTelephone";
        txtEmail = new TextBox();
        txtEmail.Name = "txtEmail";
        chkHasDateOfBirth = new CheckBox();
        dtpDateOfBirth = new DateTimePicker();
        txtWeight = new MoneyTextBox();
        txtWeight.Name = "txtWeight";
        chkPregnant = new CheckBox();
        chkBreastfeeding = new CheckBox();
        txtNotes = new TextBox();
        txtChronicConditions = new TextBox();
        grdAllergies = new DataGridView();
        btnAddAllergy = new Button();
        btnAddAllergy.Name = "btnAddAllergy";
        btnRemoveAllergy = new Button();
        btnRemoveAllergy.Name = "btnRemoveAllergy";
        btnSave = new Button();
        btnSave.Name = "btnSave";
        btnCancel = new Button();
        btnCancel.Name = "btnCancel";
        ((System.ComponentModel.ISupportInitialize)grdAllergies).BeginInit();
        SuspendLayout();

        // ---- Identity ---------------------------------------------------
        lblFileNumber.AutoSize = true;
        lblFileNumber.Location = new Point(20, 20);
        lblFileNumber.Text = "File number";

        // Allocated by the service, so it is shown and never typed.
        lblFileNumberValue.AutoSize = false;
        lblFileNumberValue.Font = new Font("Consolas", 10F, FontStyle.Bold);
        lblFileNumberValue.Location = new Point(150, 18);
        lblFileNumberValue.Name = "lblFileNumberValue";
        lblFileNumberValue.Size = new Size(160, 20);
        lblFileNumberValue.Text = "(allocated on save)";

        Field(tabIdentity, "&Surname", txtSurname, 1, 260, 200);
        Field(tabIdentity, "&First names", txtFirstNames, 2, 260, 200);
        Field(tabIdentity, "&ID number", txtIdNumber, 3, 180, 20);
        Field(tabIdentity, "&Telephone", txtTelephone, 5, 180, 30);
        Field(tabIdentity, "&Email", txtEmail, 6, 260, 150);

        chkHasDateOfBirth.AutoSize = true;
        chkHasDateOfBirth.Location = new Point(150, 20 + 4 * 34);
        chkHasDateOfBirth.Name = "chkHasDateOfBirth";
        chkHasDateOfBirth.Text = "&Date of birth known";
        chkHasDateOfBirth.UseVisualStyleBackColor = true;
        chkHasDateOfBirth.CheckedChanged += (_, _) =>
            dtpDateOfBirth.Enabled = chkHasDateOfBirth.Checked;

        dtpDateOfBirth.Enabled = false;
        dtpDateOfBirth.Format = DateTimePickerFormat.Short;
        dtpDateOfBirth.Location = new Point(310, 16 + 4 * 34);
        dtpDateOfBirth.Name = "dtpDateOfBirth";
        dtpDateOfBirth.Size = new Size(120, 23);

        // A century is enough range for a pharmacy, and it stops a
        // mistyped year producing a 400-year-old patient.
        dtpDateOfBirth.MinDate = DateTime.Today.AddYears(-120);
        dtpDateOfBirth.MaxDate = DateTime.Today;
        dtpDateOfBirth.Value = DateTime.Today.AddYears(-30);

        lblAge.AutoSize = true;
        lblAge.ForeColor = SystemColors.GrayText;
        lblAge.Location = new Point(440, 20 + 4 * 34);
        lblAge.Name = "lblAge";

        tabIdentity.Controls.AddRange(new Control[]
        {
            lblFileNumber, lblFileNumberValue, chkHasDateOfBirth,
            dtpDateOfBirth, lblAge
        });
        tabIdentity.UseVisualStyleBackColor = true;

        // ---- Clinical ---------------------------------------------------
        Field(tabClinical, "&Weight (kg)", txtWeight, 0, 90, 0);

        chkPregnant.AutoSize = true;
        chkPregnant.Location = new Point(280, 22);
        chkPregnant.Name = "chkPregnant";
        chkPregnant.Text = "Pre&gnant";
        chkPregnant.UseVisualStyleBackColor = true;

        chkBreastfeeding.AutoSize = true;
        chkBreastfeeding.Location = new Point(380, 22);
        chkBreastfeeding.Name = "chkBreastfeeding";
        chkBreastfeeding.Text = "&Breastfeeding";
        chkBreastfeeding.UseVisualStyleBackColor = true;

        tabClinical.Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(20, 62),
            Text = "&Allergies"
        });

        // Editable, because this is the screen where an allergy is
        // recorded and the dispensing screen only reads it.
        grdAllergies.AllowUserToAddRows = false;
        grdAllergies.AllowUserToResizeRows = false;
        grdAllergies.AutoSizeColumnsMode =
            DataGridViewAutoSizeColumnsMode.Fill;
        grdAllergies.Location = new Point(150, 58);
        grdAllergies.Name = "grdAllergies";
        grdAllergies.RowHeadersVisible = false;
        grdAllergies.RowTemplate.Height = 24;
        grdAllergies.Size = new Size(420, 120);

        grdAllergies.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Substance", HeaderText = "Substance", FillWeight = 140,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        // A dropdown, not free text: severity decides whether an
        // assistant may acknowledge a warning or a pharmacist must.
        var severity = new DataGridViewComboBoxColumn
        {
            Name = "Severity", HeaderText = "Severity", FillWeight = 90,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            FlatStyle = FlatStyle.Flat
        };

        severity.Items.AddRange(Severities);
        grdAllergies.Columns.Add(severity);

        grdAllergies.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Reaction", HeaderText = "Reaction", FillWeight = 170,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        btnAddAllergy.Location = new Point(150, 184);
        btnAddAllergy.Size = new Size(90, 26);
        btnAddAllergy.Text = "A&dd";
        btnAddAllergy.UseVisualStyleBackColor = true;

        btnRemoveAllergy.Location = new Point(246, 184);
        btnRemoveAllergy.Size = new Size(90, 26);
        btnRemoveAllergy.Text = "&Remove";
        btnRemoveAllergy.UseVisualStyleBackColor = true;

        tabClinical.Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(20, 224),
            Text = "&Chronic conditions"
        });

        txtChronicConditions.Location = new Point(150, 220);
        txtChronicConditions.Multiline = true;
        txtChronicConditions.Name = "txtChronicConditions";
        txtChronicConditions.ScrollBars = ScrollBars.Vertical;
        txtChronicConditions.Size = new Size(420, 70);

        tabClinical.Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(20, 302),
            Text = "&Notes"
        });

        txtNotes.Location = new Point(150, 298);
        txtNotes.MaxLength = 2000;
        txtNotes.Multiline = true;
        txtNotes.Name = "txtNotes";
        txtNotes.ScrollBars = ScrollBars.Vertical;
        txtNotes.Size = new Size(420, 70);

        tabClinical.Controls.AddRange(new Control[]
        {
            chkPregnant, chkBreastfeeding, grdAllergies, btnAddAllergy,
            btnRemoveAllergy, txtChronicConditions, txtNotes
        });
        tabClinical.UseVisualStyleBackColor = true;

        // ---- Medical aid -------------------------------------------------
        cboScheme.DropDownStyle = ComboBoxStyle.DropDownList;
        Field(tabMedicalAid, "&Scheme", cboScheme, 0, 260, 0);

        // "Membership number" is wider than the gap the Field helper
        // leaves before the input, and ran into it.
        Field(tabMedicalAid, "Membership &no.", txtMembershipNumber,
              1, 200, 40);

        Field(tabMedicalAid, "&Dependant code", txtDependantCode, 2, 80, 10);

        tabMedicalAid.Controls.Add(new Label
        {
            AutoSize = false,
            ForeColor = PhxTheme.Muted,
            Location = new Point(20, 20 + 3 * 34 + 12),
            Size = new Size(520, 60),

            // Said on the screen because it is the part staff get wrong:
            // a claim is only raised where both are present, and a
            // dependant code of "1" where the scheme expects "01" comes
            // back rejected weeks later.
            Text = "A claim is raised automatically when a script is "
                 + "issued, but only where a scheme and a membership "
                 + "number are both recorded here. Enter the dependant "
                 + "code exactly as the scheme writes it - \"00\" for the "
                 + "main member."
        });

        tabMedicalAid.UseVisualStyleBackColor = true;

        tabs.Controls.Add(tabIdentity);
        tabs.Controls.Add(tabClinical);
        tabs.Controls.Add(tabMedicalAid);
        tabs.Dock = DockStyle.Top;
        tabs.Height = 420;
        tabs.Name = "tabs";
        tabs.TabIndex = 0;

        btnSave.Location = new Point(420, 432);
        btnSave.Size = new Size(80, 30);
        btnSave.TabIndex = 1;
        btnSave.Text = "&Save";
        btnSave.UseVisualStyleBackColor = true;

        btnCancel.Location = new Point(506, 432);
        btnCancel.Size = new Size(80, 30);
        btnCancel.TabIndex = 2;
        btnCancel.Text = "Cancel";
        btnCancel.UseVisualStyleBackColor = true;

        AcceptButton = btnSave;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = btnCancel;
        ClientSize = new Size(610, 476);
        Controls.Add(tabs);
        Controls.Add(btnSave);
        Controls.Add(btnCancel);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "frmPatientEdit";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Patient";
        ((System.ComponentModel.ISupportInitialize)grdAllergies).EndInit();
        ResumeLayout(false);
        PerformLayout();
    }

    private static void Field(Control parent, string caption, Control input,
                              int row, int width, int maxLength)
    {
        var y = 20 + row * 34;

        parent.Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(20, y + 4),
            Text = caption
        });

        input.Location = new Point(150, y);
        input.Size = new Size(width, 23);
        input.TabIndex = row;

        if (maxLength > 0 && input is TextBox box) box.MaxLength = maxLength;

        parent.Controls.Add(input);
    }
}
