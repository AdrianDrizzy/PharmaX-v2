using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Sales;

partial class frmPOS
{
    private System.ComponentModel.IContainer components = null!;
    private Panel pnlHeader = null!;
    private Panel pnlHeaderRight = null!;
    private Panel pnlCustomer = null!;
    private Panel pnlTotals = null!;
    private Panel pnlFunctions = null!;
    private TableLayoutPanel tblTotals = null!;
    private ScanBar scanBar = null!;
    private PhxGrid grdLines = null!;

    private Label lblBranchTill = null!;
    private Label lblClock = null!;
    private Label lblLastDocument = null!;
    private Label lblCustomer = null!;
    private Label lblAccount = null!;
    private Label lblRated = null!;

    private Label lblSubtotalValue = null!;
    private Label lblDiscountValue = null!;
    private Label lblVatValue = null!;
    private Label lblTotalValue = null!;
    private Label lblTenderedValue = null!;
    private Label lblChangeValue = null!;

    private System.Windows.Forms.Timer tmrClock = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        pnlHeader = PhxTheme.HeaderBand(62);
        pnlHeaderRight = new Panel();
        pnlCustomer = new Panel();
        pnlTotals = new Panel();
        pnlFunctions = new Panel();
        tblTotals = new TableLayoutPanel();
        scanBar = new ScanBar();
        grdLines = new PhxGrid();
        lblBranchTill = new Label();
        lblClock = new Label();
        lblLastDocument = new Label();
        lblCustomer = new Label();
        lblAccount = new Label();
        lblRated = new Label();
        tmrClock = new System.Windows.Forms.Timer(components);
        SuspendLayout();

        // ---- Header: where this till is, and the clock -------------------
        lblBranchTill.AutoSize = false;
        lblBranchTill.Dock = DockStyle.Top;
        lblBranchTill.Font = PhxTheme.Heading;
        lblBranchTill.Height = 30;
        lblBranchTill.Name = "lblBranchTill";
        lblBranchTill.Text = "Point of Sale";

        lblClock.AutoSize = false;
        lblClock.Dock = DockStyle.Top;
        lblClock.Font = PhxTheme.Subheading;
        lblClock.Height = 26;
        lblClock.Name = "lblClock";
        lblClock.TextAlign = ContentAlignment.MiddleRight;

        lblLastDocument.AutoSize = false;
        lblLastDocument.Dock = DockStyle.Top;
        lblLastDocument.ForeColor = PhxTheme.Muted;
        lblLastDocument.Height = 20;
        lblLastDocument.Name = "lblLastDocument";
        lblLastDocument.TextAlign = ContentAlignment.MiddleRight;

        pnlHeaderRight.Controls.Add(lblLastDocument);
        pnlHeaderRight.Controls.Add(lblClock);
        pnlHeaderRight.Dock = DockStyle.Right;
        pnlHeaderRight.Name = "pnlHeaderRight";
        pnlHeaderRight.Width = 420;

        // The right-hand block is added last so that it claims the right
        // edge before the title spreads across the band. Anchoring these
        // to the right instead, against a panel that was still 200px
        // wide at the time, is what threw the clock and the document
        // number a thousand pixels off the side of the screen.
        pnlHeader.Controls.Add(lblBranchTill);
        pnlHeader.Controls.Add(pnlHeaderRight);

        // ---- Scan --------------------------------------------------------
        scanBar.Dock = DockStyle.Top;
        scanBar.Height = 52;
        scanBar.Name = "scanBar";
        scanBar.Prompt = "Scan / name";

        // ---- Who is being served -----------------------------------------
        lblCustomer.AutoSize = false;
        lblCustomer.Dock = DockStyle.Top;
        lblCustomer.Font = PhxTheme.Subheading;
        lblCustomer.Height = 26;
        lblCustomer.Name = "lblCustomer";
        lblCustomer.Text = "Cash sale";

        lblAccount.AutoSize = false;
        lblAccount.Dock = DockStyle.Top;
        lblAccount.ForeColor = PhxTheme.Muted;
        lblAccount.Height = 20;
        lblAccount.Name = "lblAccount";

        lblRated.AutoSize = false;
        lblRated.BackColor = PhxTheme.Information;
        lblRated.Dock = DockStyle.Right;
        lblRated.Font = PhxTheme.Emphasis;
        lblRated.Name = "lblRated";
        lblRated.Text = "RATED PRICING";
        lblRated.TextAlign = ContentAlignment.MiddleCenter;
        lblRated.Visible = false;
        lblRated.Width = 150;

        pnlCustomer.Controls.Add(lblAccount);
        pnlCustomer.Controls.Add(lblCustomer);
        pnlCustomer.Controls.Add(lblRated);
        pnlCustomer.Dock = DockStyle.Top;
        pnlCustomer.Height = 56;
        pnlCustomer.Name = "pnlCustomer";
        pnlCustomer.Padding = new Padding(PhxTheme.Gutter, 5,
                                          PhxTheme.Gutter, 5);

