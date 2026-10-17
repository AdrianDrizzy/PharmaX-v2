using PharmaX.Desktop.Controls;

namespace PharmaX.Desktop.Features.Suppliers;

partial class frmSupplierList
{
    private System.ComponentModel.IContainer components = null!;
    private Panel pnlTop = null!;
    private Panel pnlBottom = null!;
    private CheckBox chkIncludeInactive = null!;
    private Label lblCount = null!;
    private Label lblSearch = null!;
    private TextBox txtSearch = null!;
    private PhxGrid grdSuppliers = null!;
    private Button btnAdd = null!;
    private Button btnEdit = null!;
    private Button btnDeactivate = null!;
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
        chkIncludeInactive = new CheckBox();
        lblCount = new Label();
        lblSearch = new Label();
        txtSearch = new TextBox();
        grdSuppliers = new PhxGrid();
        btnAdd = new Button();
        btnAdd.Name = "btnAdd";
        btnEdit = new Button();
        btnEdit.Name = "btnEdit";
        btnDeactivate = new Button();
        btnDeactivate.Name = "btnDeactivate";
        btnChoose = new Button();
        btnChoose.Name = "btnChoose";
        btnClose = new Button();
        btnClose.Name = "btnClose";
        SuspendLayout();

        lblSearch.AutoSize = true;
        lblSearch.Location = new Point(12, 17);
        lblSearch.Text = "&Search";

        // A few words of the name, code, contact or telephone, in any order.
        txtSearch.Location = new Point(70, 13);
        txtSearch.Name = "txtSearch";
        txtSearch.PlaceholderText = "Name, code, contact or telephone";
        txtSearch.Size = new Size(300, 23);
        txtSearch.TabIndex = 0;

        chkIncludeInactive.AutoSize = true;
        chkIncludeInactive.Location = new Point(390, 16);
        chkIncludeInactive.Name = "chkIncludeInactive";
        chkIncludeInactive.TabIndex = 1;
        chkIncludeInactive.Text = "Show &deactivated suppliers";
        chkIncludeInactive.UseVisualStyleBackColor = true;

        lblCount.AutoSize = false;
        lblCount.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        lblCount.Location = new Point(640, 16);
        lblCount.Size = new Size(150, 20);
        lblCount.TextAlign = ContentAlignment.MiddleRight;

        pnlTop.Controls.AddRange(new Control[]
            { lblCount, chkIncludeInactive, txtSearch, lblSearch });
        pnlTop.Dock = DockStyle.Top;
        pnlTop.Height = 46;
        pnlTop.Name = "pnlTop";

        grdSuppliers.AutoGenerateColumns = false;
        grdSuppliers.Dock = DockStyle.Fill;
        grdSuppliers.Name = "grdSuppliers";

        Bind(grdSuppliers, "Code", "Code", 70);
        Bind(grdSuppliers, "Name", "Name", 200);
        Bind(grdSuppliers, "ContactName", "Contact", 130);
        Bind(grdSuppliers, "Telephone", "Telephone", 110);
        Bind(grdSuppliers, "TermsDays", "Terms", 50, rightAlign: true);
        Bind(grdSuppliers, "LeadTimeDays", "Lead", 45, rightAlign: true);
        Bind(grdSuppliers, "IsActive", "Active", 50);

        var x = 12;
        foreach (var (button, text) in new[]
        {
            (btnAdd, "&Add"), (btnEdit, "&Edit"),
            (btnDeactivate, "Deac&tivate")
        })
        {
            button.Location = new Point(x, 8);
            button.Size = new Size(110, 30);
            button.Text = text;
            button.UseVisualStyleBackColor = true;
            pnlBottom.Controls.Add(button);
            x += 116;
        }

        btnChoose.Location = new Point(x, 8);
        btnChoose.Size = new Size(110, 30);
        btnChoose.Text = "C&hoose";
        btnChoose.UseVisualStyleBackColor = true;
        btnChoose.Visible = false;
        pnlBottom.Controls.Add(btnChoose);

        btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnClose.Location = new Point(680, 8);
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
        ClientSize = new Size(810, 480);
        Controls.Add(grdSuppliers);
        Controls.Add(pnlTop);
        Controls.Add(pnlBottom);
        Name = "frmSupplierList";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Suppliers";
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
