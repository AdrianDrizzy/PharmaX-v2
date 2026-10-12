using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Sales;

partial class frmCollect
{
    private System.ComponentModel.IContainer components = null!;
    private Panel pnlTop = null!;
    private Panel pnlBottom = null!;
    private Label lblFind = null!;
    private Label lblSummary = null!;
    private TextBox txtFind = null!;
    private PhxGrid grdWaiting = null!;
    private Button btnCollect = null!;
    private Button btnRefresh = null!;
    private Button btnClose = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _poll.Dispose();
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
        lblFind = new Label();
        lblSummary = new Label();
        txtFind = new TextBox();
        grdWaiting = new PhxGrid();
        btnCollect = new Button();
        btnCollect.Name = "btnCollect";
        btnRefresh = new Button();
        btnRefresh.Name = "btnRefresh";
        btnClose = new Button();
        btnClose.Name = "btnClose";
        SuspendLayout();

        lblFind.AutoSize = true;
        lblFind.Location = new Point(12, 17);
        lblFind.Text = "&Find";

        txtFind.Location = new Point(70, 13);
        txtFind.Name = "txtFind";
        txtFind.PlaceholderText = "Patient name, script or dispensing number";
        txtFind.Size = new Size(320, 23);
        txtFind.TabIndex = 0;

        lblSummary.AutoSize = false;
        lblSummary.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        lblSummary.Font = PhxTheme.Subheading;
        lblSummary.ForeColor = PhxTheme.Title;
        lblSummary.Location = new Point(420, 13);
        lblSummary.Size = new Size(420, 26);
        lblSummary.TextAlign = ContentAlignment.MiddleRight;

        pnlTop.Controls.AddRange(new Control[]
            { lblSummary, txtFind, lblFind });
        pnlTop.Dock = DockStyle.Top;
        pnlTop.Height = 52;
        pnlTop.Name = "pnlTop";

        grdWaiting.AutoGenerateColumns = false;
        grdWaiting.Dock = DockStyle.Fill;
        grdWaiting.Name = "grdWaiting";
        grdWaiting.MultiSelect = false;

        // Wider rows than a maintenance list: a cashier reads this one
        // across a counter with a patient waiting, not up close.
        grdWaiting.RowTemplate.Height = 30;

        Bind(grdWaiting, "Waited", "Waiting", 80);
        Bind(grdWaiting, "PatientName", "Patient", 190);
        Bind(grdWaiting, "ScriptNumber", "Script", 110);
        Bind(grdWaiting, "DocumentNumber", "Dispensed", 120);
        Bind(grdWaiting, "Items", "Items", 55, rightAlign: true);
        Bind(grdWaiting, "Owing", "To pay", 95, rightAlign: true);
        Bind(grdWaiting, "PharmacistName", "Pharmacist", 130);

        btnCollect.Location = new Point(12, 8);
        btnCollect.Size = new Size(160, 34);
        btnCollect.Text = "Take &Payment";
        btnCollect.UseVisualStyleBackColor = true;
        PhxTheme.MakePrimary(btnCollect);
        pnlBottom.Controls.Add(btnCollect);

        btnRefresh.Location = new Point(180, 8);
        btnRefresh.Size = new Size(110, 34);
        btnRefresh.Text = "&Refresh";
        btnRefresh.UseVisualStyleBackColor = true;
        pnlBottom.Controls.Add(btnRefresh);

        btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnClose.Location = new Point(740, 8);
        btnClose.Size = new Size(110, 34);
        btnClose.Text = "&Close";
        btnClose.UseVisualStyleBackColor = true;
        pnlBottom.Controls.Add(btnClose);

        pnlBottom.Dock = DockStyle.Bottom;
        pnlBottom.Height = 52;
        pnlBottom.Name = "pnlBottom";

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        AcceptButton = btnCollect;
        CancelButton = btnClose;
        ClientSize = new Size(870, 540);
        Controls.Add(grdWaiting);
        Controls.Add(pnlTop);
        Controls.Add(pnlBottom);
        Name = "frmCollect";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Scripts to collect";
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
