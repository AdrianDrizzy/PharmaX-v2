using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Tills;

partial class frmTillSession
{
    private System.ComponentModel.IContainer components = null!;

    private Panel pnlHeader = null!;
    private Panel pnlButtons = null!;
    private Panel pnlCount = null!;
    private Panel pnlCountFooter = null!;
    private Panel pnlStatement = null!;
    private Panel pnlReads = null!;
    private Panel pnlExpected = null!;
    private Panel pnlVariance = null!;

    private Label lblTitle = null!;
    private Label lblSessionState = null!;
    private Label lblCountCaption = null!;
    private Label lblCountHint = null!;
    private Label lblDeclared = null!;
    private Label lblDeclaredValue = null!;
    private Label lblEcoCash = null!;
    private MoneyTextBox txtEcoCash = null!;
    private Label lblExpectedCaption = null!;
    private Label lblVarianceCaption = null!;

    private DataGridView grdDenominations = null!;
    private PhxGrid grdExpected = null!;
    private PhxGrid grdVariance = null!;

    private Button btnOpen = null!;
    private Button btnXRead = null!;
    private Button btnDeclare = null!;
    private Button btnCloseSession = null!;
    private Button btnClose = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        pnlHeader = PhxTheme.HeaderBand(66);
        pnlButtons = PhxTheme.FooterBand(56);
        pnlCount = new Panel();
        pnlCountFooter = new Panel();
        pnlStatement = new Panel();
        pnlReads = new Panel();
        pnlExpected = new Panel();
        pnlVariance = new Panel();
        lblTitle = new Label();
        lblSessionState = new Label();
        lblCountCaption = new Label();
        lblCountHint = new Label();
        lblDeclared = new Label();
        lblDeclaredValue = new Label();
        lblEcoCash = new Label();
        txtEcoCash = new MoneyTextBox();
        lblExpectedCaption = new Label();
        lblVarianceCaption = new Label();
        grdDenominations = new DataGridView();
        grdExpected = new PhxGrid();
        grdVariance = new PhxGrid();
        btnOpen = new Button();
        btnOpen.Name = "btnOpen";
        btnXRead = new Button();
        btnXRead.Name = "btnXRead";
        btnDeclare = new Button();
        btnDeclare.Name = "btnDeclare";
        btnCloseSession = new Button();
        btnCloseSession.Name = "btnCloseSession";
        btnClose = new Button();
        btnClose.Name = "btnClose";
        ((System.ComponentModel.ISupportInitialize)grdDenominations).BeginInit();
        SuspendLayout();

        // ---- Header ------------------------------------------------------
        lblTitle.AutoSize = false;
        lblTitle.Font = PhxTheme.Heading;
        lblTitle.Location = new Point(PhxTheme.Gutter, 8);
        lblTitle.Name = "lblTitle";
        lblTitle.Size = new Size(500, 26);
        lblTitle.Text = "Till session";

        lblSessionState.AutoSize = false;
        lblSessionState.ForeColor = PhxTheme.Muted;
        lblSessionState.Location = new Point(PhxTheme.Gutter + 2, 36);
        lblSessionState.Name = "lblSessionState";
        lblSessionState.Size = new Size(940, 20);

        pnlHeader.Controls.Add(lblSessionState);
        pnlHeader.Controls.Add(lblTitle);

        // ---- Counting the drawer, on the left ----------------------------
        lblCountCaption.Dock = DockStyle.Top;
        lblCountCaption.Font = PhxTheme.Subheading;
        lblCountCaption.Height = 26;
        lblCountCaption.Padding = new Padding(2, 4, 0, 0);
        lblCountCaption.Text = "Count the drawer";

        lblCountHint.Dock = DockStyle.Top;
        lblCountHint.ForeColor = PhxTheme.Muted;
        lblCountHint.Height = 22;
        lblCountHint.Padding = new Padding(2, 0, 0, 4);
        lblCountHint.Text =
            "Type how many of each note and coin you have counted.";

        // A plain grid, not a PhxGrid: this is the one grid in PharmaX
        // the operator types into. The theme still styles it, which is
        // what stops the default grey showing under the last row.
        grdDenominations.AllowUserToAddRows = false;
        grdDenominations.AllowUserToDeleteRows = false;
        grdDenominations.AutoSizeColumnsMode =
            DataGridViewAutoSizeColumnsMode.Fill;

