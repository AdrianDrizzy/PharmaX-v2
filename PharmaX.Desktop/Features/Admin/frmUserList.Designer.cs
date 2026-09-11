using PharmaX.Desktop.Controls;

namespace PharmaX.Desktop.Features.Admin;

partial class frmUserList
{
    private System.ComponentModel.IContainer components = null!;
    private Panel pnlTop = null!;
    private Panel pnlBottom = null!;
    private CheckBox chkIncludeInactive = null!;
    private Label lblCount = null!;
    private PhxGrid grdUsers = null!;
    private Button btnAdd = null!;
    private Button btnEdit = null!;
    private Button btnReset = null!;
    private Button btnUnlock = null!;
    private Button btnToggleActive = null!;
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
        grdUsers = new PhxGrid();
        btnAdd = new Button();
        btnEdit = new Button();
        btnReset = new Button();
        btnUnlock = new Button();
        btnToggleActive = new Button();
        btnClose = new Button();
        SuspendLayout();

        chkIncludeInactive.AutoSize = true;
        chkIncludeInactive.Location = new Point(12, 16);
        chkIncludeInactive.Name = "chkIncludeInactive";
        chkIncludeInactive.TabIndex = 0;
        chkIncludeInactive.Text = "Show &deactivated accounts";
        chkIncludeInactive.UseVisualStyleBackColor = true;

        lblCount.AutoSize = false;
        lblCount.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        lblCount.Location = new Point(760, 16);
        lblCount.Size = new Size(160, 20);
        lblCount.TextAlign = ContentAlignment.MiddleRight;

        pnlTop.Controls.AddRange(new Control[] { lblCount, chkIncludeInactive });
        pnlTop.Dock = DockStyle.Top;
        pnlTop.Height = 46;
        pnlTop.Name = "pnlTop";

        grdUsers.AutoGenerateColumns = false;
        grdUsers.Dock = DockStyle.Fill;
        grdUsers.Name = "grdUsers";

        Bind(grdUsers, "Username", "Username", 90);
        Bind(grdUsers, "DisplayName", "Name", 150);
        Bind(grdUsers, "Roles", "Roles", 200);
        Bind(grdUsers, "DiscountLimitPercent", "Disc %", 55, "N0", true);
        Bind(grdUsers, "IsActive", "Active", 50);
        Bind(grdUsers, "IsLockedOut", "Locked", 50);
        Bind(grdUsers, "MustChangePassword", "Must change", 75);
        Bind(grdUsers, "LastLoginAtUtc", "Last sign-in", 110,
             "dd/MM/yyyy HH:mm");

        var x = 12;
        foreach (var (button, text) in new[]
        {
            (btnAdd, "&Add"), (btnEdit, "&Edit"),
            (btnReset, "&Reset password"), (btnUnlock, "&Unlock"),
            (btnToggleActive, "Deac&tivate")
        })
        {
            button.Location = new Point(x, 8);
            button.Size = new Size(120, 30);
            button.Text = text;
            button.UseVisualStyleBackColor = true;
            pnlBottom.Controls.Add(button);
            x += 126;
        }

        btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnClose.Location = new Point(810, 8);
        btnClose.Size = new Size(110, 30);
        btnClose.Text = "&Close";
        btnClose.UseVisualStyleBackColor = true;
        pnlBottom.Controls.Add(btnClose);

        pnlBottom.Dock = DockStyle.Bottom;
        pnlBottom.Height = 48;
        pnlBottom.Name = "pnlBottom";

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(940, 520);
        Controls.Add(grdUsers);
        Controls.Add(pnlTop);
        Controls.Add(pnlBottom);
        Name = "frmUserList";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Users";
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
