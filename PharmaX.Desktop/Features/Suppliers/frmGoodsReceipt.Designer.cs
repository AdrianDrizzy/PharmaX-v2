using PharmaX.Desktop.Controls;

namespace PharmaX.Desktop.Features.Suppliers;

partial class frmGoodsReceipt
{
    private System.ComponentModel.IContainer components = null!;
    private Panel pnlHeader = null!;
    private Panel pnlTotals = null!;
    private Panel pnlButtons = null!;
    private SplitContainer splMain = null!;
    private Label lblSupplierValue = null!;
    private Label lblOrderValue = null!;
    private Label lblCapturedValue = null!;
    private Label lblVarianceValue = null!;
    private Label lblCaptureCaption = null!;
    private Label lblPreviewCaption = null!;
    private TextBox txtInvoiceNumber = null!;
    private MoneyTextBox txtInvoiceTotal = null!;
    private DateTimePicker dtpReceivedOn = null!;
    private DataGridView grdCapture = null!;
    private PhxGrid grdPreview = null!;
    private Button btnChooseSupplier = null!;
    private Button btnChooseOrder = null!;
    private Button btnAddLine = null!;
    private Button btnRemoveLine = null!;
    private Button btnPost = null!;
    private Button btnClose = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private Label lblProblem = null!;

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        pnlHeader = new Panel();
        pnlTotals = new Panel();
        pnlButtons = new Panel();
        splMain = new SplitContainer();
        lblSupplierValue = new Label();
        lblOrderValue = new Label();
        lblCapturedValue = new Label();
        lblVarianceValue = new Label();
        lblCaptureCaption = new Label();
        lblPreviewCaption = new Label();
        txtInvoiceNumber = new TextBox();
        txtInvoiceTotal = new MoneyTextBox();
        dtpReceivedOn = new DateTimePicker();
        grdCapture = new DataGridView();
        grdPreview = new PhxGrid();
        btnChooseSupplier = new Button();
        btnChooseSupplier.Name = "btnChooseSupplier";
        btnChooseOrder = new Button();
        btnChooseOrder.Name = "btnChooseOrder";
        btnAddLine = new Button();
        btnAddLine.Name = "btnAddLine";
        btnRemoveLine = new Button();
        btnRemoveLine.Name = "btnRemoveLine";
        btnPost = new Button();
        btnPost.Name = "btnPost";
        btnClose = new Button();
        btnClose.Name = "btnClose";
        ((System.ComponentModel.ISupportInitialize)grdCapture).BeginInit();
        ((System.ComponentModel.ISupportInitialize)splMain).BeginInit();
        splMain.Panel1.SuspendLayout();
        splMain.Panel2.SuspendLayout();
        splMain.SuspendLayout();
        SuspendLayout();

        // ---- Header -----------------------------------------------------
        Caption(pnlHeader, 12, 16, "Supplier");

        lblSupplierValue.AutoSize = false;
        lblSupplierValue.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        lblSupplierValue.Location = new Point(110, 14);
        lblSupplierValue.Name = "lblSupplierValue";
        lblSupplierValue.Size = new Size(330, 22);
        lblSupplierValue.Text = "(none chosen)";

        btnChooseSupplier.Location = new Point(446, 12);
        btnChooseSupplier.Size = new Size(110, 26);
        btnChooseSupplier.Text = "&Supplier...";
        btnChooseSupplier.UseVisualStyleBackColor = true;

        Caption(pnlHeader, 12, 48, "Order");

        lblOrderValue.AutoSize = false;
        lblOrderValue.Location = new Point(110, 46);
        lblOrderValue.Name = "lblOrderValue";
        lblOrderValue.Size = new Size(330, 22);
        lblOrderValue.Text = "(none)";

        btnChooseOrder.Location = new Point(446, 44);
        btnChooseOrder.Size = new Size(110, 26);
        btnChooseOrder.Text = "&Order...";
        btnChooseOrder.UseVisualStyleBackColor = true;

        Caption(pnlHeader, 580, 16, "Invoice no.");

