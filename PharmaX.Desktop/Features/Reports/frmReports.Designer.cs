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
    private Button btnExportPdf = null!;
    private CheckBox chkGraph = null!;
    private PictureBox picGraph = null!;
    private Panel pnlGraph = null!;
    private Splitter splGraph = null!;
    private Button btnEnlargeGraph = null!;
    private Button btnClose = null!;
    private Panel pnlAsk = null!;
    private Label lblAsk = null!;
    private TextBox txtAsk = null!;
    private Button btnAsk = null!;
    private Label lblAskResult = null!;
    private TextBox txtFindings = null!;

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
        btnExportPdf = new Button();
        btnExportPdf.Name = "btnExportPdf";
        chkGraph = new CheckBox();
        chkGraph.Name = "chkGraph";
        picGraph = new PictureBox();
        picGraph.Name = "picGraph";
        pnlGraph = new Panel();
        splGraph = new Splitter();
        btnEnlargeGraph = new Button();
        btnEnlargeGraph.Name = "btnEnlargeGraph";
        btnClose = new Button();
        btnClose.Name = "btnClose";
        pnlAsk = new Panel();
        lblAsk = new Label();
        txtAsk = new TextBox();
        btnAsk = new Button();
        btnAsk.Name = "btnAsk";
        lblAskResult = new Label();
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

        // Wide enough for the hour and minute that Sales Summary adds.
        dtpFrom.Format = DateTimePickerFormat.Short;
        dtpFrom.Location = new Point(210, 10);
        dtpFrom.Name = "dtpFrom";
        dtpFrom.Size = new Size(150, 23);
        dtpFrom.TabIndex = 2;

        // Defaults to today, which is the range asked for far more often
        // than any other.
        dtpFrom.Value = DateTime.Today;

        lblTo.AutoSize = true;
        lblTo.Location = new Point(376, 14);
        lblTo.Text = "&To";

        dtpTo.Format = DateTimePickerFormat.Short;
        dtpTo.Location = new Point(406, 10);
        dtpTo.Name = "dtpTo";
        dtpTo.Size = new Size(150, 23);
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

        // What the graph shows in sentences, under the rows. Read-only
        // text rather than a label so it can be copied into an email.
        txtFindings = new TextBox();
        txtFindings.BackColor = Color.White;
        txtFindings.Dock = DockStyle.Bottom;
        txtFindings.Height = 120;
        txtFindings.Multiline = true;
        txtFindings.Name = "txtFindings";
        txtFindings.ReadOnly = true;
        txtFindings.ScrollBars = ScrollBars.Vertical;
        txtFindings.TabStop = false;
        txtFindings.Visible = false;

        splMain.Panel1.Controls.Add(trePresets);
        // The graph, beside the rows, where the report has one. The
        // divider beside it drags to give it more of the screen, and
        // Enlarge (or a double-click) opens it in a window of its own.
        picGraph.BackColor = Color.White;
        picGraph.Cursor = Cursors.Hand;
        picGraph.Dock = DockStyle.Fill;
        picGraph.SizeMode = PictureBoxSizeMode.Zoom;

        btnEnlargeGraph.AutoSize = true;
        btnEnlargeGraph.Dock = DockStyle.Top;
        btnEnlargeGraph.Text = "Enlarge &graph (or double-click it)";
        btnEnlargeGraph.UseVisualStyleBackColor = true;

        pnlGraph.BackColor = Color.White;
        pnlGraph.Controls.Add(picGraph);
        pnlGraph.Controls.Add(btnEnlargeGraph);
        pnlGraph.Dock = DockStyle.Right;
        pnlGraph.Name = "pnlGraph";
        pnlGraph.Visible = false;
        pnlGraph.Width = 360;

        splGraph.Dock = DockStyle.Right;
        splGraph.MinExtra = 200;
        splGraph.MinSize = 200;
        splGraph.Name = "splGraph";
        splGraph.Visible = false;
        splGraph.Width = 6;

        splMain.Panel2.Controls.Add(grdPreview);
        splMain.Panel2.Controls.Add(splGraph);
        splMain.Panel2.Controls.Add(pnlGraph);
        splMain.Panel2.Controls.Add(txtFindings);
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

        btnExportPdf.Location = new Point(284, 8);
        btnExportPdf.Size = new Size(130, 30);
        btnExportPdf.TabIndex = 6;
        btnExportPdf.Text = "Export to P&DF";
        btnExportPdf.UseVisualStyleBackColor = true;

        chkGraph.AutoSize = true;
        chkGraph.Checked = true;
        chkGraph.Location = new Point(428, 14);
        chkGraph.TabIndex = 7;
        chkGraph.Text = "&Graph (preview, PDF and Excel)";

        btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnClose.Location = new Point(950, 8);
        btnClose.Size = new Size(130, 30);
        btnClose.TabIndex = 6;
        btnClose.Text = "&Close (F7)";
        btnClose.UseVisualStyleBackColor = true;

        pnlButtons.Controls.AddRange(new Control[]
            { btnClose, chkGraph, btnExportPdf, btnExport, btnPreview });
        pnlButtons.Dock = DockStyle.Bottom;
        pnlButtons.Height = 48;
        pnlButtons.Name = "pnlButtons";

        // pnlAsk - a request in plain words for the offline assistant,
        // across the top. Hidden until the presenter finds a model
        // installed on this computer. Sized to the window before its
        // children are anchored, so the right-hand anchors measure from
        // the real edge.
        pnlAsk.Size = new Size(1100, 66);

        lblAsk.AutoSize = true;
        lblAsk.Location = new Point(10, 14);
        lblAsk.Text = "&Ask";

        txtAsk.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        txtAsk.Location = new Point(80, 10);
        txtAsk.Name = "txtAsk";
        txtAsk.PlaceholderText =
            "Ask for a report in your own words, e.g. stock expiring in the next 3 months";
        txtAsk.Size = new Size(860, 23);
        txtAsk.TabIndex = 0;

        btnAsk.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnAsk.Location = new Point(950, 8);
        btnAsk.Size = new Size(130, 30);
        btnAsk.TabIndex = 1;
        btnAsk.Text = "&Suggest";
        btnAsk.UseVisualStyleBackColor = true;

        lblAskResult.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        lblAskResult.AutoEllipsis = true;
        lblAskResult.Location = new Point(80, 40);
        lblAskResult.Name = "lblAskResult";
        lblAskResult.Size = new Size(1000, 20);

        pnlAsk.Controls.AddRange(new Control[]
            { lblAskResult, btnAsk, txtAsk, lblAsk });
        pnlAsk.Dock = DockStyle.Top;
        pnlAsk.Name = "pnlAsk";
        pnlAsk.TabIndex = 7;
        pnlAsk.Visible = false;

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1100, 680);
        Controls.Add(splMain);
        Controls.Add(pnlButtons);
        Controls.Add(pnlAsk);
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
