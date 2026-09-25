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
    private Panel pnlPharmacy = null!;
    private TableLayoutPanel layPharmacy = null!;
    private Label lblPharmacyHeading = null!;
    private Label lblTradingName = null!;
    private TextBox txtTradingName = null!;
    private Label lblRegisteredName = null!;
    private TextBox txtRegisteredName = null!;
    private Label lblVatNumber = null!;
    private TextBox txtVatNumber = null!;
    private Label lblAddress = null!;
    private TextBox txtAddress = null!;
    private Button btnSavePharmacy = null!;

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
        btnRefresh.Name = "btnRefresh";
        btnClose = new Button();
        btnClose.Name = "btnClose";
        pnlPharmacy = new Panel();
        layPharmacy = new TableLayoutPanel();
        lblPharmacyHeading = new Label();
        lblTradingName = new Label();
        txtTradingName = new TextBox();
        lblRegisteredName = new Label();
        txtRegisteredName = new TextBox();
        lblVatNumber = new Label();
        txtVatNumber = new TextBox();
        lblAddress = new Label();
        txtAddress = new TextBox();
        btnSavePharmacy = new Button();
        SuspendLayout();

        BuildPharmacyDetails();

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
        ClientSize = new Size(810, 760);
        Controls.Add(grdRows);
        Controls.Add(pnlPharmacy);
        Controls.Add(pnlTop);
        Controls.Add(pnlBottom);
        Name = "frmSystemInfo";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - System";
        ResumeLayout(false);
        PerformLayout();
    }

    /// <summary>
    /// The pharmacy's trading name, registered name, VAT number and
    /// address, above the console's list. Shown only on the System
    /// Console.
    /// </summary>
    private void BuildPharmacyDetails()
    {
        lblPharmacyHeading.AutoSize = true;
        lblPharmacyHeading.Font = PhxTheme.Emphasis;
        lblPharmacyHeading.Margin = new Padding(0, 0, 0, 8);
        lblPharmacyHeading.Name = "lblPharmacyHeading";
        lblPharmacyHeading.Text =
            "Pharmacy details - on every receipt, window title, sign-in and home screen";

        Caption(lblTradingName, "lblTradingName", "&Trading name");
        txtTradingName.Dock = DockStyle.Fill;
        txtTradingName.MaxLength = 100;
        txtTradingName.Name = "txtTradingName";
        txtTradingName.TabIndex = 0;

        // The company behind the shop front, printed under the trading
        // name on every receipt. May be left blank.
        Caption(lblRegisteredName, "lblRegisteredName", "Re&gistered name");
        txtRegisteredName.Dock = DockStyle.Fill;
        txtRegisteredName.MaxLength = 100;
        txtRegisteredName.Name = "txtRegisteredName";
        txtRegisteredName.TabIndex = 1;

        Caption(lblVatNumber, "lblVatNumber", "&VAT number");
        txtVatNumber.Anchor = AnchorStyles.Left;
        txtVatNumber.MaxLength = 30;
        txtVatNumber.Name = "txtVatNumber";
        txtVatNumber.MinimumSize = new Size(240, 0);
        txtVatNumber.Width = 240;
        txtVatNumber.TabIndex = 2;

        Caption(lblAddress, "lblAddress", "&Address");
        lblAddress.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        txtAddress.AcceptsReturn = true;
        txtAddress.Dock = DockStyle.Fill;
        txtAddress.MaxLength = 400;
        txtAddress.Multiline = true;
        txtAddress.Name = "txtAddress";
        txtAddress.ScrollBars = ScrollBars.Vertical;
        txtAddress.TabIndex = 3;

        btnSavePharmacy.Anchor = AnchorStyles.Right;
        btnSavePharmacy.AutoSize = true;
        btnSavePharmacy.Name = "btnSavePharmacy";
        btnSavePharmacy.Padding = new Padding(10, 2, 10, 2);
        btnSavePharmacy.TabIndex = 4;
        btnSavePharmacy.Text = "&Save pharmacy details";
        btnSavePharmacy.UseVisualStyleBackColor = true;

        layPharmacy.ColumnCount = 2;
        layPharmacy.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
        layPharmacy.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layPharmacy.Dock = DockStyle.Fill;
        layPharmacy.Name = "layPharmacy";
        layPharmacy.RowCount = 6;
        layPharmacy.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layPharmacy.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        layPharmacy.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        layPharmacy.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        layPharmacy.RowStyles.Add(new RowStyle(SizeType.Absolute, 70F));
        layPharmacy.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layPharmacy.Controls.Add(lblPharmacyHeading, 0, 0);
        layPharmacy.SetColumnSpan(lblPharmacyHeading, 2);
        layPharmacy.Controls.Add(lblTradingName, 0, 1);
        layPharmacy.Controls.Add(txtTradingName, 1, 1);
        layPharmacy.Controls.Add(lblRegisteredName, 0, 2);
        layPharmacy.Controls.Add(txtRegisteredName, 1, 2);
        layPharmacy.Controls.Add(lblVatNumber, 0, 3);
        layPharmacy.Controls.Add(txtVatNumber, 1, 3);
        layPharmacy.Controls.Add(lblAddress, 0, 4);
        layPharmacy.Controls.Add(txtAddress, 1, 4);
        layPharmacy.Controls.Add(btnSavePharmacy, 1, 5);

        pnlPharmacy.Controls.Add(layPharmacy);
        pnlPharmacy.Dock = DockStyle.Top;
        pnlPharmacy.Height = 238;
        pnlPharmacy.Name = "pnlPharmacy";
        pnlPharmacy.Padding = new Padding(PhxTheme.Gutter, 4, PhxTheme.Gutter, 8);
        pnlPharmacy.Visible = false;
    }

    private static void Caption(Label label, string name, string text)
    {
        label.Anchor = AnchorStyles.Left;
        label.AutoSize = true;
        label.Name = name;
        label.Text = text;
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
