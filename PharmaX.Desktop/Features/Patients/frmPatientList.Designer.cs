using PharmaX.Desktop.Controls;

namespace PharmaX.Desktop.Features.Patients;

partial class frmPatientList
{
    private System.ComponentModel.IContainer components = null!;
    private Panel pnlTop = null!;
    private Panel pnlBottom = null!;
    private Label lblSearch = null!;
    private TextBox txtSearch = null!;
    private PhxGrid grdPatients = null!;
    private Button btnAdd = null!;
    private Button btnEdit = null!;
    private Button btnDeactivate = null!;
    private Button btnDocuments = null!;
    private Button btnExport = null!;
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
        txtSearch = new TextBox();
        grdPatients = new PhxGrid();
        btnAdd = new Button();
        btnAdd.Name = "btnAdd";
        btnEdit = new Button();
        btnEdit.Name = "btnEdit";
        btnDeactivate = new Button();
        btnDeactivate.Name = "btnDeactivate";
        btnDocuments = new Button();
        btnDocuments.Name = "btnDocuments";
        btnExport = new Button();
        btnExport.Name = "btnExport";
        btnChoose = new Button();
        btnChoose.Name = "btnChoose";
        btnClose = new Button();
        btnClose.Name = "btnClose";
        SuspendLayout();

        lblSearch.AutoSize = true;
        lblSearch.Location = new Point(12, 17);
        lblSearch.Text = "&Search";

        txtSearch.Location = new Point(70, 13);
        txtSearch.Name = "txtSearch";
        txtSearch.PlaceholderText =
            "Surname then name, file, ID, membership, telephone or address";
        txtSearch.Size = new Size(320, 23);
        txtSearch.TabIndex = 0;

        pnlTop.Controls.AddRange(new Control[] { txtSearch, lblSearch });
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
        Bind(grdPatients, "Address", "Address", 170);
        Bind(grdPatients, "HasAllergies", "Allergies", 65);
        Bind(grdPatients, "IsActive", "Active", 50);

        var x = 12;
        foreach (var (button, text) in new[]
        {
            (btnAdd, "&Add"), (btnEdit, "&Edit"),
            (btnDeactivate, "Deac&tivate"), (btnDocuments, "Doc&uments")
        })
        {
            button.Location = new Point(x, 8);
            button.Size = new Size(110, 30);
            button.Text = text;
            button.UseVisualStyleBackColor = true;
            pnlBottom.Controls.Add(button);
            x += 116;
        }

        // Where Choose goes, and hidden in its place by UseAsPicker: a
        // picker answers another screen rather than exporting.
        btnExport.Location = new Point(x, 8);
        btnExport.Size = new Size(110, 30);
        btnExport.Text = "Ex&port";
        btnExport.UseVisualStyleBackColor = true;
        pnlBottom.Controls.Add(btnExport);

        // Hidden until UseAsPicker turns it on.
        btnChoose.Location = new Point(x, 8);
        btnChoose.Size = new Size(110, 30);
        btnChoose.Text = "C&hoose";
        btnChoose.UseVisualStyleBackColor = true;
        btnChoose.Visible = false;
        pnlBottom.Controls.Add(btnChoose);

        // The pages are turned on the bar above these (PagerBar), as on
        // every other list.
        btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnClose.Location = new Point(880, 8);
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
        ClientSize = new Size(1010, 520);
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
