using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Finance;

partial class frmClaims
{
    private System.ComponentModel.IContainer components = null!;
    private Panel pnlTop = null!;
    private Panel pnlBottom = null!;
    private Panel pnlTotals = null!;
    private Label lblScheme = null!;
    private Label lblReceived = null!;
    private Label lblReference = null!;
    private Label lblTotals = null!;
    private ComboBox cboScheme = null!;
    private DateTimePicker dtpReceived = null!;
    private TextBox txtReference = null!;
    private PhxGrid grdClaims = null!;
    private Button btnSettle = null!;
    private Button btnPayAll = null!;
    private Button btnReject = null!;
    private Button btnAges = null!;
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
        pnlTotals = new Panel();
        lblScheme = new Label();
        lblReceived = new Label();
        lblReference = new Label();
        lblTotals = new Label();
        cboScheme = new ComboBox();
        dtpReceived = new DateTimePicker();
        txtReference = new TextBox();
        grdClaims = new PhxGrid();
        btnSettle = new Button();
        btnPayAll = new Button();
        btnReject = new Button();
        btnAges = new Button();
        btnRefresh = new Button();
        btnClose = new Button();
        SuspendLayout();

        lblScheme.AutoSize = true;
        lblScheme.Location = new Point(12, 17);
        lblScheme.Text = "&Scheme";

        cboScheme.DropDownStyle = ComboBoxStyle.DropDownList;
        cboScheme.Location = new Point(90, 13);
        cboScheme.Name = "cboScheme";
        cboScheme.Size = new Size(280, 23);
        cboScheme.TabIndex = 0;

        lblReceived.AutoSize = true;
        lblReceived.Location = new Point(392, 17);
        lblReceived.Text = "&Received";

        dtpReceived.Format = DateTimePickerFormat.Custom;
        dtpReceived.CustomFormat = "dd/MM/yyyy";
        dtpReceived.Location = new Point(470, 13);
        dtpReceived.Name = "dtpReceived";
        dtpReceived.Size = new Size(120, 23);
        dtpReceived.TabIndex = 1;

        lblReference.AutoSize = true;
        lblReference.Location = new Point(12, 53);
        lblReference.Text = "Re&ference";

        txtReference.Location = new Point(90, 49);
        txtReference.MaxLength = 100;
        txtReference.Name = "txtReference";
        txtReference.PlaceholderText =
            "The remittance advice number, so the bank line can be traced "
            + "back to it";
        txtReference.Size = new Size(500, 23);
        txtReference.TabIndex = 2;

        pnlTop.Controls.AddRange(new Control[]
        {
            txtReference, lblReference, dtpReceived, lblReceived,
            cboScheme, lblScheme
        });

        pnlTop.Dock = DockStyle.Top;
        pnlTop.Height = 86;
        pnlTop.Name = "pnlTop";

        grdClaims.AutoGenerateColumns = false;
        grdClaims.AllowUserToAddRows = false;
        grdClaims.Dock = DockStyle.Fill;
        grdClaims.EditMode = DataGridViewEditMode.EditOnEnter;
        grdClaims.MultiSelect = false;
        grdClaims.Name = "grdClaims";
        grdClaims.RowTemplate.Height = 28;

        Read(grdClaims, "ClaimNumber", "Claim", 90);
        Read(grdClaims, "PatientName", "Patient", 160);
        Read(grdClaims, "MembershipNumber", "Membership", 110);
        Read(grdClaims, "ClaimedOnText", "Claimed", 85);
        Read(grdClaims, "AgeText", "Age", 60, right: true);
        Read(grdClaims, "OutstandingText", "Owed", 90, right: true);

        // The two columns the operator fills in from the remittance
        // advice. Everything left of here is read-only.
        var paying = new DataGridViewTextBoxColumn
        {
            DataPropertyName = "Paying",
            Name = "Paying",
            HeaderText = "Paying",
            FillWeight = 90,
            MaxInputLength = 15
        };

        paying.DefaultCellStyle.Alignment =
            DataGridViewContentAlignment.MiddleRight;

        paying.DefaultCellStyle.BackColor = PhxTheme.Information;
        grdClaims.Columns.Add(paying);

        grdClaims.Columns.Add(new DataGridViewCheckBoxColumn
        {
            DataPropertyName = "WriteOffRest",
            Name = "WriteOffRest",
            HeaderText = "Write off rest",
            FillWeight = 85
        });

        lblTotals.AutoSize = false;
        lblTotals.Dock = DockStyle.Fill;
        lblTotals.Font = PhxTheme.Subheading;
        lblTotals.Padding = new Padding(0, 0, 16, 0);
        lblTotals.TextAlign = ContentAlignment.MiddleRight;

        pnlTotals.Controls.Add(lblTotals);
        pnlTotals.Dock = DockStyle.Bottom;
        pnlTotals.Height = 34;
        pnlTotals.Name = "pnlTotals";

        var x = 12;
        foreach (var (button, text, width) in new[]
        {
            (btnSettle, "Record &Remittance", 170),
            (btnPayAll, "Pay in &Full", 110),
            (btnReject, "Re&ject", 100),
            (btnAges, "&Age Analysis", 130)
        })
        {
            button.Location = new Point(x, 8);
            button.Size = new Size(width, 34);
            button.Text = text;
            button.UseVisualStyleBackColor = true;
            pnlBottom.Controls.Add(button);
            x += width + 8;
        }

        PhxTheme.MakePrimary(btnSettle);

        btnRefresh.Location = new Point(x, 8);
        btnRefresh.Size = new Size(100, 34);
        btnRefresh.Text = "Re&fresh";
        btnRefresh.UseVisualStyleBackColor = true;
        pnlBottom.Controls.Add(btnRefresh);

        btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnClose.Location = new Point(860, 8);
        btnClose.Size = new Size(100, 34);
        btnClose.Text = "&Close";
        btnClose.UseVisualStyleBackColor = true;
        pnlBottom.Controls.Add(btnClose);

        pnlBottom.Dock = DockStyle.Bottom;
        pnlBottom.Height = 52;
        pnlBottom.Name = "pnlBottom";

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = btnClose;
        ClientSize = new Size(980, 600);
        Controls.Add(grdClaims);
        Controls.Add(pnlTop);
        Controls.Add(pnlTotals);
        Controls.Add(pnlBottom);
        Name = "frmClaims";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Medical aid claims";
        ResumeLayout(false);
        PerformLayout();
    }

    /// A column the operator reads but cannot change.
    private static void Read(DataGridView grid, string property,
        string header, int fillWeight, bool right = false)
    {
        var column = new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            Name = property,
            HeaderText = header,
            FillWeight = fillWeight,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.Automatic
        };

        if (right)
            column.DefaultCellStyle.Alignment =
                DataGridViewContentAlignment.MiddleRight;

        grid.Columns.Add(column);
    }
}
