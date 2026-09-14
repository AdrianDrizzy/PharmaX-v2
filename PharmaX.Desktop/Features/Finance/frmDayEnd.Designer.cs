using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Finance;

partial class frmDayEnd
{
    private System.ComponentModel.IContainer components = null!;
    private Panel pnlTop = null!;
    private Panel pnlSummary = null!;
    private Panel pnlBottom = null!;
    private Label lblDay = null!;
    private DateTimePicker dtpDay = null!;
    private Button btnSummarise = null!;
    private Label lblSummary = null!;
    private SplitContainer splMain = null!;
    private PhxGrid grdBatches = null!;
    private PhxGrid grdLines = null!;
    private Label lblBalance = null!;
    private Button btnPrepare = null!;
    private Button btnPost = null!;
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
        pnlTop = new Panel();
        pnlSummary = new Panel();
        pnlBottom = new Panel();
        lblDay = new Label();
        dtpDay = new DateTimePicker();
        btnSummarise = new Button();
        lblSummary = new Label();
        splMain = new SplitContainer();
        grdBatches = new PhxGrid();
        grdLines = new PhxGrid();
        lblBalance = new Label();
        btnPrepare = new Button();
        btnPost = new Button();
        btnExport = new Button();
        btnClose = new Button();
        ((System.ComponentModel.ISupportInitialize)splMain).BeginInit();
        SuspendLayout();

        lblDay.AutoSize = true;
        lblDay.Location = new Point(PhxTheme.Gutter, 17);
        lblDay.Text = "Trading &day";

        dtpDay.Format = DateTimePickerFormat.Short;
        dtpDay.Location = new Point(100, 13);
        dtpDay.Name = "dtpDay";
        dtpDay.Size = new Size(140, 23);
        dtpDay.TabIndex = 0;

        btnSummarise.Location = new Point(250, 12);
        btnSummarise.Name = "btnSummarise";
        btnSummarise.Size = new Size(120, 26);
        btnSummarise.TabIndex = 1;
        btnSummarise.Text = "&Look at day";
        btnSummarise.UseVisualStyleBackColor = true;

        pnlTop.Controls.AddRange(new Control[]
            { btnSummarise, dtpDay, lblDay });
        pnlTop.Dock = DockStyle.Top;
        pnlTop.Height = 50;
        pnlTop.Name = "pnlTop";

        lblSummary.Dock = DockStyle.Fill;
        lblSummary.Name = "lblSummary";
        lblSummary.Padding = new Padding(PhxTheme.Gutter, 6, PhxTheme.Gutter, 6);
        lblSummary.Text = "Choose a day and look at what it took.";

        pnlSummary.BackColor = PhxTheme.Band;
        pnlSummary.Controls.Add(lblSummary);
        pnlSummary.Dock = DockStyle.Top;
        pnlSummary.Height = 74;
        pnlSummary.Name = "pnlSummary";

        grdBatches.AutoGenerateColumns = false;
        grdBatches.Dock = DockStyle.Fill;
        grdBatches.Name = "grdBatches";

        Bind(grdBatches, "PostingDate", "Day", 90, "dd/MM/yyyy");
        Bind(grdBatches, "BatchNumber", "Journal", 110);
        Bind(grdBatches, "Source", "Source", 100);
        Bind(grdBatches, "TotalDebits", "Debits", 100, "N2", true);
        Bind(grdBatches, "TotalCredits", "Credits", 100, "N2", true);
        Bind(grdBatches, "IsPosted", "Posted", 70);

        grdLines.AutoGenerateColumns = false;
        grdLines.Dock = DockStyle.Fill;
        grdLines.Name = "grdLines";

        Bind(grdLines, "LineNo", "#", 40, rightAlign: true);
        Bind(grdLines, "AccountCode", "Account", 80);
        Bind(grdLines, "AccountName", "Name", 200);
        Bind(grdLines, "Debit", "Debit", 100, "N2", true);
        Bind(grdLines, "Credit", "Credit", 100, "N2", true);
        Bind(grdLines, "Narration", "Narration", 220);

        splMain.Dock = DockStyle.Fill;
        splMain.Name = "splMain";
        splMain.Orientation = Orientation.Vertical;
        splMain.Panel1.Controls.Add(grdBatches);
        splMain.Panel2.Controls.Add(grdLines);
        splMain.SplitterDistance = 580;

        lblBalance.AutoSize = false;
        lblBalance.Font = PhxTheme.Emphasis;
        lblBalance.Location = new Point(PhxTheme.Gutter, 10);
        lblBalance.Name = "lblBalance";
        lblBalance.Size = new Size(560, 24);

        btnPrepare.Location = new Point(PhxTheme.Gutter, 40);
        btnPrepare.Name = "btnPrepare";
        btnPrepare.Size = new Size(150, 30);
        btnPrepare.Text = "&Prepare journal";
        btnPrepare.UseVisualStyleBackColor = true;

        btnPost.Location = new Point(170, 40);
        btnPost.Name = "btnPost";
        btnPost.Size = new Size(130, 30);
        btnPost.Text = "P&ost journal";
        btnPost.UseVisualStyleBackColor = true;

        btnExport.Location = new Point(306, 40);
        btnExport.Name = "btnExport";
        btnExport.Size = new Size(130, 30);
        btnExport.Text = "&Export CSV";
        btnExport.UseVisualStyleBackColor = true;

        btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnClose.Location = new Point(900, 40);
        btnClose.Size = new Size(90, 30);
        btnClose.Text = "&Close";
        btnClose.UseVisualStyleBackColor = true;

        pnlBottom.BackColor = PhxTheme.Band;
        pnlBottom.Controls.AddRange(new Control[]
            { btnClose, btnExport, btnPost, btnPrepare, lblBalance });
        pnlBottom.Dock = DockStyle.Bottom;
        pnlBottom.Height = 82;
        pnlBottom.Name = "pnlBottom";

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1010, 620);
        Controls.Add(splMain);
        Controls.Add(pnlSummary);
        Controls.Add(pnlTop);
        Controls.Add(pnlBottom);
        Name = "frmDayEnd";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Day-end posting";
        ((System.ComponentModel.ISupportInitialize)splMain).EndInit();
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
            SortMode = DataGridViewColumnSortMode.NotSortable
        };

        if (format is not null) column.DefaultCellStyle.Format = format;

        if (rightAlign)
            column.DefaultCellStyle.Alignment =
                DataGridViewContentAlignment.MiddleRight;

        grid.Columns.Add(column);
    }
}