        txtInvoiceNumber.Location = new Point(670, 12);
        txtInvoiceNumber.MaxLength = 50;
        txtInvoiceNumber.Name = "txtInvoiceNumber";
        txtInvoiceNumber.Size = new Size(160, 23);

        Caption(pnlHeader, 580, 48, "Received on");

        dtpReceivedOn.Format = DateTimePickerFormat.Short;
        dtpReceivedOn.Location = new Point(670, 44);
        dtpReceivedOn.MaxDate = DateTime.Today;
        dtpReceivedOn.Name = "dtpReceivedOn";
        dtpReceivedOn.Size = new Size(160, 23);

        pnlHeader.Controls.AddRange(new Control[]
        {
            lblSupplierValue, btnChooseSupplier, lblOrderValue,
            btnChooseOrder, txtInvoiceNumber, dtpReceivedOn
        });
        pnlHeader.Dock = DockStyle.Top;
        pnlHeader.Height = 80;
        pnlHeader.Name = "pnlHeader";

        // ---- Capture grid -----------------------------------------------
        lblCaptureCaption.Dock = DockStyle.Top;
        lblCaptureCaption.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        lblCaptureCaption.Height = 24;
        lblCaptureCaption.Text =
            "Delivery note - expiry as MM/yyyy, batch required for "
            + "batch-tracked items";

        grdCapture.AllowUserToAddRows = false;
        grdCapture.AllowUserToResizeRows = false;
        grdCapture.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grdCapture.Dock = DockStyle.Fill;
        grdCapture.Name = "grdCapture";
        grdCapture.RowHeadersVisible = false;
        grdCapture.RowTemplate.Height = 24;
        grdCapture.SelectionMode = DataGridViewSelectionMode.CellSelect;

        Column(grdCapture, "Code", "Product code", 80);

        // What the code resolves to, filled in by the preview - or, for a
        // line from an order, by the order itself. Never typed: the
        // description is not an identity, which is what went wrong when
        // it was put in the code column (DEF-0013).
        var product = new DataGridViewTextBoxColumn
        {
            Name = "Product", HeaderText = "Product", FillWeight = 150,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };

        product.DefaultCellStyle.ForeColor = SystemColors.GrayText;
        grdCapture.Columns.Add(product);

        Column(grdCapture, "Qty", "Qty", 60, "0.###", true);
        Column(grdCapture, "Cost", "Unit cost", 75, "N2", true);
        Column(grdCapture, "Batch", "Batch no.", 90);
        Column(grdCapture, "Expiry", "Expiry", 65);

        // Carried through from a purchase order so the receipt can close
        // the right order line off; not typed by the operator.
        var orderLine = new DataGridViewTextBoxColumn
        {
            Name = "OrderLine", HeaderText = "PO line", FillWeight = 50,
            ReadOnly = true, Visible = false,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };

        grdCapture.Columns.Add(orderLine);

        // The product an order line names, carried so the receipt posts
        // against that product and not against whatever a code resolves to.
        grdCapture.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ProductId", HeaderText = "Product id", ReadOnly = true,
            Visible = false,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        splMain.Panel1.Controls.Add(grdCapture);
        splMain.Panel1.Controls.Add(lblCaptureCaption);

        // ---- Preview grid -----------------------------------------------
        lblPreviewCaption.Dock = DockStyle.Top;
        lblPreviewCaption.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        lblPreviewCaption.Height = 24;
        lblPreviewCaption.Text = "Resolved - what will be posted";

        grdPreview.Dock = DockStyle.Fill;
        grdPreview.Name = "grdPreview";
        grdPreview.AddColumn("LineNo", "#", 30, rightAlign: true);
        grdPreview.AddColumn("Description", "Product", 200);
        grdPreview.AddColumn("Qty", "Qty", 60, "0.###", true);
        grdPreview.AddColumn("Cost", "Unit cost", 75, "N2", true);
        grdPreview.AddColumn("Total", "Total", 80, "N2", true);
        grdPreview.AddColumn("Batch", "Batch", 90);
        grdPreview.AddColumn("Expiry", "Expiry", 65);

        splMain.Panel2.Controls.Add(grdPreview);
        splMain.Panel2.Controls.Add(lblPreviewCaption);

