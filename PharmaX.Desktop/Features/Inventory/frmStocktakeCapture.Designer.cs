using PharmaX.Desktop.Controls;

namespace PharmaX.Desktop.Features.Inventory;

partial class frmStocktakeCapture
{
    private System.ComponentModel.IContainer components = null!;
    private Panel pnlHeader = null!;
    private Panel pnlTotals = null!;
    private Panel pnlButtons = null!;
    private Label lblHeader = null!;
    private Label lblProgressValue = null!;
    private Label lblNetVarianceValue = null!;
    private Label lblShrinkageValue = null!;
    private Label lblReasonValue = null!;
    private CheckBox chkVarianceOnly = null!;
    private PhxGrid grdLines = null!;
    private Button btnCount = null!;
    private Button btnClearCount = null!;
    private Button btnPost = null!;
    private Button btnClose = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        pnlHeader = new Panel();
        pnlTotals = new Panel();
        pnlButtons = new Panel();
        lblHeader = new Label();
        lblProgressValue = new Label();
        lblNetVarianceValue = new Label();
        lblShrinkageValue = new Label();
        lblReasonValue = new Label();
        chkVarianceOnly = new CheckBox();
        grdLines = new PhxGrid();
        btnCount = new Button();
        btnClearCount = new Button();
        btnPost = new Button();
        btnClose = new Button();
        SuspendLayout();

        lblHeader.AutoSize = false;
        lblHeader.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
        lblHeader.Location = new Point(12, 12);
        lblHeader.Name = "lblHeader";
        lblHeader.Size = new Size(700, 24);

        chkVarianceOnly.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        chkVarianceOnly.AutoSize = false;
        chkVarianceOnly.Location = new Point(710, 12);
        chkVarianceOnly.Name = "chkVarianceOnly";
        chkVarianceOnly.Size = new Size(130, 24);
        chkVarianceOnly.Text = "&Variances only";
        chkVarianceOnly.UseVisualStyleBackColor = true;

        pnlHeader.Controls.AddRange(new Control[]
            { chkVarianceOnly, lblHeader });
        pnlHeader.Dock = DockStyle.Top;
        pnlHeader.Height = 44;
        pnlHeader.Name = "pnlHeader";

        grdLines.Dock = DockStyle.Fill;
        grdLines.Name = "grdLines";
        grdLines.AddColumn("LineNo", "#", 30, rightAlign: true);
        grdLines.AddColumn("Description", "Product", 230);
        grdLines.AddColumn("Batch", "Batch", 85);
        grdLines.AddColumn("Expiry", "Expiry", 60);
        grdLines.AddColumn("Expected", "Expected", 70, "0.###", true);
        grdLines.AddColumn("Counted", "Counted", 70, "0.###", true);
        grdLines.AddColumn("Variance", "Variance", 70, "0.###", true);
        grdLines.AddColumn("Value", "Value", 75, "N2", true);

        // ---- Totals -----------------------------------------------------
        Caption(pnlTotals, 12, 12, "Progress");

        lblProgressValue.AutoSize = false;
        lblProgressValue.Font = new Font("Segoe UI", 10F);
        lblProgressValue.Location = new Point(110, 10);
        lblProgressValue.Name = "lblProgressValue";
        lblProgressValue.Size = new Size(260, 20);

        Caption(pnlTotals, 12, 40, "Reason");

        lblReasonValue.AutoSize = false;
        lblReasonValue.ForeColor = SystemColors.GrayText;
        lblReasonValue.Location = new Point(110, 38);
        lblReasonValue.Name = "lblReasonValue";
        lblReasonValue.Size = new Size(430, 20);

        Caption(pnlTotals, 560, 12, "Net variance");

        lblNetVarianceValue.AutoSize = false;
        lblNetVarianceValue.Font = new Font("Consolas", 13F, FontStyle.Bold);
        lblNetVarianceValue.Location = new Point(680, 8);
        lblNetVarianceValue.Name = "lblNetVarianceValue";
        lblNetVarianceValue.Size = new Size(150, 24);
        lblNetVarianceValue.Text = "0.00";
        lblNetVarianceValue.TextAlign = ContentAlignment.MiddleRight;

        // Shown separately from the net figure, because a count that is
        // over on one item and short on another nets to something that
        // hides the shortage the accountant cares about.
        Caption(pnlTotals, 560, 42, "Shrinkage");

        lblShrinkageValue.AutoSize = false;
        lblShrinkageValue.Font = new Font("Consolas", 11F);
        lblShrinkageValue.Location = new Point(680, 38);
        lblShrinkageValue.Name = "lblShrinkageValue";
        lblShrinkageValue.Size = new Size(150, 22);
        lblShrinkageValue.Text = "0.00";
        lblShrinkageValue.TextAlign = ContentAlignment.MiddleRight;

        pnlTotals.Controls.AddRange(new Control[]
        {
            lblProgressValue, lblReasonValue, lblNetVarianceValue,
            lblShrinkageValue
        });
        pnlTotals.Dock = DockStyle.Bottom;
        pnlTotals.Height = 68;
        pnlTotals.Name = "pnlTotals";

        btnCount.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        btnCount.Location = new Point(12, 8);
        btnCount.Size = new Size(150, 32);
        btnCount.Text = "&Count line (Enter)";
        btnCount.UseVisualStyleBackColor = true;

        btnClearCount.Location = new Point(168, 8);
        btnClearCount.Size = new Size(130, 32);
        btnClearCount.Text = "C&lear count";
        btnClearCount.UseVisualStyleBackColor = true;

        btnPost.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        btnPost.Location = new Point(304, 8);
        btnPost.Size = new Size(170, 32);
        btnPost.Text = "&Post count";
        btnPost.UseVisualStyleBackColor = true;

        btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnClose.Location = new Point(730, 8);
        btnClose.Size = new Size(110, 32);
        btnClose.Text = "&Close";
        btnClose.UseVisualStyleBackColor = true;

        pnlButtons.Controls.AddRange(new Control[]
            { btnClose, btnPost, btnClearCount, btnCount });
        pnlButtons.Dock = DockStyle.Bottom;
        pnlButtons.Height = 50;
        pnlButtons.Name = "pnlButtons";

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(860, 620);
        Controls.Add(grdLines);
        Controls.Add(pnlHeader);
        Controls.Add(pnlTotals);
        Controls.Add(pnlButtons);
        KeyPreview = true;
        Name = "frmStocktakeCapture";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Stocktake capture";
        ResumeLayout(false);
        PerformLayout();
    }

    private static void Caption(Control parent, int x, int y, string text) =>
        parent.Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(x, y),
            Text = text
        });
}
