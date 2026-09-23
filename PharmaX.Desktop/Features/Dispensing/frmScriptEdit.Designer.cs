namespace PharmaX.Desktop.Features.Dispensing;

partial class frmScriptEdit
{
    private System.ComponentModel.IContainer components = null!;
    private Label lblPatient = null!;
    private TextBox txtPrescriber = null!;
    private TextBox txtPrescriberNumber = null!;
    private DateTimePicker dtpPrescribedOn = null!;
    private CheckBox chkHasValidUntil = null!;
    private DateTimePicker dtpValidUntil = null!;
    private NumericUpDown numRepeats = null!;
    private DataGridView grdItems = null!;
    private Button btnAddItem = null!;
    private Button btnRemoveItem = null!;
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
        lblPatient = new Label();
        txtPrescriber = new TextBox();
        txtPrescriber.Name = "txtPrescriber";
        txtPrescriberNumber = new TextBox();
        txtPrescriberNumber.Name = "txtPrescriberNumber";
        dtpPrescribedOn = new DateTimePicker();
        chkHasValidUntil = new CheckBox();
        dtpValidUntil = new DateTimePicker();
        numRepeats = new NumericUpDown();
        grdItems = new DataGridView();
        btnAddItem = new Button();
        btnAddItem.Name = "btnAddItem";
        btnRemoveItem = new Button();
        btnRemoveItem.Name = "btnRemoveItem";
        btnSave = new Button();
        btnSave.Name = "btnSave";
        btnCancel = new Button();
        btnCancel.Name = "btnCancel";
        ((System.ComponentModel.ISupportInitialize)numRepeats).BeginInit();
        ((System.ComponentModel.ISupportInitialize)grdItems).BeginInit();
        SuspendLayout();

        lblPatient.AutoSize = false;
        lblPatient.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
        lblPatient.Location = new Point(20, 16);
        lblPatient.Name = "lblPatient";
        lblPatient.Size = new Size(560, 24);

        Field("&Prescriber", txtPrescriber, 1, 300);
        txtPrescriber.MaxLength = 150;

        Field("Prescriber &no.", txtPrescriberNumber, 2, 160);
        txtPrescriberNumber.MaxLength = 30;

        Label(20, 20 + 3 * 34 + 4, "Prescri&bed on");
        dtpPrescribedOn.Format = DateTimePickerFormat.Short;
        dtpPrescribedOn.Location = new Point(150, 20 + 3 * 34);
        dtpPrescribedOn.Name = "dtpPrescribedOn";
        dtpPrescribedOn.Size = new Size(120, 23);
        dtpPrescribedOn.MaxDate = DateTime.Today;
        dtpPrescribedOn.Value = DateTime.Today;

        Label(20, 20 + 4 * 34 + 4, "&Repeats");
        numRepeats.Location = new Point(150, 20 + 4 * 34);
        numRepeats.Maximum = 11;
        numRepeats.Name = "numRepeats";
        numRepeats.Size = new Size(60, 23);

        // The default validity is six months from the prescription date
        // (specification Section 9.2), so the field stays off unless the
        // prescriber wrote something else.
        chkHasValidUntil.AutoSize = true;
        chkHasValidUntil.Location = new Point(240, 20 + 4 * 34 + 2);
        chkHasValidUntil.Name = "chkHasValidUntil";
        chkHasValidUntil.Text = "Prescriber set a &validity date";
        chkHasValidUntil.UseVisualStyleBackColor = true;

        dtpValidUntil.Enabled = false;
        dtpValidUntil.Format = DateTimePickerFormat.Short;
        dtpValidUntil.Location = new Point(450, 20 + 4 * 34);
        dtpValidUntil.Name = "dtpValidUntil";
        dtpValidUntil.Size = new Size(120, 23);
        dtpValidUntil.Value = DateTime.Today.AddMonths(6);

        Label(20, 20 + 5 * 34 + 6, "&Items");

        grdItems.AllowUserToAddRows = false;
        grdItems.AllowUserToResizeRows = false;
        grdItems.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grdItems.Location = new Point(150, 20 + 5 * 34);
        grdItems.Name = "grdItems";
        grdItems.RowHeadersVisible = false;
        grdItems.RowTemplate.Height = 24;
        grdItems.Size = new Size(560, 150);

        grdItems.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ProductCode", HeaderText = "Product code",
            FillWeight = 90, SortMode = DataGridViewColumnSortMode.NotSortable
        });

        grdItems.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Quantity", HeaderText = "Qty", FillWeight = 50,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Format = "0.###",
                Alignment = DataGridViewContentAlignment.MiddleRight
            }
        });

        grdItems.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Directions", HeaderText = "Directions (printed on label)",
            FillWeight = 190,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        grdItems.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "AllowSubstitution", HeaderText = "Generic ok",
            FillWeight = 60,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        btnAddItem.Location = new Point(150, 20 + 5 * 34 + 158);
        btnAddItem.Size = new Size(90, 26);
        btnAddItem.Text = "A&dd";
        btnAddItem.UseVisualStyleBackColor = true;

        btnRemoveItem.Location = new Point(246, 20 + 5 * 34 + 158);
        btnRemoveItem.Size = new Size(90, 26);
        btnRemoveItem.Text = "Re&move";
        btnRemoveItem.UseVisualStyleBackColor = true;

        btnSave.Location = new Point(544, 20 + 5 * 34 + 196);
        btnSave.Size = new Size(80, 30);
        btnSave.Text = "&Save";
        btnSave.UseVisualStyleBackColor = true;

        btnCancel.Location = new Point(630, 20 + 5 * 34 + 196);
        btnCancel.Size = new Size(80, 30);
        btnCancel.Text = "Cancel";
        btnCancel.UseVisualStyleBackColor = true;

        AcceptButton = btnSave;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = btnCancel;
        ClientSize = new Size(740, 20 + 5 * 34 + 244);
        Controls.AddRange(new Control[]
        {
            lblPatient, dtpPrescribedOn, numRepeats, chkHasValidUntil,
            dtpValidUntil, grdItems, btnAddItem, btnRemoveItem, btnSave,
            btnCancel
        });
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "frmScriptEdit";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Capture script";
        ((System.ComponentModel.ISupportInitialize)numRepeats).EndInit();
        ((System.ComponentModel.ISupportInitialize)grdItems).EndInit();
        ResumeLayout(false);
        PerformLayout();
    }

    private void Field(string caption, Control input, int row, int width)
    {
        var y = 20 + row * 34;
        Label(20, y + 4, caption);

        input.Location = new Point(150, y);
        input.Size = new Size(width, 23);
        input.TabIndex = row;
        Controls.Add(input);
    }

    private void Label(int x, int y, string text) =>
        Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(x, y),
            Text = text
        });
}
