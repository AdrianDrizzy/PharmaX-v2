using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Sales;

partial class frmQuotation
{
    private System.ComponentModel.IContainer components = null!;
    private Panel pnlHeader = null!;
    private Panel pnlEntry = null!;
    private Panel pnlFooter = null!;
    private Label lblTitle = null!;
    private Label lblNumber = null!;
    private Label lblCustomer = null!;
    private TextBox txtCustomer = null!;
    private Button btnStart = null!;
    private ScanBar scanBar = null!;
    private Button btnFind = null!;
    private PhxGrid grdLines = null!;
    private Label lblTotalCaption = null!;
    private Label lblTotalValue = null!;
    private Label lblValid = null!;
    private Button btnRemove = null!;
    private Button btnIssue = null!;
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
        btnStart = new Button();
        scanBar = new ScanBar();
        btnFind = new Button();
        grdLines = new PhxGrid();
        lblTotalCaption = new Label();
        lblTotalValue = new Label();
        lblValid = new Label();
        btnRemove = new Button();
        btnIssue = new Button();
        btnClose = new Button();
        btnClose.Name = "btnClose";
        SuspendLayout();

        lblTitle.AutoSize = true;
        lblTitle.Font = PhxTheme.Heading;
        lblTitle.Location = new Point(PhxTheme.Gutter, 10);
        lblTitle.Text = "Quotation";

        lblNumber.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        lblNumber.AutoSize = false;
        lblNumber.Font = PhxTheme.Emphasis;
        lblNumber.Location = new Point(700, 14);
        lblNumber.Size = new Size(260, 24);
        lblNumber.TextAlign = ContentAlignment.MiddleRight;

        lblCustomer.AutoSize = true;
        lblCustomer.Location = new Point(PhxTheme.Gutter, 52);
        lblCustomer.Text = "&Customer";

        txtCustomer.Location = new Point(90, 48);
        txtCustomer.MaxLength = 200;
        txtCustomer.Name = "txtCustomer";
        txtCustomer.PlaceholderText = "Who the quotation is addressed to";
        txtCustomer.Size = new Size(380, 23);

        btnStart.Location = new Point(480, 47);
        btnStart.Name = "btnStart";
        btnStart.Size = new Size(130, 26);
        btnStart.Text = "&Start quotation";
        btnStart.UseVisualStyleBackColor = true;

        pnlHeader.BackColor = PhxTheme.Band;
        pnlHeader.Controls.AddRange(new Control[]
            { btnStart, txtCustomer, lblCustomer, lblNumber, lblTitle });
        pnlHeader.Dock = DockStyle.Top;
        pnlHeader.Height = 86;
        pnlHeader.Name = "pnlHeader";

        scanBar.Dock = DockStyle.Fill;
        scanBar.Name = "scanBar";
        scanBar.Prompt = "Scan / name";

        btnFind.Dock = DockStyle.Right;
        btnFind.Name = "btnFind";
        btnFind.Text = "F&ind item";
        btnFind.UseVisualStyleBackColor = true;
        btnFind.Width = 110;

        pnlEntry.Controls.Add(scanBar);
        pnlEntry.Controls.Add(btnFind);
        pnlEntry.Dock = DockStyle.Top;
        pnlEntry.Height = 52;
        pnlEntry.Name = "pnlEntry";

        grdLines.AutoGenerateColumns = false;
        grdLines.Dock = DockStyle.Fill;
        grdLines.Name = "grdLines";

        Bind(grdLines, "LineNo", "#", 40, rightAlign: true);
        Bind(grdLines, "Description", "Item", 380);
        Bind(grdLines, "Quantity", "Qty", 80, "N2", true);
        Bind(grdLines, "UnitPrice", "Unit", 100, "N2", true);
        Bind(grdLines, "TaxCode", "Tax", 50);
        Bind(grdLines, "LineTotal", "Total", 110, "N2", true);

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
        lblValid.Location = new Point(PhxTheme.Gutter, 48);
        lblValid.Name = "lblValid";
        lblValid.Size = new Size(560, 22);

        btnRemove.Location = new Point(PhxTheme.Gutter, 76);
        btnRemove.Name = "btnRemove";
        btnRemove.Size = new Size(130, 30);
        btnRemove.Text = "&Remove line";
        btnRemove.UseVisualStyleBackColor = true;

        btnIssue.Location = new Point(150, 76);
        btnIssue.Name = "btnIssue";
        btnIssue.Size = new Size(150, 30);
        btnIssue.Text = "Iss&ue quotation";
        btnIssue.UseVisualStyleBackColor = true;

        btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnClose.Location = new Point(870, 76);
        btnClose.Size = new Size(90, 30);
        btnClose.Text = "&Close";
        btnClose.UseVisualStyleBackColor = true;

        pnlFooter.BackColor = PhxTheme.Band;
        pnlFooter.Controls.AddRange(new Control[]
        {
            btnClose, btnIssue, btnRemove, lblValid, lblTotalValue,
            lblTotalCaption
        });
        pnlFooter.Dock = DockStyle.Bottom;
        pnlFooter.Height = 118;
        pnlFooter.Name = "pnlFooter";

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(980, 640);
        Controls.Add(grdLines);
        Controls.Add(pnlEntry);
        Controls.Add(pnlHeader);
        Controls.Add(pnlFooter);
        KeyPreview = true;
        Name = "frmQuotation";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Quotation";
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
