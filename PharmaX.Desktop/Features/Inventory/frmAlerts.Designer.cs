using PharmaX.Application.Alerts;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Inventory;

partial class frmAlerts
{
    private System.ComponentModel.IContainer components = null!;
    private Panel pnlTop = null!;
    private Panel pnlBottom = null!;
    private Label lblShow = null!;
    private Label lblSummary = null!;
    private ComboBox cboFilter = null!;
    private PhxGrid grdAlerts = null!;
    private Button btnProduct = null!;
    private Button btnRefresh = null!;
    private Button btnDelete = null!;
    private Button btnClose = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _poll.Dispose();
            components?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        pnlTop = new Panel();
        pnlBottom = new Panel();
        lblShow = new Label();
        lblSummary = new Label();
        cboFilter = new ComboBox();
        grdAlerts = new PhxGrid();
        btnProduct = new Button();
        btnProduct.Name = "btnProduct";
        btnRefresh = new Button();
        btnRefresh.Name = "btnRefresh";
        btnDelete = new Button();
        btnDelete.Name = "btnDelete";
        btnClose = new Button();
        btnClose.Name = "btnClose";
        SuspendLayout();

        lblShow.AutoSize = true;
        lblShow.Location = new Point(12, 17);
        lblShow.Text = "&Show";

        cboFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        cboFilter.Location = new Point(70, 13);
        cboFilter.Name = "cboFilter";
        cboFilter.Size = new Size(200, 23);
        cboFilter.TabIndex = 0;

        // Tag carries the kind, so the list reads in English and the
        // presenter still gets an enum.
        cboFilter.Items.AddRange(new object[]
        {
            new Choice("Everything", null),
            new Choice("Expired", AlertKind.Expired),
            new Choice("Expiring soon", AlertKind.ExpiringSoon),
            new Choice("Out of stock", AlertKind.OutOfStock),
            new Choice("Low stock", AlertKind.LowStock),
            new Choice("Dispensed with no batch or expiry", AlertKind.DispensedWithoutExpiry)
        });

        cboFilter.SelectedIndex = 0;

        lblSummary.AutoSize = false;
        lblSummary.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        lblSummary.Font = PhxTheme.Subheading;
        lblSummary.Location = new Point(300, 13);
        lblSummary.Size = new Size(560, 26);
        lblSummary.TextAlign = ContentAlignment.MiddleRight;

        pnlTop.Controls.AddRange(new Control[]
            { lblSummary, cboFilter, lblShow });
        pnlTop.Dock = DockStyle.Top;
        pnlTop.Height = 52;
        pnlTop.Name = "pnlTop";

        grdAlerts.AutoGenerateColumns = false;
        grdAlerts.Dock = DockStyle.Fill;
        // Several at once, so a run of expired lines is deleted together.
        grdAlerts.MultiSelect = true;
        grdAlerts.Name = "grdAlerts";
        grdAlerts.RowTemplate.Height = 28;

        Bind(grdAlerts, "What", "What", 95);
        Bind(grdAlerts, "ProductCode", "Code", 70);
        Bind(grdAlerts, "ProductName", "Product", 250);
        Bind(grdAlerts, "BatchNumber", "Batch", 95);
        Bind(grdAlerts, "ExpiryText", "Expiry", 85);
        Bind(grdAlerts, "Summary", "Detail", 220);

        btnProduct.Location = new Point(12, 8);
        btnProduct.Size = new Size(150, 34);
        btnProduct.Text = "Open &Product";
        btnProduct.UseVisualStyleBackColor = true;
        PhxTheme.MakePrimary(btnProduct);
        pnlBottom.Controls.Add(btnProduct);

        btnRefresh.Location = new Point(170, 8);
        btnRefresh.Size = new Size(110, 34);
        btnRefresh.Text = "&Refresh";
        btnRefresh.UseVisualStyleBackColor = true;
        pnlBottom.Controls.Add(btnRefresh);

        // Offered only to those who may clear dead stock (AllowDelete).
        btnDelete.Location = new Point(288, 8);
        btnDelete.Size = new Size(110, 34);
        btnDelete.Text = "&Delete";
        btnDelete.UseVisualStyleBackColor = true;
        btnDelete.Visible = false;
        pnlBottom.Controls.Add(btnDelete);

        btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnClose.Location = new Point(760, 8);
        btnClose.Size = new Size(110, 34);
        btnClose.Text = "&Close";
        btnClose.UseVisualStyleBackColor = true;
        pnlBottom.Controls.Add(btnClose);

        pnlBottom.Dock = DockStyle.Bottom;
        pnlBottom.Height = 52;
        pnlBottom.Name = "pnlBottom";

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = btnClose;
        ClientSize = new Size(890, 560);
        Controls.Add(grdAlerts);
        Controls.Add(pnlTop);
        Controls.Add(pnlBottom);
        Name = "frmAlerts";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Stock alerts";
        ResumeLayout(false);
        PerformLayout();
    }

    /// One entry in the filter list: what it says, and what it means.
    private sealed record Choice(string Text, AlertKind? Kind)
    {
        public override string ToString() => Text;
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
