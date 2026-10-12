using PharmaX.Desktop.Controls;

namespace PharmaX.Desktop.Features.Reports;

partial class frmReports
{
    private System.ComponentModel.IContainer components = null!;
    private SplitContainer splMain = null!;
    private TreeView trePresets = null!;
    private Panel pnlParameters = null!;
    private Panel pnlSlicers = null!;
    private Panel pnlButtons = null!;
    private Label lblBranch = null!;
    private Label lblFrom = null!;
    private Label lblTo = null!;
    private Label lblProvenance = null!;
    private TextBox txtBranch = null!;
    private DateTimePicker dtpFrom = null!;
    private DateTimePicker dtpTo = null!;
    private PhxGrid grdPreview = null!;
    private Button btnPreview = null!;
    private Button btnExport = null!;
    private Button btnClose = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        splMain = new SplitContainer();
        trePresets = new TreeView();
        pnlParameters = new Panel();
        pnlSlicers = new Panel();
        pnlButtons = new Panel();
        lblBranch = new Label();
        lblFrom = new Label();
        lblTo = new Label();
        lblProvenance = new Label();
        txtBranch = new TextBox();
        dtpFrom = new DateTimePicker();
        dtpTo = new DateTimePicker();
        grdPreview = new PhxGrid();
        btnPreview = new Button();
        btnPreview.Name = "btnPreview";
        btnExport = new Button();
        btnExport.Name = "btnExport";
        btnClose = new Button();
        btnClose.Name = "btnClose";
        ((System.ComponentModel.ISupportInitialize)splMain).BeginInit();
        splMain.Panel1.SuspendLayout();
        splMain.Panel2.SuspendLayout();
        splMain.SuspendLayout();
        SuspendLayout();

        // trePresets - one node per incumbent report name, grouped.
        trePresets.Dock = DockStyle.Fill;
        trePresets.HideSelection = false;
        trePresets.Name = "trePresets";
        trePresets.TabIndex = 0;

        // Parameters
        lblBranch.AutoSize = true;
        lblBranch.Location = new Point(10, 14);
        lblBranch.Text = "&Branch";

        txtBranch.Location = new Point(80, 10);
        txtBranch.Name = "txtBranch";
        txtBranch.PlaceholderText = "e.g. 1";
        txtBranch.Size = new Size(60, 23);
        txtBranch.TabIndex = 1;

        lblFrom.AutoSize = true;
        lblFrom.Location = new Point(160, 14);
        lblFrom.Text = "F&rom";

        dtpFrom.Format = DateTimePickerFormat.Short;
        dtpFrom.Location = new Point(210, 10);
        dtpFrom.Name = "dtpFrom";
        dtpFrom.Size = new Size(120, 23);
        dtpFrom.TabIndex = 2;

        // Defaults to today, which is the range asked for far more often
        // than any other.
        dtpFrom.Value = DateTime.Today;

        lblTo.AutoSize = true;
        lblTo.Location = new Point(346, 14);
        lblTo.Text = "&To";

        dtpTo.Format = DateTimePickerFormat.Short;
        dtpTo.Location = new Point(376, 10);
        dtpTo.Name = "dtpTo";
        dtpTo.Size = new Size(120, 23);
        dtpTo.TabIndex = 3;
        dtpTo.Value = DateTime.Today;

        pnlParameters.Controls.AddRange(new Control[]
            { dtpTo, lblTo, dtpFrom, lblFrom, txtBranch, lblBranch });
        pnlParameters.Dock = DockStyle.Top;
        pnlParameters.Height = 46;
        pnlParameters.Name = "pnlParameters";

        // Slicers are built at run time from the chosen preset.
        pnlSlicers.AutoScroll = true;
        pnlSlicers.Dock = DockStyle.Top;
        pnlSlicers.Height = 140;
        pnlSlicers.Name = "pnlSlicers";

        grdPreview.Dock = DockStyle.Fill;
        grdPreview.Name = "grdPreview";

        lblProvenance.AutoSize = false;
        lblProvenance.Dock = DockStyle.Bottom;
        lblProvenance.Height = 34;
        lblProvenance.Name = "lblProvenance";
        lblProvenance.Padding = new Padding(6, 8, 6, 8);

        splMain.Panel1.Controls.Add(trePresets);
        splMain.Panel2.Controls.Add(grdPreview);
        splMain.Panel2.Controls.Add(lblProvenance);
        splMain.Panel2.Controls.Add(pnlSlicers);
        splMain.Panel2.Controls.Add(pnlParameters);
        splMain.Dock = DockStyle.Fill;
        splMain.Name = "splMain";
        splMain.FixedPanel = FixedPanel.Panel1;



        // Applied for real in OnLoad: see SeatSplitter.

        splMain.SplitterDistance = 300;
        splMain.TabIndex = 0;

        btnPreview.Location = new Point(12, 8);
        btnPreview.Size = new Size(130, 30);
        btnPreview.TabIndex = 4;
        btnPreview.Text = "&Preview (F12)";
        btnPreview.UseVisualStyleBackColor = true;

        btnExport.Location = new Point(148, 8);
        btnExport.Size = new Size(130, 30);
        btnExport.TabIndex = 5;
        btnExport.Text = "&Export to Excel";
        btnExport.UseVisualStyleBackColor = true;

        btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnClose.Location = new Point(950, 8);
        btnClose.Size = new Size(130, 30);
        btnClose.TabIndex = 6;
        btnClose.Text = "&Close (F7)";
        btnClose.UseVisualStyleBackColor = true;

        pnlButtons.Controls.AddRange(new Control[]
            { btnClose, btnExport, btnPreview });
        pnlButtons.Dock = DockStyle.Bottom;
        pnlButtons.Height = 48;
        pnlButtons.Name = "pnlButtons";

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1100, 680);
        Controls.Add(splMain);
        Controls.Add(pnlButtons);
        KeyPreview = true;
        Name = "frmReports";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Reports";
        splMain.Panel1.ResumeLayout(false);
        splMain.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)splMain).EndInit();
        splMain.ResumeLayout(false);
        ResumeLayout(false);
    }
}
