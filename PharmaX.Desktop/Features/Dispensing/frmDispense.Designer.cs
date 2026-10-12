using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Dispensing;

partial class frmDispense
{
    private System.ComponentModel.IContainer components = null!;
    private Panel pnlHeader = null!;
    private Panel pnlTotals = null!;
    private Panel pnlButtons = null!;
    private TableLayoutPanel tblTotals = null!;
    private SplitContainer splMain = null!;
    private ScanBar scanBar = null!;
    private PhxGrid grdItems = null!;
    private ListBox lstWarnings = null!;
    private Label lblPatient = null!;
    private Label lblAllergies = null!;
    private Label lblConditions = null!;
    private Label lblScript = null!;
    private Label lblWarningsCaption = null!;
    private Label lblTariffValue = null!;
    private Label lblContributionValue = null!;
    private Label lblCoPaymentValue = null!;
    private Button btnFindScript = null!;
    private Button btnQuote = null!;
    private Button btnIssue = null!;
    private Button btnReprint = null!;
    private Button btnRemove = null!;
    private Button btnClose = null!;
    private Button btnSuspended = null!;

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
        tblTotals = new TableLayoutPanel();
        splMain = new SplitContainer();
        scanBar = new ScanBar();
        grdItems = new PhxGrid();
        lstWarnings = new ListBox();
        lblPatient = new Label();
        lblAllergies = new Label();
        lblConditions = new Label();
        lblScript = new Label();
        lblWarningsCaption = new Label();
        btnFindScript = new Button();
        btnFindScript.Name = "btnFindScript";
        btnQuote = new Button();
        btnQuote.Name = "btnQuote";
        btnIssue = new Button();
        btnIssue.Name = "btnIssue";
        btnReprint = new Button();
        btnReprint.Name = "btnReprint";
        btnRemove = new Button();
        btnRemove.Name = "btnRemove";
        btnClose = new Button();
        btnClose.Name = "btnClose";
        btnSuspended = new Button();
        btnSuspended.Name = "btnSuspended";
        ((System.ComponentModel.ISupportInitialize)splMain).BeginInit();
        splMain.Panel1.SuspendLayout();
        splMain.Panel2.SuspendLayout();
        splMain.SuspendLayout();
        SuspendLayout();

        lblPatient.AutoSize = false;
        lblPatient.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
        lblPatient.Location = new Point(12, 10);
        lblPatient.Name = "lblPatient";
        lblPatient.Size = new Size(600, 26);
        lblPatient.Text = "No patient selected";

        lblAllergies.AutoSize = false;
        lblAllergies.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        lblAllergies.Location = new Point(12, 40);
        lblAllergies.Name = "lblAllergies";
        lblAllergies.Size = new Size(600, 22);

        // Beside the allergies: the chronic conditions a pharmacist checks
        // a new item against, which were loaded and never shown.
        lblConditions.AutoSize = false;
        lblConditions.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        lblConditions.ForeColor = PhxTheme.Title;
        lblConditions.Location = new Point(620, 40);
        lblConditions.Name = "lblConditions";
        lblConditions.Size = new Size(600, 22);

        lblScript.AutoSize = false;
        lblScript.Location = new Point(12, 66);
        lblScript.Name = "lblScript";
        lblScript.Size = new Size(800, 22);

        pnlHeader.Controls.AddRange(new Control[]
            { lblScript, lblConditions, lblAllergies, lblPatient });
        pnlHeader.Dock = DockStyle.Top;
        pnlHeader.Height = 94;
        pnlHeader.Name = "pnlHeader";

        scanBar.Dock = DockStyle.Top;
        scanBar.Height = 48;
        scanBar.Name = "scanBar";
        scanBar.Prompt = "Product";

        grdItems.Dock = DockStyle.Fill;
        grdItems.Name = "grdItems";
        grdItems.AddColumn("LineNo", "#", 30, rightAlign: true);
        grdItems.AddColumn("Description", "Item", 220);
        grdItems.AddColumn("Qty", "Qty", 60, "0.###", true);
        grdItems.AddColumn("Directions", "Directions", 260);
        grdItems.AddColumn("Batch", "Batch", 90);
        grdItems.AddColumn("Tariff", "Tariff", 80, "N2", true);
        grdItems.AddColumn("Contribution", "Scheme", 80, "N2", true);
        grdItems.AddColumn("CoPayment", "Co-pay", 80, "N2", true);

        lblWarningsCaption.Dock = DockStyle.Top;
        lblWarningsCaption.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        lblWarningsCaption.Height = 24;
        lblWarningsCaption.Text = "Clinical warnings";

        lstWarnings.Dock = DockStyle.Fill;
        lstWarnings.IntegralHeight = false;
        lstWarnings.Name = "lstWarnings";

        splMain.Dock = DockStyle.Fill;
        splMain.Name = "splMain";
        splMain.Orientation = Orientation.Horizontal;
        splMain.Panel1.Controls.Add(grdItems);
        splMain.Panel2.Controls.Add(lstWarnings);
        splMain.Panel2.Controls.Add(lblWarningsCaption);
        splMain.FixedPanel = FixedPanel.Panel2;



