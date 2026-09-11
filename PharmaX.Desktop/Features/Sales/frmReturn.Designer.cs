using PharmaX.Desktop.Controls;

namespace PharmaX.Desktop.Features.Sales;

partial class frmReturn
{
    private System.ComponentModel.IContainer components = null!;
    private Panel pnlHeader = null!;
    private Panel pnlTotals = null!;
    private Panel pnlButtons = null!;
    private Label lblSaleValue = null!;
    private Label lblKindHint = null!;
    private Label lblCreditValue = null!;
    private TextBox txtDocumentNumber = null!;
    private ComboBox cboKind = null!;
    private DataGridView grdLines = null!;
    private Button btnFind = null!;
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
        lblSaleValue = new Label();
        lblKindHint = new Label();
        lblCreditValue = new Label();
        txtDocumentNumber = new TextBox();
        cboKind = new ComboBox();
        grdLines = new DataGridView();
        btnFind = new Button();
        btnPost = new Button();
        btnClose = new Button();
        ((System.ComponentModel.ISupportInitialize)grdLines).BeginInit();
        SuspendLayout();

        Caption(pnlHeader, 12, 16, "Receipt number");

        txtDocumentNumber.Font = new Font("Consolas", 11F);
        txtDocumentNumber.Location = new Point(130, 12);
        txtDocumentNumber.MaxLength = 30;
        txtDocumentNumber.Name = "txtDocumentNumber";
        txtDocumentNumber.PlaceholderText = "S-001-000001";
        txtDocumentNumber.Size = new Size(170, 25);
        txtDocumentNumber.TabIndex = 0;

        btnFind.Location = new Point(308, 11);
        btnFind.Size = new Size(100, 27);
        btnFind.TabIndex = 1;
        btnFind.Text = "&Find";
        btnFind.UseVisualStyleBackColor = true;

        Caption(pnlHeader, 440, 16, "Kind");

        cboKind.DropDownStyle = ComboBoxStyle.DropDownList;
        cboKind.Location = new Point(490, 12);
        cboKind.Name = "cboKind";
        cboKind.Size = new Size(140, 23);
        cboKind.TabIndex = 2;

        lblKindHint.AutoSize = false;
        lblKindHint.ForeColor = SystemColors.GrayText;
        lblKindHint.Location = new Point(640, 15);
        lblKindHint.Name = "lblKindHint";
        lblKindHint.Size = new Size(300, 34);

        lblSaleValue.AutoSize = false;
        lblSaleValue.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        lblSaleValue.Location = new Point(12, 48);
        lblSaleValue.Name = "lblSaleValue";
        lblSaleValue.Size = new Size(620, 22);
        lblSaleValue.Text = "(no sale found yet)";

        pnlHeader.Controls.AddRange(new Control[]
        {
            txtDocumentNumber, btnFind, cboKind, lblKindHint, lblSaleValue
        });
        pnlHeader.Dock = DockStyle.Top;
        pnlHeader.Height = 78;
        pnlHeader.Name = "pnlHeader";

        grdLines.AllowUserToAddRows = false;
        grdLines.AllowUserToResizeRows = false;
        grdLines.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grdLines.Dock = DockStyle.Fill;
        grdLines.Name = "grdLines";
        grdLines.RowHeadersVisible = false;
        grdLines.RowTemplate.Height = 24;
        grdLines.SelectionMode = DataGridViewSelectionMode.CellSelect;

        Column(grdLines, "LineNo", "#", 30, readOnly: true, rightAlign: true);
        Column(grdLines, "Description", "Product", 200, readOnly: true);
        Column(grdLines, "Sold", "Sold", 55, "0.###", true, readOnly: true);
        Column(grdLines, "Returned", "Returned", 65, "0.###", true,
               readOnly: true);
        Column(grdLines, "Returnable", "Can return", 70, "0.###", true,
               readOnly: true);

        // The one editable quantity: how many are actually coming back.
        Column(grdLines, "Return", "Returning", 70, "0.###", true);

        Column(grdLines, "UnitPrice", "Unit paid", 70, "N2", true,
               readOnly: true);

        grdLines.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Restock", HeaderText = "Back on shelf", FillWeight = 75,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        Column(grdLines, "Reason", "Line note", 130);

        // ---- Totals -----------------------------------------------------
        Caption(pnlTotals, 560, 14, "Credit due");

        lblCreditValue.AutoSize = false;
        lblCreditValue.Font = new Font("Consolas", 15F, FontStyle.Bold);
        lblCreditValue.Location = new Point(680, 8);
        lblCreditValue.Name = "lblCreditValue";
        lblCreditValue.Size = new Size(190, 30);
        lblCreditValue.Text = "0.00";
        lblCreditValue.TextAlign = ContentAlignment.MiddleRight;

        Caption(pnlTotals, 12, 14,
            "Expired batches can be credited but never restocked.");

        pnlTotals.Controls.Add(lblCreditValue);
        pnlTotals.Dock = DockStyle.Bottom;
        pnlTotals.Height = 46;
        pnlTotals.Name = "pnlTotals";

        btnPost.Enabled = false;
        btnPost.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        btnPost.Location = new Point(12, 8);
        btnPost.Size = new Size(210, 32);
        btnPost.TabIndex = 3;
        btnPost.Text = "&Refund and credit";
        btnPost.UseVisualStyleBackColor = true;

        btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnClose.Location = new Point(830, 8);
        btnClose.Size = new Size(110, 32);
        btnClose.TabIndex = 4;
        btnClose.Text = "&Close";
        btnClose.UseVisualStyleBackColor = true;

        pnlButtons.Controls.AddRange(new Control[] { btnClose, btnPost });
        pnlButtons.Dock = DockStyle.Bottom;
        pnlButtons.Height = 50;
        pnlButtons.Name = "pnlButtons";

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(964, 560);
        Controls.Add(grdLines);
        Controls.Add(pnlHeader);
        Controls.Add(pnlTotals);
        Controls.Add(pnlButtons);
        Name = "frmReturn";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Returns and credit notes";
        ((System.ComponentModel.ISupportInitialize)grdLines).EndInit();
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

    private static void Column(DataGridView grid, string name, string header,
        int fillWeight, string? format = null, bool rightAlign = false,
        bool readOnly = false)
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

        grid.Columns.Add(column);
    }
}
