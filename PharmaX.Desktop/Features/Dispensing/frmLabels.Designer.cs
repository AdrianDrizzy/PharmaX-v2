using PharmaX.Desktop.Controls;

namespace PharmaX.Desktop.Features.Dispensing;

partial class frmLabels
{
    private System.ComponentModel.IContainer components = null!;
    private Panel pnlTop = null!;
    private Panel pnlBottom = null!;
    private Label lblPatient = null!;
    private TextBox txtPatient = null!;
    private Button btnChoosePatient = null!;
    private PhxGrid grdDispensings = null!;
    private Button btnPreview = null!;
    private Button btnReprint = null!;
    private Button btnReverse = null!;
    private Button btnClaimForm = null!;
    private Button btnChangeItem = null!;
    private Button btnUseAgain = null!;
    private Button btnClose = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        pnlTop = new Panel();
        pnlBottom = new Panel();
        lblPatient = new Label();
        txtPatient = new TextBox();
        btnChoosePatient = new Button();
        grdDispensings = new PhxGrid();
        btnPreview = new Button();
        btnPreview.Name = "btnPreview";
        btnReprint = new Button();
        btnReprint.Name = "btnReprint";
        btnReverse = new Button();
        btnReverse.Name = "btnReverse";
        btnClaimForm = new Button();
        btnClaimForm.Name = "btnClaimForm";
        btnChangeItem = new Button();
        btnChangeItem.Name = "btnChangeItem";
        btnUseAgain = new Button();
        btnUseAgain.Name = "btnUseAgain";
        btnClose = new Button();
        btnClose.Name = "btnClose";
        SuspendLayout();

        lblPatient.AutoSize = true;
        lblPatient.Location = new Point(12, 17);
        lblPatient.Text = "&Patient";

        txtPatient.Location = new Point(70, 13);
        txtPatient.Name = "txtPatient";
        txtPatient.PlaceholderText = "Choose a patient to see what was dispensed";
        txtPatient.ReadOnly = true;
        txtPatient.Size = new Size(420, 23);

        btnChoosePatient.Location = new Point(500, 12);
        btnChoosePatient.Name = "btnChoosePatient";
        btnChoosePatient.Size = new Size(90, 26);
        btnChoosePatient.TabIndex = 0;
        btnChoosePatient.Text = "F&ind...";
        btnChoosePatient.UseVisualStyleBackColor = true;

        pnlTop.Controls.AddRange(new Control[]
            { btnChoosePatient, txtPatient, lblPatient });
        pnlTop.Dock = DockStyle.Top;
        pnlTop.Height = 50;
        pnlTop.Name = "pnlTop";

        grdDispensings.AutoGenerateColumns = false;
        grdDispensings.Dock = DockStyle.Fill;
        grdDispensings.Name = "grdDispensings";

        Bind(grdDispensings, "DispensedOn", "Dispensed", 90, "dd/MM/yyyy");
        Bind(grdDispensings, "ScriptNumber", "Script", 100);

        // What was handed over, one medicine a line: the dispensing to
        // reprint, change or use again is found by what is on it.
        Bind(grdDispensings, "Items", "Items", 320);
        grdDispensings.Columns["Items"]!.DefaultCellStyle.WrapMode =
            DataGridViewTriState.True;
        grdDispensings.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;

        Bind(grdDispensings, "DocumentNumber", "Document", 110);
        Bind(grdDispensings, "Labels", "Labels", 55, rightAlign: true);
        Bind(grdDispensings, "CoPayment", "Patient pays", 80, "N2",
             rightAlign: true);

        btnPreview.Location = new Point(12, 8);
        btnPreview.Size = new Size(120, 30);
        btnPreview.TabIndex = 1;
        btnPreview.Text = "&What prints";
        btnPreview.UseVisualStyleBackColor = true;

        btnReprint.Location = new Point(138, 8);
        btnReprint.Size = new Size(120, 30);
        btnReprint.TabIndex = 2;
        btnReprint.Text = "&Print labels";
        btnReprint.UseVisualStyleBackColor = true;

        // Taking back what was issued to the wrong patient, or not in
        // the end handed over. Shown only to those who may reverse.
        btnReverse.Location = new Point(264, 8);
        btnReverse.Size = new Size(140, 30);
        btnReverse.TabIndex = 3;
        btnReverse.Text = "Re&verse dispensing";
        btnReverse.UseVisualStyleBackColor = true;

        // The medical aid claim form, on the A4 printer, for the member to
        // sign - again, if the first was lost or never printed.
        btnClaimForm.Location = new Point(410, 8);
        btnClaimForm.Size = new Size(110, 30);
        btnClaimForm.TabIndex = 4;
        btnClaimForm.Text = "Claim &form";
        btnClaimForm.UseVisualStyleBackColor = true;

        // The quantity handed over or the amount charged for one item,
        // corrected after the issue: the stock and what the patient pays
        // follow. Shown only to those who may.
        btnChangeItem.Location = new Point(526, 8);
        btnChangeItem.Size = new Size(120, 30);
        btnChangeItem.TabIndex = 5;
        btnChangeItem.Text = "C&hange item...";
        btnChangeItem.UseVisualStyleBackColor = true;

        // A new script from this one - imported history included - to
        // change and dispense again.
        btnUseAgain.Location = new Point(652, 8);
        btnUseAgain.Size = new Size(110, 30);
        btnUseAgain.TabIndex = 6;
        btnUseAgain.Text = "&Use again...";
        btnUseAgain.UseVisualStyleBackColor = true;

        btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnClose.Location = new Point(870, 8);
        btnClose.Size = new Size(90, 30);
        btnClose.TabIndex = 7;
        btnClose.Text = "&Close";
        btnClose.UseVisualStyleBackColor = true;

        pnlBottom.Controls.AddRange(new Control[]
        {
            btnClose, btnUseAgain, btnChangeItem, btnClaimForm, btnReverse,
            btnReprint, btnPreview
        });
        pnlBottom.Dock = DockStyle.Bottom;
        pnlBottom.Height = 48;
        pnlBottom.Name = "pnlBottom";

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(980, 480);
        Controls.Add(grdDispensings);
        Controls.Add(pnlTop);
        Controls.Add(pnlBottom);
        Name = "frmLabels";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Labels";
        ResumeLayout(false);
        PerformLayout();
    }

    private static void Bind(DataGridView grid, string property,
        string header, int fillWeight, string? format = null,
        bool rightAlign = false)
    {
        var column = new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            Name = property,
            HeaderText = header,
            FillWeight = fillWeight,
            SortMode = DataGridViewColumnSortMode.Automatic
        };

        if (format is not null) column.DefaultCellStyle.Format = format;

        if (rightAlign)
            column.DefaultCellStyle.Alignment =
                DataGridViewContentAlignment.MiddleRight;

        grid.Columns.Add(column);
    }
}
