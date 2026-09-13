using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Finance;

partial class frmTrialBalance
{
    private System.ComponentModel.IContainer components = null!;
    private Panel pnlTop = null!;
    private Panel pnlBottom = null!;
    private Label lblAsAt = null!;
    private Label lblTotals = null!;
    private DateTimePicker dtpAsAt = null!;
    private PhxGrid grdBalance = null!;
    private Button btnEntries = null!;
    private Button btnRefresh = null!;
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
        lblAsAt = new Label();
        lblTotals = new Label();
        dtpAsAt = new DateTimePicker();
        grdBalance = new PhxGrid();
        btnEntries = new Button();
        btnRefresh = new Button();
        btnClose = new Button();
        SuspendLayout();

        lblAsAt.AutoSize = true;
        lblAsAt.Location = new Point(12, 17);
        lblAsAt.Text = "&As at";

        dtpAsAt.Format = DateTimePickerFormat.Custom;
        dtpAsAt.CustomFormat = "dd/MM/yyyy";
        dtpAsAt.Location = new Point(70, 13);
        dtpAsAt.Name = "dtpAsAt";
        dtpAsAt.Size = new Size(130, 23);
        dtpAsAt.TabIndex = 0;

        lblTotals.AutoSize = false;
        lblTotals.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        lblTotals.Font = PhxTheme.Subheading;
        lblTotals.Location = new Point(240, 13);
        lblTotals.Size = new Size(560, 26);
        lblTotals.TextAlign = ContentAlignment.MiddleRight;

        pnlTop.Controls.AddRange(new Control[]
            { lblTotals, dtpAsAt, lblAsAt });
        pnlTop.Dock = DockStyle.Top;
        pnlTop.Height = 52;
        pnlTop.Name = "pnlTop";

        grdBalance.AutoGenerateColumns = false;
        grdBalance.Dock = DockStyle.Fill;
        grdBalance.MultiSelect = false;
        grdBalance.Name = "grdBalance";

        Bind(grdBalance, "Code", "Code", 70);
        Bind(grdBalance, "Name", "Account", 250);
        Bind(grdBalance, "TypeText", "Type", 90);
        Bind(grdBalance, "DebitText", "Debit", 110, rightAlign: true);
        Bind(grdBalance, "CreditText", "Credit", 110, rightAlign: true);

        btnEntries.Location = new Point(12, 8);
        btnEntries.Size = new Size(150, 34);
        btnEntries.Text = "Show &Postings";
        btnEntries.UseVisualStyleBackColor = true;
        PhxTheme.MakePrimary(btnEntries);
        pnlBottom.Controls.Add(btnEntries);

        btnRefresh.Location = new Point(170, 8);
        btnRefresh.Size = new Size(110, 34);
        btnRefresh.Text = "&Refresh";
        btnRefresh.UseVisualStyleBackColor = true;
        pnlBottom.Controls.Add(btnRefresh);

        btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnClose.Location = new Point(700, 8);
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
        ClientSize = new Size(830, 560);
        Controls.Add(grdBalance);
        Controls.Add(pnlTop);
        Controls.Add(pnlBottom);
        Name = "frmTrialBalance";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Trial balance";
        ResumeLayout(false);
        PerformLayout();
    }

    private static void Bind(DataGridView grid, string property,
        string header, int fillWeight, bool rightAlign = false)
    {
        var column = new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            Name = property,
            HeaderText = header,
            FillWeight = fillWeight,
            SortMode = DataGridViewColumnSortMode.Automatic
        };

        if (rightAlign)
            column.DefaultCellStyle.Alignment =
                DataGridViewContentAlignment.MiddleRight;

        grid.Columns.Add(column);
    }
}
