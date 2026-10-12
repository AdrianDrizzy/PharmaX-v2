namespace PharmaX.Desktop.Features.Reference;

partial class frmTariffEdit
{
    private System.ComponentModel.IContainer components = null!;
    private TableLayoutPanel layRoot = null!;
    private Label lblScheme = null!;
    private Label lblProduct = null!;
    private Label lblPrice = null!;
    private Label lblFee = null!;
    private Label lblFrom = null!;
    private Label lblTo = null!;
    private ComboBox cboScheme = null!;
    private Panel pnlProduct = null!;
    private TextBox txtProduct = null!;
    private Button btnChooseProduct = null!;
    private Controls.MoneyTextBox txtPrice = null!;
    private Controls.MoneyTextBox txtFee = null!;
    private DateTimePicker dtpFrom = null!;
    private Panel pnlTo = null!;
    private CheckBox chkOpenEnded = null!;
    private DateTimePicker dtpTo = null!;
    private FlowLayoutPanel pnlButtons = null!;
    private Button btnSave = null!;
    private Button btnCancel = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        layRoot = new TableLayoutPanel();
        lblScheme = new Label();
        lblProduct = new Label();
        lblPrice = new Label();
        lblFee = new Label();
        lblFrom = new Label();
        lblTo = new Label();
        cboScheme = new ComboBox();
        pnlProduct = new Panel();
        txtProduct = new TextBox();
        btnChooseProduct = new Button();
        txtPrice = new Controls.MoneyTextBox();
        txtFee = new Controls.MoneyTextBox();
        dtpFrom = new DateTimePicker();
        pnlTo = new Panel();
        chkOpenEnded = new CheckBox();
        dtpTo = new DateTimePicker();
        pnlButtons = new FlowLayoutPanel();
        btnSave = new Button();
        btnCancel = new Button();
        SuspendLayout();

        lblScheme.Anchor = AnchorStyles.Left;
        lblScheme.AutoSize = true;
        lblScheme.Text = "S&ociety";

        cboScheme.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        cboScheme.DropDownStyle = ComboBoxStyle.DropDownList;
        cboScheme.Name = "cboScheme";
        cboScheme.Width = 320;

        lblProduct.Anchor = AnchorStyles.Left;
        lblProduct.AutoSize = true;
        lblProduct.Text = "&Product";

        txtProduct.Dock = DockStyle.Fill;
        txtProduct.Name = "txtProduct";
        txtProduct.PlaceholderText = "Choose a product";
        txtProduct.ReadOnly = true;

        btnChooseProduct.Dock = DockStyle.Right;
        btnChooseProduct.Name = "btnChooseProduct";
        btnChooseProduct.Text = "&Find...";
        btnChooseProduct.UseVisualStyleBackColor = true;
        btnChooseProduct.Width = 90;

        pnlProduct.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        pnlProduct.Controls.Add(txtProduct);
        pnlProduct.Controls.Add(btnChooseProduct);
        pnlProduct.Height = 26;
        pnlProduct.Name = "pnlProduct";
        pnlProduct.Width = 320;

        lblPrice.Anchor = AnchorStyles.Left;
        lblPrice.AutoSize = true;
        lblPrice.Text = "P&rice";

        txtPrice.Anchor = AnchorStyles.Left;
        txtPrice.Name = "txtPrice";
        txtPrice.PlaceholderText = "0.00";
        txtPrice.Width = 130;

        lblFee.Anchor = AnchorStyles.Left;
        lblFee.AutoSize = true;
        lblFee.Text = "Dispensing &fee";

        txtFee.Anchor = AnchorStyles.Left;
        txtFee.Name = "txtFee";
        txtFee.PlaceholderText = "0.00";
        txtFee.Width = 130;

        lblFrom.Anchor = AnchorStyles.Left;
        lblFrom.AutoSize = true;
        lblFrom.Text = "Effective fro&m";

        dtpFrom.Anchor = AnchorStyles.Left;
        dtpFrom.Format = DateTimePickerFormat.Short;
        dtpFrom.Name = "dtpFrom";
        dtpFrom.Width = 140;

        lblTo.Anchor = AnchorStyles.Left;
        lblTo.AutoSize = true;
        lblTo.Text = "Effective &to";

        chkOpenEnded.AutoSize = true;
        chkOpenEnded.Checked = true;
        chkOpenEnded.Location = new Point(150, 4);
        chkOpenEnded.Name = "chkOpenEnded";
        chkOpenEnded.Text = "&Until further notice";

        dtpTo.Format = DateTimePickerFormat.Short;
        dtpTo.Location = new Point(0, 2);
        dtpTo.Name = "dtpTo";
        dtpTo.Width = 140;

        pnlTo.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        pnlTo.Controls.Add(chkOpenEnded);
        pnlTo.Controls.Add(dtpTo);
        pnlTo.Height = 28;
        pnlTo.Name = "pnlTo";
        pnlTo.Width = 320;

        btnSave.AutoSize = true;
        btnSave.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnSave.MinimumSize = new Size(90, 30);
        btnSave.Name = "btnSave";
        btnSave.Text = "&Save";
        btnSave.UseVisualStyleBackColor = true;

        btnCancel.AutoSize = true;
        btnCancel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnCancel.Margin = new Padding(8, 3, 0, 3);
        btnCancel.MinimumSize = new Size(90, 30);
        btnCancel.Name = "btnCancel";
        btnCancel.Text = "Cancel";
        btnCancel.UseVisualStyleBackColor = true;

        pnlButtons.Anchor = AnchorStyles.Right;
        pnlButtons.AutoSize = true;
        pnlButtons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        pnlButtons.FlowDirection = FlowDirection.LeftToRight;
        pnlButtons.Margin = new Padding(0, 14, 0, 0);
        pnlButtons.WrapContents = false;
        pnlButtons.Controls.Add(btnSave);
        pnlButtons.Controls.Add(btnCancel);

        layRoot.AutoSize = true;
        layRoot.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        layRoot.ColumnCount = 2;
        layRoot.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layRoot.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layRoot.Name = "layRoot";
        layRoot.Padding = new Padding(20, 18, 20, 18);
        layRoot.RowCount = 7;
        for (var row = 0; row < 7; row++)
            layRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layRoot.Controls.Add(lblScheme, 0, 0);
        layRoot.Controls.Add(cboScheme, 1, 0);
        layRoot.Controls.Add(lblProduct, 0, 1);
        layRoot.Controls.Add(pnlProduct, 1, 1);
        layRoot.Controls.Add(lblPrice, 0, 2);
        layRoot.Controls.Add(txtPrice, 1, 2);
        layRoot.Controls.Add(lblFee, 0, 3);
        layRoot.Controls.Add(txtFee, 1, 3);
        layRoot.Controls.Add(lblFrom, 0, 4);
        layRoot.Controls.Add(dtpFrom, 1, 4);
        layRoot.Controls.Add(lblTo, 0, 5);
        layRoot.Controls.Add(pnlTo, 1, 5);
        layRoot.Controls.Add(pnlButtons, 0, 6);
        layRoot.SetColumnSpan(pnlButtons, 2);

        AcceptButton = btnSave;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = btnCancel;
        ClientSize = new Size(470, 330);
        Controls.Add(layRoot);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "frmTariffEdit";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Tariff";
        ResumeLayout(false);
        PerformLayout();
    }
}