        // Applied for real in OnLoad: see SeatSplitter.

        splMain.SplitterDistance = 340;

        // A table docked to the right, rather than labels anchored to
        // it. Anchoring resolved against a 200px-wide panel and put
        // every figure on this screen off the side of the window.
        tblTotals.ColumnCount = 2;
        tblTotals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54F));
        tblTotals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46F));
        tblTotals.Dock = DockStyle.Right;
        tblTotals.Name = "tblTotals";
        tblTotals.RowCount = 3;
        tblTotals.Width = 420;

        foreach (var height in new[] { 24, 24, 32 })
            tblTotals.RowStyles.Add(new RowStyle(SizeType.Absolute, height));

        AddTotal(tblTotals, "Tariff", 0, out lblTariffValue);
        AddTotal(tblTotals, "Scheme pays", 1, out lblContributionValue);
        AddTotal(tblTotals, "Patient pays", 2, out lblCoPaymentValue,
                 emphasise: true);

        pnlTotals.BackColor = PhxTheme.Band;
        pnlTotals.Controls.Add(tblTotals);
        pnlTotals.Dock = DockStyle.Bottom;
        pnlTotals.Height = 96;
        pnlTotals.Name = "pnlTotals";
        pnlTotals.Padding = new Padding(PhxTheme.Gutter, 8,
                                        PhxTheme.Gutter, 8);

        btnFindScript.Location = new Point(12, 8);
        btnFindScript.Size = new Size(140, 32);
        btnFindScript.Text = "&Find script";
        btnFindScript.UseVisualStyleBackColor = true;

        btnIssue.Location = new Point(158, 8);
        btnIssue.Size = new Size(140, 32);
        btnIssue.Text = "&Issue";
        btnIssue.UseVisualStyleBackColor = true;

        btnReprint.Location = new Point(304, 8);
        btnReprint.Size = new Size(140, 32);
        btnReprint.Text = "&Reprint labels";
        btnReprint.UseVisualStyleBackColor = true;

        // Takes an item back off before the issue: a repeat where the
        // patient wants only part of the script, or an item scanned by
        // mistake.
        btnRemove.Location = new Point(450, 8);
        btnRemove.Size = new Size(140, 32);
        btnRemove.Text = "Re&move item";
        btnRemove.UseVisualStyleBackColor = true;

        // What it will cost, for a customer who has not decided: priced,
        // saved, and dispensed here if they go ahead.
        btnQuote.Location = new Point(596, 8);
        btnQuote.Size = new Size(140, 32);
        btnQuote.Text = "&Quote";
        btnQuote.UseVisualStyleBackColor = true;

        // Scripts kept to finish later, picked up where they were left.
        btnSuspended.Location = new Point(742, 8);
        btnSuspended.Size = new Size(140, 32);
        btnSuspended.Text = "S&uspended";
        btnSuspended.UseVisualStyleBackColor = true;

        btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnClose.Location = new Point(910, 8);
        btnClose.Size = new Size(100, 32);
        btnClose.Text = "&Close";
        btnClose.UseVisualStyleBackColor = true;

        pnlButtons.Controls.AddRange(new Control[]
            { btnClose, btnSuspended, btnQuote, btnRemove, btnReprint, btnIssue, btnFindScript });
        pnlButtons.Dock = DockStyle.Bottom;
        pnlButtons.Height = 50;
        pnlButtons.Name = "pnlButtons";

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1024, 720);
        Controls.Add(splMain);
        Controls.Add(scanBar);
        Controls.Add(pnlHeader);
        Controls.Add(pnlTotals);
        Controls.Add(pnlButtons);
        KeyPreview = true;
        Name = "frmDispense";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "PharmaX - Dispensing";
        WindowState = FormWindowState.Maximized;
        splMain.Panel1.ResumeLayout(false);
        splMain.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)splMain).EndInit();
        splMain.ResumeLayout(false);
        ResumeLayout(false);
    }

    /// <summary>
    /// One caption and one figure, both right-aligned, as a row of the
    /// totals table.
    /// </summary>
    private static void AddTotal(TableLayoutPanel table, string caption,
        int row, out Label value, bool emphasise = false)
    {
        table.Controls.Add(new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = emphasise ? PhxTheme.Subheading : PhxTheme.Body,
            Padding = new Padding(0, 0, 12, 0),
            Text = caption,
            TextAlign = ContentAlignment.MiddleRight
        }, 0, row);

        value = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = emphasise ? PhxTheme.FigureLarge : PhxTheme.Figure,
            ForeColor = emphasise ? PhxTheme.Title : SystemColors.ControlText,
            Text = emphasise ? Format.Money(0m) : Format.Amount(0m),
            TextAlign = ContentAlignment.MiddleRight
        };

        table.Controls.Add(value, 1, row);
    }
}
