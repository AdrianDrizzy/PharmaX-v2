using PharmaX.Desktop.Controls;

namespace PharmaX.Desktop.Features.Inventory;

partial class frmProductList
{
    private System.ComponentModel.IContainer components = null!;
    private Panel pnlTop = null!;
    private Panel pnlBottom = null!;
    private Label lblSearch = null!;
    private Label lblCount = null!;
    private TextBox txtSearch = null!;
    private PhxGrid grdProducts = null!;
    private PhxGrid grdBatches = null!;
    private CheckBox chkBatches = null!;
    private Button btnAdd = null!;
    private Button btnEdit = null!;
    private Button btnRefresh = null!;
    private Button btnChoose = null!;
    private Button btnExport = null!;
    private Button btnClose = null!;
    private Button btnPrevious = null!;
    private Button btnNext = null!;
    private Label lblPage = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _debounce.Dispose();
            components?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        pnlTop = new Panel();
        pnlBottom = new Panel();
        lblSearch = new Label();
        lblCount = new Label();
        txtSearch = new TextBox();
        grdProducts = new PhxGrid();
        grdBatches = new PhxGrid();
        chkBatches = new CheckBox();
        chkBatches.Name = "chkBatches";
        btnAdd = new Button();
        btnAdd.Name = "btnAdd";
        btnEdit = new Button();
        btnEdit.Name = "btnEdit";
        btnRefresh = new Button();
        btnRefresh.Name = "btnRefresh";
        btnChoose = new Button();
        btnChoose.Name = "btnChoose";
        btnExport = new Button();
        btnExport.Name = "btnExport";
        btnClose = new Button();
        btnClose.Name = "btnClose";
        btnPrevious = new Button();
        btnPrevious.Name = "btnPrevious";
        btnNext = new Button();
        btnNext.Name = "btnNext";
        lblPage = new Label();
        lblPage.Name = "lblPage";
        SuspendLayout();

        lblSearch.AutoSize = true;
        lblSearch.Location = new Point(12, 17);
        lblSearch.Text = "&Search";

        txtSearch.Location = new Point(70, 13);
        txtSearch.Name = "txtSearch";
        txtSearch.PlaceholderText = "Code, description or generic name";
        txtSearch.Size = new Size(360, 23);
        txtSearch.TabIndex = 0;

        lblCount.AutoSize = false;
        lblCount.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        lblCount.Location = new Point(620, 17);
        lblCount.Size = new Size(200, 20);
        lblCount.TextAlign = ContentAlignment.MiddleRight;

        // Off by default: the product list is what this screen is for,
        // and most of the shelf is not batch tracked.
        chkBatches.AutoSize = true;
        chkBatches.Location = new Point(446, 15);
        chkBatches.TabIndex = 1;
        chkBatches.Text = "Show &batches and expiry";
        chkBatches.UseVisualStyleBackColor = true;

        pnlTop.Controls.AddRange(new Control[]
            { lblCount, chkBatches, txtSearch, lblSearch });
        pnlTop.Dock = DockStyle.Top;
        pnlTop.Height = 50;
        pnlTop.Name = "pnlTop";

        grdProducts.AutoGenerateColumns = false;
        grdProducts.Dock = DockStyle.Fill;
        grdProducts.Name = "grdProducts";

        Bind(grdProducts, "Code", "Code", 70);
        Bind(grdProducts, "Description", "Description", 300);
        Bind(grdProducts, "RetailPrice", "Retail", 80, "N2", true);
        Bind(grdProducts, "QuantityOnHand", "On hand", 80, "0.###", true);
        Bind(grdProducts, "Schedule", "Sch", 40, rightAlign: true);
        Bind(grdProducts, "BatchTracked", "Batch", 50);
        Bind(grdProducts, "Active", "Active", 50);

        // The same shelf, a batch at a time. A second grid rather than
        // rebinding the first: the two have different columns, and a grid
        // that swaps its column set on a tick is where the sorting, the
        // widths and the selected row all quietly come apart.
        grdBatches.AutoGenerateColumns = false;
        grdBatches.Dock = DockStyle.Fill;
        grdBatches.Name = "grdBatches";
        grdBatches.Visible = false;

