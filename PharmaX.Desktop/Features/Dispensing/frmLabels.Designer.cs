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

        Bind(grdDispensings, "DispensedOn", "Dispensed", 110, "dd/MM/yyyy");
        Bind(grdDispensings, "ScriptNumber", "Script", 130);
        Bind(grdDispensings, "DocumentNumber", "Document", 150);
        Bind(grdDispensings, "Labels", "Labels", 70, rightAlign: true);

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

        btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnClose.Location = new Point(590, 8);
        btnClose.Size = new Size(90, 30);
        btnClose.TabIndex = 5;
        btnClose.Text = "&Close";
        btnClose.UseVisualStyleBackColor = true;

        pnlBottom.Controls.AddRange(new Control[]
            { btnClose, btnClaimForm, btnReverse, btnReprint, btnPreview });
        pnlBottom.Dock = DockStyle.Bottom;
        pnlBottom.Height = 48;
        pnlBottom.Name = "pnlBottom";

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(700, 480);
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
