using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Dispensing;

partial class frmDispensaryQuote
{
    private System.ComponentModel.IContainer components = null!;
    private Panel pnlHeader = null!;
    private Panel pnlEntry = null!;
    private Panel pnlFooter = null!;
    private Label lblTitle = null!;
    private Label lblNumber = null!;
    private Label lblCustomer = null!;
    private TextBox txtCustomer = null!;
    private Label lblPatient = null!;
    private ScanBar scanBar = null!;
    private Button btnFind = null!;
    private PhxGrid grdLines = null!;
    private Label lblTotalCaption = null!;
    private Label lblTotalValue = null!;
    private Label lblValid = null!;
    private Button btnRemove = null!;
    private Button btnSave = null!;
    private Button btnResume = null!;
    private Button btnPrint = null!;
    private Button btnCreateScript = null!;
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
        pnlEntry = new Panel();
        pnlFooter = new Panel();
        lblTitle = new Label();
        lblNumber = new Label();
        lblCustomer = new Label();
        txtCustomer = new TextBox();
        lblPatient = new Label();
        scanBar = new ScanBar();
        btnFind = new Button();
        grdLines = new PhxGrid();
        lblTotalCaption = new Label();
        lblTotalValue = new Label();
        lblValid = new Label();
        btnRemove = new Button();
        btnSave = new Button();
        btnResume = new Button();
        btnPrint = new Button();
        btnCreateScript = new Button();
        btnClose = new Button();
        SuspendLayout();

        lblTitle.AutoSize = true;
        lblTitle.Font = PhxTheme.Heading;
        lblTitle.Location = new Point(PhxTheme.Gutter, 10);
        lblTitle.Text = "Dispensary quote";

        lblNumber.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        lblNumber.AutoSize = false;
        lblNumber.Font = PhxTheme.Emphasis;
        lblNumber.Location = new Point(700, 14);
        lblNumber.Name = "lblNumber";
        lblNumber.Size = new Size(260, 24);
        lblNumber.TextAlign = ContentAlignment.MiddleRight;

        lblCustomer.AutoSize = true;
        lblCustomer.Location = new Point(PhxTheme.Gutter, 52);
        lblCustomer.Text = "&Customer";

        // Optional: the price is what was asked for, and a name is given,
        // if at all, once the customer decides to go ahead.
        txtCustomer.Location = new Point(90, 48);
        txtCustomer.MaxLength = 200;
        txtCustomer.Name = "txtCustomer";
        txtCustomer.PlaceholderText = "Name, if they have given one (optional)";
        txtCustomer.Size = new Size(320, 23);

        lblPatient.AutoSize = false;
        lblPatient.AutoEllipsis = true;
        lblPatient.Location = new Point(420, 52);
        lblPatient.Name = "lblPatient";
        lblPatient.Size = new Size(540, 20);
        lblPatient.Anchor = AnchorStyles.Top | AnchorStyles.Left
                            | AnchorStyles.Right;

        pnlHeader.BackColor = PhxTheme.Band;
        pnlHeader.Controls.AddRange(new Control[]
            { lblPatient, txtCustomer, lblCustomer, lblNumber, lblTitle });
        pnlHeader.Dock = DockStyle.Top;
        pnlHeader.Height = 86;
        pnlHeader.Name = "pnlHeader";

        scanBar.Dock = DockStyle.Fill;
        scanBar.Name = "scanBar";
        scanBar.Prompt = "Scan / name";

        btnFind.Dock = DockStyle.Right;
        btnFind.Name = "btnFind";
        btnFind.Text = "&Find product";
        btnFind.UseVisualStyleBackColor = true;
        btnFind.Width = 130;

        pnlEntry.Controls.Add(scanBar);
        pnlEntry.Controls.Add(btnFind);
        pnlEntry.Dock = DockStyle.Top;
        pnlEntry.Height = 52;
        pnlEntry.Name = "pnlEntry";

        grdLines.AutoGenerateColumns = false;
        grdLines.Dock = DockStyle.Fill;
        grdLines.Name = "grdLines";