        Bind(grdBatches, "Code", "Code", 70);
        Bind(grdBatches, "Description", "Description", 260);
        Bind(grdBatches, "BatchNumber", "Batch", 110);
        Bind(grdBatches, "Expiry", "Expires", 90);
        Bind(grdBatches, "DaysLeft", "Days left", 70, rightAlign: true);
        Bind(grdBatches, "QuantityOnHand", "On hand", 80, "0.###", true);

        btnAdd.Location = new Point(12, 8);
        btnAdd.Size = new Size(90, 30);
        btnAdd.TabIndex = 1;
        btnAdd.Text = "&Add";
        btnAdd.UseVisualStyleBackColor = true;

        btnEdit.Location = new Point(108, 8);
        btnEdit.Size = new Size(90, 30);
        btnEdit.TabIndex = 2;
        btnEdit.Text = "&Edit";
        btnEdit.UseVisualStyleBackColor = true;

        btnRefresh.Location = new Point(204, 8);
        btnRefresh.Size = new Size(90, 30);
        btnRefresh.TabIndex = 3;
        btnRefresh.Text = "&Refresh";
        btnRefresh.UseVisualStyleBackColor = true;

        // Hidden until UseAsPicker shows it, so the maintenance screen
        // does not offer a "choose" that would have nobody to answer to.
        btnChoose.Location = new Point(300, 8);
        btnChoose.Size = new Size(110, 30);
        btnChoose.TabIndex = 4;
        btnChoose.Text = "C&hoose";
        btnChoose.UseVisualStyleBackColor = true;
        btnChoose.Visible = false;

        // In Choose's place on the maintenance screen; UseAsPicker swaps
        // them over.
        btnExport.Location = new Point(300, 8);
        btnExport.Size = new Size(110, 30);
        btnExport.TabIndex = 4;
        btnExport.Text = "Ex&port";
        btnExport.UseVisualStyleBackColor = true;

        btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnClose.Location = new Point(730, 8);
        btnClose.Size = new Size(90, 30);
        btnClose.TabIndex = 5;
        btnClose.Text = "&Close";
        btnClose.UseVisualStyleBackColor = true;

        // The pages either side, after the other buttons. Not anchored
        // right like Close: PhxTheme seats every right-anchored control
        // that starts off the edge at the same spot, so three of them
        // would land on top of each other.
        btnPrevious.Enabled = false;
        btnPrevious.Location = new Point(432, 8);
        btnPrevious.Size = new Size(86, 30);
        btnPrevious.TabIndex = 6;
        btnPrevious.Text = "Pre&vious";
        btnPrevious.UseVisualStyleBackColor = true;

        lblPage.AutoSize = false;
        lblPage.Location = new Point(522, 8);
        lblPage.Size = new Size(110, 30);
        lblPage.TextAlign = ContentAlignment.MiddleCenter;

        btnNext.Enabled = false;
        btnNext.Location = new Point(636, 8);
        btnNext.Size = new Size(86, 30);
        btnNext.TabIndex = 7;
        btnNext.Text = "Ne&xt";
        btnNext.UseVisualStyleBackColor = true;

        pnlBottom.Controls.AddRange(new Control[]
            { btnClose, btnNext, lblPage, btnPrevious, btnExport, btnChoose, btnRefresh,
              btnEdit, btnAdd });
        pnlBottom.Dock = DockStyle.Bottom;
        pnlBottom.Height = 48;
        pnlBottom.Name = "pnlBottom";

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(840, 560);
        Controls.Add(grdBatches);
        Controls.Add(grdProducts);
        Controls.Add(pnlTop);
        Controls.Add(pnlBottom);
        Name = "frmProductList";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Products";
        ResumeLayout(false);
        PerformLayout();
    }

    /// <summary>
    /// A bound column. Separate from PhxGrid.AddColumn because this grid
    /// binds to a DTO list, so each column needs its property name.
    /// </summary>
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