        // Docked to the top and resized to its rows once they arrive,
        // rather than filling: a cash-up grid has eleven rows and will
        // never have twelve, so filling only buys a tall empty box
        // between the last coin and the total underneath it.
        grdDenominations.Dock = DockStyle.Top;
        grdDenominations.Height = 344;
        grdDenominations.Name = "grdDenominations";
        grdDenominations.ScrollBars = ScrollBars.Vertical;
        grdDenominations.SelectionMode = DataGridViewSelectionMode.CellSelect;
        grdDenominations.RowTemplate.Height = 28;
        PhxTheme.ApplyToGrid(grdDenominations);

        AddColumn(grdDenominations, "Denomination", "Note or coin", 90,
                  readOnly: true);
        AddColumn(grdDenominations, "Value", "Value", 70, "N2", true, true);
        AddColumn(grdDenominations, "Count", "Count", 70, null, true);
        AddColumn(grdDenominations, "Total", "Total", 90, "N2", true, true);

        grdDenominations.CellEndEdit += OnDenominationEdited;

        // The counted total sits directly under the column it totals,
        // which is where a person counting looks for it.
        // Docked rather than positioned, so the figure stays hard
        // against the right edge of the Total column at any width.
        lblDeclared.Dock = DockStyle.Left;
        lblDeclared.Font = PhxTheme.Subheading;
        lblDeclared.Name = "lblDeclared";
        lblDeclared.Padding = new Padding(8, 0, 0, 0);
        lblDeclared.Text = "Counted in drawer";
        lblDeclared.TextAlign = ContentAlignment.MiddleLeft;
        lblDeclared.Width = 220;

        lblDeclaredValue.Dock = DockStyle.Fill;
        lblDeclaredValue.Font = PhxTheme.FigureLarge;
        lblDeclaredValue.Name = "lblDeclaredValue";
        lblDeclaredValue.Padding = new Padding(0, 0, 10, 0);
        lblDeclaredValue.Text = "0.00";
        lblDeclaredValue.TextAlign = ContentAlignment.MiddleRight;

        pnlCountFooter.BackColor = PhxTheme.Band;
        pnlCountFooter.Controls.Add(lblDeclaredValue);
        pnlCountFooter.Controls.Add(lblDeclared);
        pnlCountFooter.Dock = DockStyle.Top;
        pnlCountFooter.Height = 52;
        pnlCountFooter.Name = "pnlCountFooter";

        // EcoCash is not in the drawer, so it cannot be counted with the
        // notes. It is declared from the merchant statement on the phone
        // - what EcoCash itself says it received for this till - which is
        // the only figure that catches a payment the customer never
        // actually sent.
        lblEcoCash.AutoSize = false;
        lblEcoCash.Location = new Point(8, 10);
        lblEcoCash.Name = "lblEcoCash";
        lblEcoCash.Size = new Size(220, 24);
        lblEcoCash.Text = "&EcoCash per statement";
        lblEcoCash.TextAlign = ContentAlignment.MiddleLeft;

        txtEcoCash.AccessibleName = "EcoCash per statement";
        txtEcoCash.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        txtEcoCash.Location = new Point(240, 8);
        txtEcoCash.Name = "txtEcoCash";
        txtEcoCash.PlaceholderText = "From the merchant statement";
        txtEcoCash.Size = new Size(200, 26);

        pnlStatement.Controls.Add(txtEcoCash);
        pnlStatement.Controls.Add(lblEcoCash);
        pnlStatement.Dock = DockStyle.Top;
        pnlStatement.Height = 44;
        pnlStatement.Name = "pnlStatement";

        // Added bottom-up: docking is resolved from the highest child
        // index down, so the caption added last is the one that ends up
        // at the top.
        pnlCount.Controls.Add(pnlStatement);
        pnlCount.Controls.Add(pnlCountFooter);
        pnlCount.Controls.Add(grdDenominations);
        pnlCount.Controls.Add(lblCountHint);
        pnlCount.Controls.Add(lblCountCaption);
        pnlCount.Dock = DockStyle.Fill;
        pnlCount.Name = "pnlCount";
        pnlCount.Padding = new Padding(PhxTheme.Gutter, 8, 6, PhxTheme.Gutter);

        // ---- Reads, on the right -----------------------------------------
        lblExpectedCaption.Dock = DockStyle.Top;
        lblExpectedCaption.Font = PhxTheme.Subheading;
        lblExpectedCaption.Height = 26;
        lblExpectedCaption.Padding = new Padding(2, 4, 0, 0);
        lblExpectedCaption.Text = "Expected by tender";

        grdExpected.Dock = DockStyle.Fill;
        grdExpected.Name = "grdExpected";
        grdExpected.AddColumn("Tender", "Tender", 110);
        grdExpected.AddColumn("Expected", "Expected", 100, "N2", true);
        grdExpected.AddColumn("Count", "Items", 60, rightAlign: true);

