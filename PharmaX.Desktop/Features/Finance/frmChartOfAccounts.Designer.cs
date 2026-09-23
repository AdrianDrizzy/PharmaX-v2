using PharmaX.Desktop.Controls;

namespace PharmaX.Desktop.Features.Finance;

partial class frmChartOfAccounts
{
    private System.ComponentModel.IContainer components = null!;
    private Panel pnlTop = null!;
    private Panel pnlBottom = null!;
    private CheckBox chkInactive = null!;
    private Label lblCount = null!;
    private PhxGrid grdAccounts = null!;
    private Button btnAdd = null!;
    private Button btnRename = null!;
    private Button btnDeactivate = null!;
    private Button btnExport = null!;
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
        chkInactive = new CheckBox();
        lblCount = new Label();
        grdAccounts = new PhxGrid();
        btnAdd = new Button();
        btnAdd.Name = "btnAdd";
        btnRename = new Button();
        btnRename.Name = "btnRename";
        btnDeactivate = new Button();
        btnDeactivate.Name = "btnDeactivate";
        btnExport = new Button();
        btnExport.Name = "btnExport";
        btnClose = new Button();
        btnClose.Name = "btnClose";
        SuspendLayout();

        chkInactive.AutoSize = true;
        chkInactive.Location = new Point(12, 16);
        chkInactive.Name = "chkInactive";
        chkInactive.TabIndex = 0;
        chkInactive.Text = "Show accounts no longer in &use";

        lblCount.AutoSize = false;
        lblCount.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        lblCount.Location = new Point(380, 16);
        lblCount.Size = new Size(240, 20);
        lblCount.TextAlign = ContentAlignment.MiddleRight;

        pnlTop.Controls.AddRange(new Control[] { lblCount, chkInactive });
        pnlTop.Dock = DockStyle.Top;
        pnlTop.Height = 48;
        pnlTop.Name = "pnlTop";

        grdAccounts.AutoGenerateColumns = false;
        grdAccounts.Dock = DockStyle.Fill;
        grdAccounts.MultiSelect = false;
        grdAccounts.Name = "grdAccounts";

        Bind(grdAccounts, "Code", "Code", 70);
        Bind(grdAccounts, "Name", "Account", 260);
        Bind(grdAccounts, "TypeText", "Type", 100);
        Bind(grdAccounts, "StatusText", "Status", 90);

        var x = 12;
        foreach (var (button, text) in new[]
        {
            (btnAdd, "&Add"), (btnRename, "Re&name"),
            (btnDeactivate, "Take Out of &Use")
        })
        {
            button.Location = new Point(x, 8);
            button.Size = new Size(140, 32);
            button.Text = text;
            button.UseVisualStyleBackColor = true;
            pnlBottom.Controls.Add(button);
            x += 146;
        }

        btnExport.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnExport.Location = new Point(532, 8);
        btnExport.Size = new Size(150, 34);
        btnExport.Text = "&Export to Excel";
        btnExport.UseVisualStyleBackColor = true;
        pnlBottom.Controls.Add(btnExport);

        btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnClose.Location = new Point(690, 8);
        btnClose.Size = new Size(110, 32);
        btnClose.Text = "&Close";
        btnClose.UseVisualStyleBackColor = true;
        pnlBottom.Controls.Add(btnClose);

        pnlBottom.Dock = DockStyle.Bottom;
        pnlBottom.Height = 48;
        pnlBottom.Name = "pnlBottom";

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = btnClose;
        ClientSize = new Size(820, 500);
        Controls.Add(grdAccounts);
        Controls.Add(pnlTop);
        Controls.Add(pnlBottom);
        Name = "frmChartOfAccounts";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Chart of accounts";
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