        // ---- The sale ----------------------------------------------------
        grdLines.Dock = DockStyle.Fill;
        grdLines.Name = "grdLines";
        grdLines.AddColumn("LineNo", "#", 30, rightAlign: true);
        grdLines.AddColumn("Description", "Description", 320);
        grdLines.AddColumn("Batch", "Batch", 90);
        grdLines.AddColumn("Qty", "Qty", 60, "N2", rightAlign: true);
        grdLines.AddColumn("UnitPrice", "Unit", 80, "N2", rightAlign: true);
        grdLines.AddColumn("Discount", "Disc", 70, "N2", rightAlign: true);
        grdLines.AddColumn("Tax", "Tax", 40);
        grdLines.AddColumn("LineTotal", "Total", 90, "N2", rightAlign: true);

        // ---- What it comes to --------------------------------------------
        tblTotals.ColumnCount = 2;
        tblTotals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52F));
        tblTotals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48F));
        tblTotals.Dock = DockStyle.Right;
        tblTotals.Name = "tblTotals";
        tblTotals.RowCount = 6;
        tblTotals.Width = 440;

        foreach (var height in new[] { 22, 22, 22, 34, 22, 22 })
            tblTotals.RowStyles.Add(new RowStyle(SizeType.Absolute, height));

        lblSubtotalValue = AddTotal(tblTotals, "Subtotal", 0);
        lblDiscountValue = AddTotal(tblTotals, "Discount", 1);
        lblVatValue = AddTotal(tblTotals, "VAT", 2);
        lblTotalValue = AddTotal(tblTotals, "TOTAL", 3, emphasise: true);
        lblTenderedValue = AddTotal(tblTotals, "Tendered", 4);
        lblChangeValue = AddTotal(tblTotals, "Change", 5);

        pnlTotals.BackColor = PhxTheme.Band;
        pnlTotals.Controls.Add(tblTotals);
        pnlTotals.Dock = DockStyle.Bottom;
        pnlTotals.Height = 164;
        pnlTotals.Name = "pnlTotals";
        pnlTotals.Padding = new Padding(PhxTheme.Gutter, 10,
                                        PhxTheme.Gutter, 10);

        // ---- The keys -----------------------------------------------------
        pnlFunctions.BackColor = PhxTheme.Band;
        pnlFunctions.Dock = DockStyle.Bottom;
        pnlFunctions.Height = 48;
        pnlFunctions.Name = "pnlFunctions";

        var x = PhxTheme.Gutter;

        // Buttons, not labels. These were labels once, on the reasoning
        // that the keyboard is the input device at a till and a button
        // would only invite a mouse. The reasoning was sound and the
        // result was not: a white box with a border and bold centred
        // text is a button to everyone who looks at it, so the bar
        // invited the click it then ignored.
        foreach (var (key, label) in PosKeyMap.Bar)
        {
            var button = new Button
            {
                Font = PhxTheme.Emphasis,
                Location = new Point(x, 7),
                Size = new Size(126, 34),

                // The key it stands for, so one handler serves the whole
                // bar and each button raises exactly what its key does.
                Tag = key,
                Text = label,
                UseVisualStyleBackColor = true
            };

            button.Click += OnFunctionButton;

            pnlFunctions.Controls.Add(button);
            x += 132;
        }

        // Paying is what the screen is for, so it is the one that looks
        // like it.
        PhxTheme.MakePrimary(pnlFunctions.Controls
            .OfType<Button>()
            .First(b => (Keys)b.Tag! == PosKeyMap.Payment));

        // ---- Clock --------------------------------------------------------
        tmrClock.Enabled = true;
        tmrClock.Interval = 1000;
        tmrClock.Tick += (_, _) => lblClock.Text = Format.Clock();

        // ---- Form ---------------------------------------------------------
        // Added inside-out: docking is resolved from the highest child
        // index down, so the last one added claims the outermost edge.
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1200, 800);
        Controls.Add(grdLines);
        Controls.Add(pnlTotals);
        Controls.Add(PhxTheme.Rule(DockStyle.Bottom));
        Controls.Add(pnlFunctions);
        Controls.Add(pnlCustomer);
        Controls.Add(scanBar);
        Controls.Add(PhxTheme.Rule(DockStyle.Top));
        Controls.Add(pnlHeader);
        KeyPreview = true;
        Name = "frmPOS";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "PharmaX - Point of Sale";
        WindowState = FormWindowState.Maximized;
        ResumeLayout(false);
        PerformLayout();
    }

    /// <summary>
    /// One caption and one figure, both right-aligned, as a row of the
    /// totals table. A helper rather than twelve near-identical blocks.
    /// </summary>
    private static Label AddTotal(TableLayoutPanel table, string caption,
                                  int row, bool emphasise = false)
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

        var value = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = emphasise ? PhxTheme.FigureLarge : PhxTheme.Figure,
            ForeColor = emphasise ? PhxTheme.Accent : SystemColors.ControlText,

            // Through the formatter even when it is zero, so the screen
            // does not open showing a figure in a different currency
            // from the one it will show a second later.
            Text = emphasise ? Format.Money(0m) : Format.Amount(0m),
            TextAlign = ContentAlignment.MiddleRight
        };

        table.Controls.Add(value, 1, row);
        return value;
    }
}
