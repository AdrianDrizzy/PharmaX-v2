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
    private Button btnAdd = null!;
    private Button btnEdit = null!;
    private Button btnRefresh = null!;
    private Button btnClose = null!;

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
        btnAdd = new Button();
        btnEdit = new Button();
        btnRefresh = new Button();
        btnClose = new Button();
        SuspendLayout();

        lblSearch.AutoSize = true;
        lblSearch.Location = new Point(12, 17);
        lblSearch.Text = "&Search";

        txtSearch.Location = new Point(70, 13);
        txtSearch.Name = "txtSearch";
        txtSearch.PlaceholderText = "Code or description";
        txtSearch.Size = new Size(360, 23);
        txtSearch.TabIndex = 0;

        lblCount.AutoSize = false;
        lblCount.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        lblCount.Location = new Point(660, 17);
        lblCount.Size = new Size(160, 20);
        lblCount.TextAlign = ContentAlignment.MiddleRight;

        pnlTop.Controls.AddRange(new Control[]
            { lblCount, txtSearch, lblSearch });
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
        Bind(grdProducts, "IsBatchTracked", "Batch", 50);
        Bind(grdProducts, "IsActive", "Active", 50);

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

        btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnClose.Location = new Point(730, 8);
        btnClose.Size = new Size(90, 30);
        btnClose.TabIndex = 4;
        btnClose.Text = "&Close";
        btnClose.UseVisualStyleBackColor = true;

        pnlBottom.Controls.AddRange(new Control[]
            { btnClose, btnRefresh, btnEdit, btnAdd });
        pnlBottom.Dock = DockStyle.Bottom;
        pnlBottom.Height = 48;
        pnlBottom.Name = "pnlBottom";

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(840, 560);
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