        pnlExpected.Controls.Add(grdExpected);
        pnlExpected.Controls.Add(lblExpectedCaption);
        pnlExpected.Dock = DockStyle.Top;
        pnlExpected.Height = 170;
        pnlExpected.Name = "pnlExpected";

        lblVarianceCaption.Dock = DockStyle.Top;
        lblVarianceCaption.Font = PhxTheme.Subheading;
        lblVarianceCaption.Height = 26;
        lblVarianceCaption.Padding = new Padding(2, 8, 0, 0);
        lblVarianceCaption.Text = "Variance";

        grdVariance.Dock = DockStyle.Fill;
        grdVariance.Name = "grdVariance";
        grdVariance.AddColumn("Tender", "Tender", 100);
        grdVariance.AddColumn("Expected", "Expected", 90, "N2", true);
        grdVariance.AddColumn("Declared", "Declared", 90, "N2", true);
        grdVariance.AddColumn("Variance", "Variance", 90, "N2", true);

        // Added last so the filling grid ends up beneath the headers.
        pnlVariance.Controls.Add(grdVariance);
        pnlVariance.Controls.Add(lblVarianceCaption);
        pnlVariance.Dock = DockStyle.Fill;
        pnlVariance.Name = "pnlVariance";

        // A fixed-width right column rather than a splitter: the grids
        // in it have known columns, and a splitter left at its default
        // is what truncated those headers to "Expecte" and "Transac".
        pnlReads.Controls.Add(pnlVariance);
        pnlReads.Controls.Add(pnlExpected);
        pnlReads.Dock = DockStyle.Right;
        pnlReads.Name = "pnlReads";
        pnlReads.Padding = new Padding(6, 8, PhxTheme.Gutter, PhxTheme.Gutter);
        pnlReads.Width = 420;

        // ---- Actions ------------------------------------------------------
        pnlButtons.Padding = new Padding(PhxTheme.Gutter, 11,
                                         PhxTheme.Gutter, 11);

        var x = PhxTheme.Gutter;

        foreach (var (button, text, width) in new[]
        {
            (btnOpen, "&Open session", 130),
            (btnXRead, "&X-read  Ctrl+X", 130),
            (btnDeclare, "&Declare", 110),
            (btnCloseSession, "Cash up and close  Ctrl+&Z", 200)
        })
        {
            button.Location = new Point(x, 11);
            button.Size = new Size(width, 34);
            button.Text = text;
            button.UseVisualStyleBackColor = true;
            pnlButtons.Controls.Add(button);
            x += width + 8;
        }

        PhxTheme.MakePrimary(btnCloseSession);

        // Docked, not anchored. An anchored control takes its offsets
        // from the parent's size at the moment it is added, and this
        // band is still 200px wide then, which threw the button a
        // thousand pixels off the right-hand edge where nobody saw it.
        btnClose.Dock = DockStyle.Right;
        btnClose.Text = "&Close";
        btnClose.UseVisualStyleBackColor = true;
        btnClose.Width = 100;
        pnlButtons.Controls.Add(btnClose);

        // ---- Form ---------------------------------------------------------
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1124, 524);
        MinimumSize = new Size(1040, 520);

        // Fill first, then the docked edges, so the centre gets what is
        // left rather than the other way round.
        Controls.Add(pnlCount);
        Controls.Add(pnlReads);
        Controls.Add(PhxTheme.Rule(DockStyle.Top));
        Controls.Add(pnlHeader);
        Controls.Add(pnlButtons);

        KeyPreview = true;
        Name = "frmTillSession";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Till session";
        ((System.ComponentModel.ISupportInitialize)grdDenominations).EndInit();
        ResumeLayout(false);
    }

    private static void AddColumn(DataGridView grid, string name,
        string header, int fillWeight, string? format = null,
        bool rightAlign = false, bool readOnly = false)
    {
        var column = new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            FillWeight = fillWeight,
            ReadOnly = readOnly,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };

        if (format is not null) column.DefaultCellStyle.Format = format;

        if (rightAlign)
            column.DefaultCellStyle.Alignment =
                DataGridViewContentAlignment.MiddleRight;

        // The one column that is typed into gets the emphasis, so the
        // eye lands on it rather than on the figures beside it.
        if (!readOnly && format is null)
        {
            column.DefaultCellStyle.Font = PhxTheme.Emphasis;
            column.DefaultCellStyle.BackColor = Color.FromArgb(255, 253, 240);
        }

        grid.Columns.Add(column);
    }
}
