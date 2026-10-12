using PharmaX.Desktop.Controls;

namespace PharmaX.Desktop.Features.Reference;

partial class frmTariffList
{
    private System.ComponentModel.IContainer components = null!;
    private Panel pnlTop = null!;
    private Panel pnlBottom = null!;
    private Label lblScheme = null!;
    private Label lblSearch = null!;
    private Label lblCount = null!;
    private ComboBox cboScheme = null!;
    private TextBox txtSearch = null!;
    private PhxGrid grdTariffs = null!;
    private Button btnAdd = null!;
    private Button btnEdit = null!;
    private Button btnToggle = null!;
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
        lblScheme = new Label();
        lblSearch = new Label();
        lblCount = new Label();
        cboScheme = new ComboBox();
        txtSearch = new TextBox();
        grdTariffs = new PhxGrid();
        btnAdd = new Button();
        btnAdd.Name = "btnAdd";
        btnEdit = new Button();
        btnEdit.Name = "btnEdit";
        btnToggle = new Button();
        btnToggle.Name = "btnToggle";
        btnRefresh = new Button();
        btnRefresh.Name = "btnRefresh";
        btnClose = new Button();
        btnClose.Name = "btnClose";
        SuspendLayout();

        lblScheme.AutoSize = true;
        lblScheme.Location = new Point(12, 17);
        lblScheme.Text = "S&ociety";

        cboScheme.DropDownStyle = ComboBoxStyle.DropDownList;
        cboScheme.Location = new Point(75, 13);
        cboScheme.Name = "cboScheme";
        cboScheme.Size = new Size(260, 23);
        cboScheme.TabIndex = 0;

        lblSearch.AutoSize = true;
        lblSearch.Location = new Point(355, 17);
        lblSearch.Text = "&Search";

        txtSearch.Location = new Point(413, 13);
        txtSearch.Name = "txtSearch";
        txtSearch.PlaceholderText = "Product code, description or society";
        txtSearch.Size = new Size(300, 23);
        txtSearch.TabIndex = 1;

        lblCount.AutoSize = false;
        lblCount.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        lblCount.Location = new Point(760, 17);
        lblCount.Size = new Size(150, 20);
        lblCount.TextAlign = ContentAlignment.MiddleRight;

        pnlTop.Controls.AddRange(new Control[]
            { lblCount, txtSearch, lblSearch, cboScheme, lblScheme });
        pnlTop.Dock = DockStyle.Top;
        pnlTop.Height = 50;
        pnlTop.Name = "pnlTop";

        grdTariffs.AutoGenerateColumns = false;
        grdTariffs.Dock = DockStyle.Fill;
        grdTariffs.Name = "grdTariffs";

        Bind(grdTariffs, "SchemeName", "Society", 150);
        Bind(grdTariffs, "ProductCode", "Code", 70);
        Bind(grdTariffs, "ProductDescription", "Item", 240);
        Bind(grdTariffs, "Price", "Price", 80, "N2", true);
        Bind(grdTariffs, "DispensingFee", "Fee", 70, "N2", true);
        Bind(grdTariffs, "Total", "Total", 80, "N2", true);
        Bind(grdTariffs, "EffectiveFrom", "From", 90, "dd/MM/yyyy");
        Bind(grdTariffs, "EffectiveTo", "To", 90, "dd/MM/yyyy");
        Bind(grdTariffs, "IsActive", "In use", 60);

        btnAdd.Location = new Point(12, 8);
        btnAdd.Size = new Size(90, 30);
        btnAdd.TabIndex = 2;
        btnAdd.Text = "&Add";
        btnAdd.UseVisualStyleBackColor = true;

        btnEdit.Location = new Point(108, 8);
        btnEdit.Size = new Size(90, 30);
        btnEdit.TabIndex = 3;
        btnEdit.Text = "&Edit";
        btnEdit.UseVisualStyleBackColor = true;

        btnToggle.Location = new Point(204, 8);
        btnToggle.Size = new Size(110, 30);
        btnToggle.TabIndex = 4;
        btnToggle.Text = "&Retire";
        btnToggle.UseVisualStyleBackColor = true;

        btnRefresh.Location = new Point(320, 8);
        btnRefresh.Size = new Size(90, 30);
        btnRefresh.TabIndex = 5;
        btnRefresh.Text = "Re&fresh";
        btnRefresh.UseVisualStyleBackColor = true;

        btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnClose.Location = new Point(820, 8);
        btnClose.Size = new Size(90, 30);
        btnClose.TabIndex = 6;
        btnClose.Text = "&Close";
        btnClose.UseVisualStyleBackColor = true;

        pnlBottom.Controls.AddRange(new Control[]
            { btnClose, btnRefresh, btnToggle, btnEdit, btnAdd });
        pnlBottom.Dock = DockStyle.Bottom;
        pnlBottom.Height = 48;
        pnlBottom.Name = "pnlBottom";

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(930, 560);
        Controls.Add(grdTariffs);
        Controls.Add(pnlTop);
        Controls.Add(pnlBottom);
        Name = "frmTariffList";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Tariffs";
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
            SortMode = DataGridViewColumnSortMode.Automatic
        };

        if (format is not null) column.DefaultCellStyle.Format = format;

        if (rightAlign)
            column.DefaultCellStyle.Alignment =
                DataGridViewContentAlignment.MiddleRight;

        grid.Columns.Add(column);
    }
}