        splMain.Dock = DockStyle.Fill;
        splMain.Name = "splMain";
        splMain.Orientation = Orientation.Horizontal;
        splMain.FixedPanel = FixedPanel.Panel1;



        // Applied for real in OnLoad: see SeatSplitter.

        splMain.SplitterDistance = 220;

        // ---- Totals -----------------------------------------------------
        Caption(pnlTotals, 470, 12, "Lines total");

        lblCapturedValue.AutoSize = false;
        lblCapturedValue.Font = new Font("Consolas", 12F, FontStyle.Bold);
        lblCapturedValue.Location = new Point(580, 8);
        lblCapturedValue.Name = "lblCapturedValue";
        lblCapturedValue.Size = new Size(120, 24);
        lblCapturedValue.Text = "0.00";
        lblCapturedValue.TextAlign = ContentAlignment.MiddleRight;

        Caption(pnlTotals, 470, 42, "Invoice total");

        txtInvoiceTotal.Location = new Point(580, 38);
        txtInvoiceTotal.Name = "txtInvoiceTotal";
        txtInvoiceTotal.Size = new Size(120, 26);

        Caption(pnlTotals, 720, 42, "Variance");

        lblVarianceValue.AutoSize = false;
        lblVarianceValue.Font = new Font("Consolas", 11F, FontStyle.Bold);
        lblVarianceValue.Location = new Point(800, 38);
        lblVarianceValue.Name = "lblVarianceValue";
        lblVarianceValue.Size = new Size(110, 24);
        lblVarianceValue.TextAlign = ContentAlignment.MiddleRight;

        btnAddLine.Location = new Point(12, 10);
        btnAddLine.Size = new Size(100, 28);
        btnAddLine.Text = "A&dd line";
        btnAddLine.UseVisualStyleBackColor = true;

        btnRemoveLine.Location = new Point(118, 10);
        btnRemoveLine.Size = new Size(100, 28);
        btnRemoveLine.Text = "Re&move line";
        btnRemoveLine.UseVisualStyleBackColor = true;

        // Why the preview could not resolve the lines, where the operator
        // is looking: the post button stayed greyed with no word of why
        // (DEF-0013).
        lblProblem = new Label
        {
            AutoSize = false,
            ForeColor = PharmaX.Desktop.Mvp.PhxTheme.ProblemText,
            Location = new Point(12, 44),
            Name = "lblProblem",
            Size = new Size(450, 30)
        };

        pnlTotals.Controls.AddRange(new Control[]
        {
            btnAddLine, btnRemoveLine, lblCapturedValue, txtInvoiceTotal,
            lblVarianceValue, lblProblem
        });
        pnlTotals.Dock = DockStyle.Bottom;
        pnlTotals.Height = 76;
        pnlTotals.Name = "pnlTotals";

        btnPost.Enabled = false;
        btnPost.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        btnPost.Location = new Point(12, 8);
        btnPost.Size = new Size(180, 32);
        btnPost.Text = "&Post receipt";
        btnPost.UseVisualStyleBackColor = true;

        btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnClose.Location = new Point(810, 8);
        btnClose.Size = new Size(110, 32);
        btnClose.Text = "&Close";
        btnClose.UseVisualStyleBackColor = true;

        pnlButtons.Controls.AddRange(new Control[] { btnClose, btnPost });
        pnlButtons.Dock = DockStyle.Bottom;
        pnlButtons.Height = 50;
        pnlButtons.Name = "pnlButtons";

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(944, 680);
        Controls.Add(splMain);
        Controls.Add(pnlHeader);
        Controls.Add(pnlTotals);
        Controls.Add(pnlButtons);
        Name = "frmGoodsReceipt";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Goods receipt";
        splMain.Panel1.ResumeLayout(false);
        splMain.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)splMain).EndInit();
        splMain.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)grdCapture).EndInit();
        ResumeLayout(false);
    }

    private static void Caption(Control parent, int x, int y, string text) =>
        parent.Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(x, y),
            Text = text
        });

    private static void Column(DataGridView grid, string name, string header,
        int fillWeight, string? format = null, bool rightAlign = false)
    {
        var column = new DataGridViewTextBoxColumn
        {
            Name = name,
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
