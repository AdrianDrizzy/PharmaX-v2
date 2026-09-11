using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Admin;

partial class frmSystemInfo
{
    private System.ComponentModel.IContainer components = null!;
    private Panel pnlTop = null!;
    private Panel pnlBottom = null!;
    private Label lblCaption = null!;
    private PhxGrid grdRows = null!;
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
        lblCaption = new Label();
        grdRows = new PhxGrid();
        btnRefresh = new Button();
        btnClose = new Button();
        SuspendLayout();

        lblCaption.AutoSize = false;
        lblCaption.Dock = DockStyle.Fill;
        lblCaption.Font = PhxTheme.Subheading;
        lblCaption.Name = "lblCaption";
        lblCaption.Padding = new Padding(PhxTheme.Gutter, 0, 0, 0);
        lblCaption.TextAlign = ContentAlignment.MiddleLeft;

        pnlTop.Controls.Add(lblCaption);
        pnlTop.Dock = DockStyle.Top;
        pnlTop.Height = 46;
        pnlTop.Name = "pnlTop";

        grdRows.AutoGenerateColumns = false;
        grdRows.Dock = DockStyle.Fill;
        grdRows.Name = "grdRows";

        Bind(grdRows, "Group", "Group", 90);
        Bind(grdRows, "Name", "Setting", 170);
        Bind(grdRows, "Value", "Value", 330);
        Bind(grdRows, "Source", "Held in", 130);

        btnRefresh.Location = new Point(PhxTheme.Gutter, 8);
        btnRefresh.Size = new Size(90, 30);
        btnRefresh.TabIndex = 0;
        btnRefresh.Text = "&Refresh";
        btnRefresh.UseVisualStyleBackColor = true;

        btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnClose.Location = new Point(700, 8);
        btnClose.Size = new Size(90, 30);
        btnClose.TabIndex = 1;
        btnClose.Text = "&Close";
        btnClose.UseVisualStyleBackColor = true;

        pnlBottom.Controls.AddRange(new Control[] { btnClose, btnRefresh });
        pnlBottom.Dock = DockStyle.Bottom;
        pnlBottom.Height = 48;
        pnlBottom.Name = "pnlBottom";

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(810, 560);
        Controls.Add(grdRows);
        Controls.Add(pnlTop);
        Controls.Add(pnlBottom);
        Name = "frmSystemInfo";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - System";
        ResumeLayout(false);
        PerformLayout();
    }

    /// A bound column, as the other list screens declare theirs.
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
