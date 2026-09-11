using PharmaX.Desktop.Controls;

namespace PharmaX.Desktop.Features.Patients;

partial class frmPatientList
{
    private System.ComponentModel.IContainer components = null!;
    private Panel pnlTop = null!;
    private Panel pnlBottom = null!;
    private Label lblSearch = null!;
    private Label lblCount = null!;
    private TextBox txtSearch = null!;
    private PhxGrid grdPatients = null!;
    private Button btnAdd = null!;
    private Button btnEdit = null!;
    private Button btnDeactivate = null!;
    private Button btnChoose = null!;
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
        grdPatients = new PhxGrid();
        btnAdd = new Button();
        btnEdit = new Button();
        btnDeactivate = new Button();
        btnChoose = new Button();
        btnClose = new Button();
        SuspendLayout();

        lblSearch.AutoSize = true;
        lblSearch.Location = new Point(12, 17);
        lblSearch.Text = "&Search";

        txtSearch.Location = new Point(70, 13);
        txtSearch.Name = "txtSearch";
        txtSearch.PlaceholderText = "Surname or file number";
        txtSearch.Size = new Size(320, 23);
        txtSearch.TabIndex = 0;

        lblCount.AutoSize = false;
        lblCount.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        lblCount.Location = new Point(620, 17);
        lblCount.Size = new Size(150, 20);
        lblCount.TextAlign = ContentAlignment.MiddleRight;

        pnlTop.Controls.AddRange(new Control[]
            { lblCount, txtSearch, lblSearch });
        pnlTop.Dock = DockStyle.Top;
        pnlTop.Height = 48;
        pnlTop.Name = "pnlTop";

        grdPatients.AutoGenerateColumns = false;
        grdPatients.Dock = DockStyle.Fill;
        grdPatients.Name = "grdPatients";

        Bind(grdPatients, "FileNumber", "File", 90);
        Bind(grdPatients, "Surname", "Surname", 140);
        Bind(grdPatients, "FirstNames", "First names", 140);
        Bind(grdPatients, "Age", "Age", 45, rightAlign: true);
        Bind(grdPatients, "Telephone", "Telephone", 110);
        Bind(grdPatients, "HasAllergies", "Allergies", 65);
        Bind(grdPatients, "IsActive", "Active", 50);

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

        // Hidden until UseAsPicker turns it on.
        btnChoose.Location = new Point(x, 8);
        btnChoose.Size = new Size(110, 30);
        btnChoose.Text = "C&hoose";
        btnChoose.UseVisualStyleBackColor = true;
        btnChoose.Visible = false;
        pnlBottom.Controls.Add(btnChoose);

        btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnClose.Location = new Point(660, 8);
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
        ClientSize = new Size(790, 520);
        Controls.Add(grdPatients);
        Controls.Add(pnlTop);
        Controls.Add(pnlBottom);
        Name = "frmPatientList";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Patients";
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
