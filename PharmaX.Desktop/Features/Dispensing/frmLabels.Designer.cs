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
    private Button btnToday = null!;
    private Button btnStickers = null!;
    private PhxGrid grdDispensings = null!;
    private Button btnPreview = null!;
    private Button btnReprint = null!;
    private Button btnReverse = null!;
    private Button btnClaimForm = null!;
    private Button btnChangeItem = null!;
    private Button btnUseAgain = null!;
    private Button btnClose = null!;
    private TabControl tabLabels = null!;
    private TabPage pageDispensed = null!;
    private TabPage pagePrepacks = null!;
    private PhxGrid grdPrepacks = null!;
    private TabPage pageTemplates = null!;
    private PhxGrid grdTemplates = null!;
    private Button btnDeactivate = null!;

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
        btnToday = new Button();
        btnStickers = new Button();
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
        tabLabels = new TabControl();
        pageDispensed = new TabPage();
        pagePrepacks = new TabPage();
        grdPrepacks = new PhxGrid();
        pageTemplates = new TabPage();
        grdTemplates = new PhxGrid();
        btnDeactivate = new Button();
        btnDeactivate.Name = "btnDeactivate";
        SuspendLayout();

        lblPatient.AutoSize = true;
        lblPatient.Location = new Point(12, 17);
        lblPatient.Text = "&Patient";

        txtPatient.Location = new Point(70, 13);
        txtPatient.Name = "txtPatient";
        txtPatient.PlaceholderText = "Today's labels are listed. Find a patient for older ones";
        txtPatient.ReadOnly = true;
        txtPatient.Size = new Size(330, 23);
        txtPatient.TabStop = false;

        btnChoosePatient.Location = new Point(408, 12);
        btnChoosePatient.Name = "btnChoosePatient";
        btnChoosePatient.Size = new Size(110, 26);
        btnChoosePatient.TabIndex = 0;
        btnChoosePatient.Text = "F&ind patient";
        btnChoosePatient.UseVisualStyleBackColor = true;

        btnToday.Location = new Point(524, 12);
        btnToday.Name = "btnToday";
        btnToday.Size = new Size(80, 26);
        btnToday.TabIndex = 8;
        btnToday.Text = "&Today";
        btnToday.UseVisualStyleBackColor = true;

        // Stickers for a run of pre-packs, typed in: no patient needed.
        btnStickers.Location = new Point(610, 12);
        btnStickers.Name = "btnStickers";
        btnStickers.Size = new Size(150, 26);
        btnStickers.TabIndex = 9;
        btnStickers.Text = "Pre-pack stic&kers...";
        btnStickers.UseVisualStyleBackColor = true;

        pnlTop.Controls.AddRange(new Control[]
            { btnStickers, btnToday, btnChoosePatient, txtPatient, lblPatient });
        pnlTop.Dock = DockStyle.Top;
        pnlTop.Height = 50;
        pnlTop.Name = "pnlTop";

        grdDispensings.AutoGenerateColumns = false;
        grdDispensings.Dock = DockStyle.Fill;
        grdDispensings.Name = "grdDispensings";

        pageDispensed.Name = "pageDispensed";
        pageDispensed.Text = "Dispensed";
        pageDispensed.Padding = new Padding(0);
        pageDispensed.Controls.Add(grdDispensings);

        grdPrepacks.AutoGenerateColumns = false;
        grdPrepacks.Dock = DockStyle.Fill;
        grdPrepacks.Name = "grdPrepacks";
        grdPrepacks.AddColumn("PrintedAt", "Printed", 120, "dd/MM/yyyy HH:mm");
        grdPrepacks.AddColumn("Medicine", "Medicine", 160);
        grdPrepacks.AddColumn("Quantity", "In pack", 70, "0.###", rightAlign: true);
        grdPrepacks.AddColumn("Directions", "Directions", 220);
        grdPrepacks.AddColumn("BatchNumber", "Batch", 80);
        grdPrepacks.AddColumn("Expiry", "Expiry", 90, "dd/MM/yyyy");
        grdPrepacks.AddColumn("Price", "Price", 60, "N2", rightAlign: true);
        grdPrepacks.AddColumn("Copies", "Labels", 55, rightAlign: true);
        foreach (DataGridViewColumn column in grdPrepacks.Columns)
            column.DataPropertyName = column.Name;

        grdPrepacks.AddColumn("Active", "Active", 55);
        grdPrepacks.Columns["Active"]!.DataPropertyName = "Active";

        grdPrepacks.Columns["Directions"]!.DefaultCellStyle.WrapMode =
            DataGridViewTriState.True;
        grdPrepacks.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;

        pagePrepacks.Name = "pagePrepacks";
        pagePrepacks.Text = "Pre-packs";
        pagePrepacks.Controls.Add(grdPrepacks);

        grdTemplates.AutoGenerateColumns = false;
        grdTemplates.Dock = DockStyle.Fill;
        grdTemplates.Name = "grdTemplates";
        grdTemplates.AddColumn("PrintedAt", "Printed", 120, "dd/MM/yyyy HH:mm");
        grdTemplates.AddColumn("Medicine", "Medicine", 160);
        grdTemplates.AddColumn("Quantity", "In pack", 70, "0.###", rightAlign: true);
        grdTemplates.AddColumn("Directions", "Directions", 220);
        grdTemplates.AddColumn("BatchNumber", "Batch", 80);
        grdTemplates.AddColumn("Expiry", "Expiry", 90, "dd/MM/yyyy");
        grdTemplates.AddColumn("Price", "Price", 60, "N2", rightAlign: true);
        grdTemplates.AddColumn("Copies", "Labels", 55, rightAlign: true);
        grdTemplates.AddColumn("Active", "Active", 55);
        foreach (DataGridViewColumn column in grdTemplates.Columns)
            column.DataPropertyName = column.Name;

        pageTemplates.Name = "pageTemplates";
        pageTemplates.Text = "Template Label";
        pageTemplates.Controls.Add(grdTemplates);

        tabLabels.Dock = DockStyle.Fill;
        tabLabels.Name = "tabLabels";
        tabLabels.TabPages.Add(pageDispensed);
        tabLabels.TabPages.Add(pagePrepacks);
        tabLabels.TabPages.Add(pageTemplates);

        Bind(grdDispensings, "DispensedOn", "Dispensed", 90, "dd/MM/yyyy");
        Bind(grdDispensings, "PatientName", "Patient", 150);
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

        btnDeactivate.Location = new Point(768, 8);
        btnDeactivate.Size = new Size(96, 30);
        btnDeactivate.TabIndex = 8;
        btnDeactivate.Text = "Deac&tivate";
        btnDeactivate.UseVisualStyleBackColor = true;
        btnDeactivate.Visible = false;

        btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnClose.Location = new Point(870, 8);
        btnClose.Size = new Size(90, 30);
        btnClose.TabIndex = 7;
        btnClose.Text = "&Close";
        btnClose.UseVisualStyleBackColor = true;

        pnlBottom.Controls.AddRange(new Control[]
        {
            btnClose, btnDeactivate, btnUseAgain, btnChangeItem, btnClaimForm,
            btnReverse, btnReprint, btnPreview
        });
        pnlBottom.Dock = DockStyle.Bottom;
        pnlBottom.Height = 48;
        pnlBottom.Name = "pnlBottom";

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(980, 480);
        Controls.Add(tabLabels);
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
