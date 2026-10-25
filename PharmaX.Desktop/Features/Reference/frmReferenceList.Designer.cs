using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Reference;

partial class frmReferenceList
{
    private System.ComponentModel.IContainer components = null!;
    private Panel pnlTop = null!;
    private Panel pnlBottom = null!;
    private Label lblSearch = null!;
    private Label lblCount = null!;
    private TextBox txtSearch = null!;
    private CheckBox chkInactive = null!;
    private PhxGrid grdItems = null!;
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
        lblSearch = new Label();
        lblCount = new Label();
        txtSearch = new TextBox();
        chkInactive = new CheckBox();
        grdItems = new PhxGrid();
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

        lblSearch.AutoSize = true;
        lblSearch.Location = new Point(12, 17);
        lblSearch.Text = "&Search";

        txtSearch.Location = new Point(70, 13);
        txtSearch.Name = "txtSearch";
        txtSearch.PlaceholderText = "Code, name or details";
        txtSearch.Size = new Size(280, 23);
        txtSearch.TabIndex = 0;

        chkInactive.AutoSize = true;
        chkInactive.Location = new Point(360, 15);
        chkInactive.Name = "chkInactive";
        chkInactive.Text = "Show inactive";
        chkInactive.TabIndex = 1;

        lblCount.AutoSize = false;
        lblCount.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        lblCount.Location = new Point(560, 17);
        lblCount.Size = new Size(160, 20);
        lblCount.TextAlign = ContentAlignment.MiddleRight;

        pnlTop.Controls.AddRange(new Control[]
            { lblCount, chkInactive, txtSearch, lblSearch });
        pnlTop.Dock = DockStyle.Top;
        pnlTop.Height = 50;
        pnlTop.Name = "pnlTop";

        grdItems.AutoGenerateColumns = false;
        grdItems.Dock = DockStyle.Fill;
        grdItems.Name = "grdItems";

        Bind(grdItems, "Code", "Code", 80);
        Bind(grdItems, "Name", "Name", 280);
        Bind(grdItems, "Detail", "Detail", 260);
        Bind(grdItems, "IsActive", "In use", 60);

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

        btnToggle.Location = new Point(204, 8);
        btnToggle.Size = new Size(110, 30);
        btnToggle.TabIndex = 3;
        btnToggle.Text = "Deac&tivate";
        btnToggle.UseVisualStyleBackColor = true;

        btnRefresh.Location = new Point(320, 8);
        btnRefresh.Size = new Size(90, 30);
        btnRefresh.TabIndex = 4;
        btnRefresh.Text = "Re&fresh";
        btnRefresh.UseVisualStyleBackColor = true;

        btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnClose.Location = new Point(630, 8);
        btnClose.Size = new Size(90, 30);
        btnClose.TabIndex = 5;
        btnClose.Text = "&Close";
        btnClose.UseVisualStyleBackColor = true;

        pnlBottom.Controls.AddRange(new Control[]
            { btnClose, btnRefresh, btnToggle, btnEdit, btnAdd });
        pnlBottom.Dock = DockStyle.Bottom;
        pnlBottom.Height = 48;
        pnlBottom.Name = "pnlBottom";

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(740, 520);
        Controls.Add(grdItems);
        Controls.Add(pnlTop);
        Controls.Add(pnlBottom);
        Name = "frmReferenceList";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX";
        ResumeLayout(false);
        PerformLayout();
    }

    private static void Bind(DataGridView grid, string property,
        string header, int fillWeight)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            Name = property,
            HeaderText = header,
            FillWeight = fillWeight,
            SortMode = DataGridViewColumnSortMode.Automatic
        });
    }
}