        // The quantity is counted the way the script will count it -
        // tablets, millilitres - and priced as the pack's share of the
        // shelf price, as a dispensing is.
        Bind(grdLines, "LineNo", "#", 36, rightAlign: true);
        Bind(grdLines, "ProductCode", "Code", 90);
        Bind(grdLines, "Description", "Item", 320);
        Bind(grdLines, "Quantity", "Qty", 70, "0.###", true);
        Bind(grdLines, "UnitsInPack", "Pack of", 70, "0.###", true);
        Bind(grdLines, "UnitPrice", "Pack price", 90, "N2", true);
        Bind(grdLines, "LineTotal", "Price", 100, "N2", true);
        Bind(grdLines, "OnHand", "On hand", 80, "0.###", true);

        lblTotalCaption.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        lblTotalCaption.AutoSize = false;
        lblTotalCaption.Font = PhxTheme.Emphasis;
        lblTotalCaption.Location = new Point(600, 12);
        lblTotalCaption.Size = new Size(120, 26);
        lblTotalCaption.Text = "TOTAL";
        lblTotalCaption.TextAlign = ContentAlignment.MiddleRight;

        lblTotalValue.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        lblTotalValue.AutoSize = false;
        lblTotalValue.Font = PhxTheme.FigureLarge;
        lblTotalValue.Location = new Point(726, 8);
        lblTotalValue.Name = "lblTotalValue";
        lblTotalValue.Size = new Size(234, 34);
        lblTotalValue.TextAlign = ContentAlignment.MiddleRight;

        lblValid.AutoSize = false;
        lblValid.Location = new Point(PhxTheme.Gutter, 12);
        lblValid.Name = "lblValid";
        lblValid.Size = new Size(580, 56);

        btnRemove.Location = new Point(PhxTheme.Gutter, 76);
        btnRemove.Name = "btnRemove";
        btnRemove.Size = new Size(130, 30);
        btnRemove.Text = "Re&move line";
        btnRemove.UseVisualStyleBackColor = true;

        // Written with their keys: a save and a script are both "F6"
        // words, and the one that is not the everyday answer here - the
        // save - keeps the quote key.
        btnSave.Location = new Point(150, 76);
        btnSave.Name = "btnSave";
        btnSave.Size = new Size(130, 30);
        btnSave.Text = "F8 &Save quote";
        btnSave.UseVisualStyleBackColor = true;

        // A quote saved earlier, for the customer who has come back.
        btnResume.Location = new Point(286, 76);
        btnResume.Name = "btnResume";
        btnResume.Size = new Size(140, 30);
        btnResume.Text = "F9 &Resume quote";
        btnResume.UseVisualStyleBackColor = true;

        btnPrint.Location = new Point(432, 76);
        btnPrint.Name = "btnPrint";
        btnPrint.Size = new Size(100, 30);
        btnPrint.Text = "&Print";
        btnPrint.UseVisualStyleBackColor = true;

        // The customer goes ahead: the patient's details, then the
        // script, captured from what was quoted.
        btnCreateScript.Location = new Point(538, 76);
        btnCreateScript.Name = "btnCreateScript";
        btnCreateScript.Size = new Size(150, 30);
        btnCreateScript.Text = "F6 &Create script";
        btnCreateScript.UseVisualStyleBackColor = true;

        btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnClose.Location = new Point(870, 76);
        btnClose.Name = "btnClose";
        btnClose.Size = new Size(90, 30);
        btnClose.Text = "&Close";
        btnClose.UseVisualStyleBackColor = true;

        pnlFooter.BackColor = PhxTheme.Band;
        pnlFooter.Controls.AddRange(new Control[]
        {
            btnClose, btnCreateScript, btnPrint, btnResume, btnSave,
            btnRemove, lblValid, lblTotalValue, lblTotalCaption
        });
        pnlFooter.Dock = DockStyle.Bottom;
        pnlFooter.Height = 118;
        pnlFooter.Name = "pnlFooter";

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(980, 600);
        Controls.Add(grdLines);
        Controls.Add(pnlEntry);
        Controls.Add(pnlHeader);
        Controls.Add(pnlFooter);
        KeyPreview = true;
        Name = "frmDispensaryQuote";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Dispensary quote";
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
