using PharmaX.Desktop.Controls;

namespace PharmaX.Desktop.Features.Suppliers;

partial class frmPurchaseOrderList
{
    private System.ComponentModel.IContainer components = null!;
    private Panel pnlTop = null!;
    private Panel pnlBottom = null!;
    private SplitContainer splMain = null!;
    private Label lblCount = null!;
    private Label lblOrdersCaption = null!;
    private Label lblLinesCaption = null!;
    private PhxGrid grdOrders = null!;
    private PhxGrid grdLines = null!;
    private Button btnRefresh = null!;
    private Button btnSuggest = null!;
    private Button btnCancelOrder = null!;
    private Button btnChoose = null!;
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
        pnlBottom = new Panel();
        splMain = new SplitContainer();
        lblCount = new Label();
        lblOrdersCaption = new Label();
        lblLinesCaption = new Label();
        grdOrders = new PhxGrid();
        grdLines = new PhxGrid();
        btnRefresh = new Button();
        btnSuggest = new Button();
        btnCancelOrder = new Button();
        btnChoose = new Button();
        btnClose = new Button();
        ((System.ComponentModel.ISupportInitialize)splMain).BeginInit();
        splMain.Panel1.SuspendLayout();
        splMain.Panel2.SuspendLayout();
        splMain.SuspendLayout();
        SuspendLayout();

        lblCount.AutoSize = false;
        lblCount.Font = new Font("Segoe UI", 10F);
        lblCount.Location = new Point(12, 14);
        lblCount.Name = "lblCount";
        lblCount.Size = new Size(400, 20);

        pnlTop.Controls.Add(lblCount);
        pnlTop.Dock = DockStyle.Top;
        pnlTop.Height = 42;
        pnlTop.Name = "pnlTop";

        lblOrdersCaption.Dock = DockStyle.Top;
        lblOrdersCaption.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        lblOrdersCaption.Height = 24;
        lblOrdersCaption.Text = "Orders still expecting stock";

        grdOrders.AutoGenerateColumns = false;
        grdOrders.Dock = DockStyle.Fill;
        grdOrders.Name = "grdOrders";

        Bind(grdOrders, "OrderNumber", "Order", 90);
        Bind(grdOrders, "SupplierName", "Supplier", 170);
        Bind(grdOrders, "OrderedOn", "Ordered", 85, "dd/MM/yyyy");
        Bind(grdOrders, "ExpectedOn", "Expected", 85, "dd/MM/yyyy");
        Bind(grdOrders, "Status", "Status", 110);
        Bind(grdOrders, "Total", "Total", 85, "N2", true);

        splMain.Panel1.Controls.Add(grdOrders);
        splMain.Panel1.Controls.Add(lblOrdersCaption);

        lblLinesCaption.Dock = DockStyle.Top;
        lblLinesCaption.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        lblLinesCaption.Height = 24;
        lblLinesCaption.Text = "Lines";

        grdLines.Dock = DockStyle.Fill;
        grdLines.Name = "grdLines";
        grdLines.AddColumn("LineNo", "#", 30, rightAlign: true);
        grdLines.AddColumn("Description", "Product", 200);
        grdLines.AddColumn("Ordered", "Ordered", 70, "0.###", true);
        grdLines.AddColumn("Received", "Received", 70, "0.###", true);
        grdLines.AddColumn("Outstanding", "Outstanding", 80, "0.###", true);
        grdLines.AddColumn("UnitCost", "Unit cost", 75, "N2", true);
        grdLines.AddColumn("LineTotal", "Total", 80, "N2", true);

        splMain.Panel2.Controls.Add(grdLines);
        splMain.Panel2.Controls.Add(lblLinesCaption);

        splMain.Dock = DockStyle.Fill;
        splMain.Name = "splMain";
        splMain.Orientation = Orientation.Horizontal;
        splMain.FixedPanel = FixedPanel.Panel1;

        // Applied for real in OnLoad: see SeatSplitter.
        splMain.SplitterDistance = 220;

        var x = 12;
        foreach (var (button, text) in new[]
        {
            (btnRefresh, "&Refresh"), (btnSuggest, "&What to order"),
            (btnCancelOrder, "Cance&l order")
        })
        {
            button.Location = new Point(x, 8);
            button.Size = new Size(120, 30);
            button.Text = text;
            button.UseVisualStyleBackColor = true;
            pnlBottom.Controls.Add(button);
            x += 126;
        }

        btnChoose.Location = new Point(x, 8);
        btnChoose.Size = new Size(120, 30);
        btnChoose.Text = "C&hoose";
        btnChoose.UseVisualStyleBackColor = true;
        btnChoose.Visible = false;
        pnlBottom.Controls.Add(btnChoose);

        btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnClose.Location = new Point(750, 8);
        btnClose.Size = new Size(110, 30);
        btnClose.Text = "&Close";
        btnClose.UseVisualStyleBackColor = true;
        pnlBottom.Controls.Add(btnClose);

        pnlBottom.Dock = DockStyle.Bottom;
        pnlBottom.Height = 48;
        pnlBottom.Name = "pnlBottom";

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = btnClose;
        ClientSize = new Size(880, 560);
        Controls.Add(splMain);
        Controls.Add(pnlTop);
        Controls.Add(pnlBottom);
        Name = "frmPurchaseOrderList";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Purchase orders";
        splMain.Panel1.ResumeLayout(false);
        splMain.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)splMain).EndInit();
        splMain.ResumeLayout(false);
        ResumeLayout(false);
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
            SortMode = DataGridViewColumnSortMode.Automatic
        };

        if (format is not null) column.DefaultCellStyle.Format = format;

        if (rightAlign)
            column.DefaultCellStyle.Alignment =
                DataGridViewContentAlignment.MiddleRight;

        grid.Columns.Add(column);
    }
}
