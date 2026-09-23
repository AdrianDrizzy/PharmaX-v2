using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Setup;

partial class frmFirstRun
{
    private System.ComponentModel.IContainer components = null!;

    private Panel pnlHeader = null!;
    private Panel pnlBody = null!;
    private Panel pnlFooter = null!;
    private Label lblTitle = null!;
    private Label lblSubtitle = null!;

    private GroupBox grpKind = null!;
    private RadioButton radStandalone = null!;
    private RadioButton radServer = null!;
    private RadioButton radTill = null!;

    private GroupBox grpDatabase = null!;
    private Label lblHost = null!;
    private Label lblPort = null!;
    private Label lblDatabase = null!;
    private Label lblUser = null!;
    private Label lblPassword = null!;
    private TextBox txtHost = null!;
    private NumericUpDown numPort = null!;
    private TextBox txtDatabase = null!;
    private TextBox txtUser = null!;
    private TextBox txtPassword = null!;
    private Button btnTest = null!;
    private Label lblTestResult = null!;

    private GroupBox grpWorkstation = null!;
    private Label lblBranchCode = null!;
    private Label lblTillNumber = null!;
    private Label lblWorkstationId = null!;
    private TextBox txtBranchCode = null!;
    private TextBox txtTillNumber = null!;
    private TextBox txtWorkstationId = null!;

    private GroupBox grpPharmacy = null!;
    private Label lblTradingName = null!;
    private Label lblCurrency = null!;
    private Label lblVatNumber = null!;
    private TextBox txtTradingName = null!;
    private ComboBox cboCurrency = null!;
    private TextBox txtVatNumber = null!;

    private Button btnFinish = null!;
    private Button btnQuit = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();

        pnlHeader = new Panel();
        pnlBody = new Panel();
        pnlFooter = new Panel();
        lblTitle = new Label();
        lblSubtitle = new Label();

        grpKind = new GroupBox();
        radStandalone = new RadioButton();
        radStandalone.Name = "radStandalone";
        radServer = new RadioButton();
        radServer.Name = "radServer";
        radTill = new RadioButton();
        radTill.Name = "radTill";

        grpDatabase = new GroupBox();
        lblHost = new Label();
        lblPort = new Label();
        lblDatabase = new Label();
        lblUser = new Label();
        lblPassword = new Label();
        txtHost = new TextBox();
        numPort = new NumericUpDown();
        txtDatabase = new TextBox();
        txtUser = new TextBox();
        txtPassword = new TextBox();
        btnTest = new Button();
        lblTestResult = new Label();

        grpWorkstation = new GroupBox();
        lblBranchCode = new Label();
        lblTillNumber = new Label();
        lblWorkstationId = new Label();
        txtBranchCode = new TextBox();
        txtTillNumber = new TextBox();
        txtWorkstationId = new TextBox();

        grpPharmacy = new GroupBox();
        lblTradingName = new Label();
        lblCurrency = new Label();
        lblVatNumber = new Label();
        txtTradingName = new TextBox();
        cboCurrency = new ComboBox();
        txtVatNumber = new TextBox();

        btnFinish = new Button();
        btnQuit = new Button();

        SuspendLayout();

        // ---- Header ------------------------------------------------------
        lblTitle.AutoSize = false;
        lblTitle.Dock = DockStyle.Top;
        lblTitle.Font = PhxTheme.Display;
        lblTitle.Height = 40;
        lblTitle.Text = "Set up PharmaX on this computer";

        lblSubtitle.AutoSize = false;
        lblSubtitle.Dock = DockStyle.Top;
        lblSubtitle.ForeColor = PhxTheme.Muted;
        lblSubtitle.Height = 40;
        lblSubtitle.Text =
            "Answered once per machine. PharmaX will create and prepare "
            + "the database for you.";

        // Added bottom-up: docking resolves from the highest child index
        // down, so the title added last sits at the top.
        pnlHeader.Controls.Add(lblSubtitle);
        pnlHeader.Controls.Add(lblTitle);
        pnlHeader.Dock = DockStyle.Top;
        pnlHeader.Height = 88;
        pnlHeader.Padding = new Padding(0, 0, 0, 8);

        // ---- What is this machine ----------------------------------------
        radStandalone.AutoSize = true;
        radStandalone.Checked = true;
        radStandalone.Location = new Point(18, 28);
        radStandalone.Size = new Size(560, 21);
        radStandalone.Text =
            "One computer on its own - PharmaX keeps its own file";

        radServer.AutoSize = true;
        radServer.Location = new Point(18, 56);
        radServer.Size = new Size(560, 21);
        radServer.Text =
            "The pharmacy's main computer - it holds the database the "
            + "tills share";

        radTill.AutoSize = true;
        radTill.Location = new Point(18, 84);
        radTill.Size = new Size(560, 21);
        radTill.Text = "A till - it uses the main computer's database";

        grpKind.Controls.AddRange(new Control[]
            { radStandalone, radServer, radTill });
        grpKind.Dock = DockStyle.Top;
        grpKind.Height = 122;
        grpKind.Text = "What is this computer?";

        // ---- Database ----------------------------------------------------
        Caption(lblHost, "Main computer", 18, 32);
        txtHost.Location = new Point(150, 28);
        txtHost.Name = "txtHost";
        txtHost.PlaceholderText = "Computer name or address, e.g. PHARMACY-PC";
        txtHost.Size = new Size(290, 23);

        Caption(lblPort, "Port", 460, 32);
        numPort.Location = new Point(516, 28);
        numPort.Maximum = 65535;
        numPort.Minimum = 1;
        numPort.Name = "numPort";
        numPort.Size = new Size(80, 23);
        numPort.Value = 3306;

        Caption(lblDatabase, "Database", 18, 68);
        txtDatabase.Location = new Point(150, 64);
        txtDatabase.Name = "txtDatabase";
        txtDatabase.Size = new Size(200, 23);
        txtDatabase.Text = "PharmaX";

        Caption(lblUser, "Username", 18, 104);
        txtUser.Location = new Point(150, 100);
        txtUser.Name = "txtUser";
        txtUser.Size = new Size(200, 23);

        Caption(lblPassword, "Password", 370, 104);
        txtPassword.Location = new Point(460, 100);
        txtPassword.Name = "txtPassword";
        txtPassword.Size = new Size(136, 23);
        txtPassword.UseSystemPasswordChar = true;

        btnTest.Location = new Point(150, 140);
        btnTest.Name = "btnTest";
        btnTest.Size = new Size(160, 32);
        btnTest.Text = "&Test connection";
        btnTest.UseVisualStyleBackColor = true;

        lblTestResult.AutoSize = false;
        lblTestResult.Location = new Point(322, 140);
        lblTestResult.Size = new Size(274, 44);

        grpDatabase.Controls.AddRange(new Control[]
        {
            lblTestResult, btnTest, txtPassword, lblPassword, txtUser,
            lblUser, txtDatabase, lblDatabase, numPort, lblPort, txtHost,
            lblHost
        });

        grpDatabase.Dock = DockStyle.Top;
        grpDatabase.Height = 196;
        grpDatabase.Text = "Database";

        // ---- This workstation --------------------------------------------
        Caption(lblBranchCode, "Branch", 18, 32);
        txtBranchCode.Location = new Point(150, 28);
        txtBranchCode.MaxLength = 10;
        txtBranchCode.Name = "txtBranchCode";
        txtBranchCode.Size = new Size(80, 23);
        txtBranchCode.Text = "1";

        Caption(lblTillNumber, "Till number", 260, 32);
        txtTillNumber.Location = new Point(360, 28);
        txtTillNumber.MaxLength = 10;
        txtTillNumber.Name = "txtTillNumber";
        txtTillNumber.Size = new Size(80, 23);
        txtTillNumber.Text = "1";

        Caption(lblWorkstationId, "This computer", 18, 68);
        txtWorkstationId.Location = new Point(150, 64);
        txtWorkstationId.Name = "txtWorkstationId";
        txtWorkstationId.ReadOnly = true;
        txtWorkstationId.Size = new Size(446, 23);

        grpWorkstation.Controls.AddRange(new Control[]
        {
            txtWorkstationId, lblWorkstationId, txtTillNumber,
            lblTillNumber, txtBranchCode, lblBranchCode
        });

        grpWorkstation.Dock = DockStyle.Top;
        grpWorkstation.Height = 104;
        grpWorkstation.Text =
            "This workstation - every till needs its own number";

        // ---- Pharmacy ----------------------------------------------------
        Caption(lblTradingName, "Pharmacy name", 18, 32);
        txtTradingName.Location = new Point(150, 28);
        txtTradingName.MaxLength = 100;
        txtTradingName.Name = "txtTradingName";
        txtTradingName.Size = new Size(290, 23);

        Caption(lblCurrency, "Currency", 18, 68);
        cboCurrency.DropDownStyle = ComboBoxStyle.DropDownList;
        cboCurrency.Location = new Point(150, 64);
        cboCurrency.Name = "cboCurrency";
        cboCurrency.Size = new Size(290, 23);

        Caption(lblVatNumber, "VAT number", 18, 104);
        txtVatNumber.Location = new Point(150, 100);
        txtVatNumber.MaxLength = 30;
        txtVatNumber.Name = "txtVatNumber";
        txtVatNumber.Size = new Size(290, 23);

        grpPharmacy.Controls.AddRange(new Control[]
        {
            txtVatNumber, lblVatNumber, cboCurrency, lblCurrency,
            txtTradingName, lblTradingName
        });

        grpPharmacy.Dock = DockStyle.Top;
        grpPharmacy.Height = 140;
        grpPharmacy.Text = "The pharmacy";

        // Added bottom-up so the order on screen reads kind, database,
        // workstation, pharmacy.
        pnlBody.AutoScroll = true;
        pnlBody.Controls.Add(grpPharmacy);
        pnlBody.Controls.Add(grpWorkstation);
        pnlBody.Controls.Add(grpDatabase);
        pnlBody.Controls.Add(grpKind);
        pnlBody.Dock = DockStyle.Fill;

        // ---- Footer ------------------------------------------------------
        btnFinish.Location = new Point(0, 10);
        btnFinish.Name = "btnFinish";
        btnFinish.Size = new Size(190, 36);
        btnFinish.Text = "&Set up and start";
        btnFinish.UseVisualStyleBackColor = true;
        PhxTheme.MakePrimary(btnFinish);

        btnQuit.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnQuit.Location = new Point(510, 10);
        btnQuit.Name = "btnQuit";
        btnQuit.Size = new Size(110, 36);
        btnQuit.Text = "&Quit";
        btnQuit.UseVisualStyleBackColor = true;

        pnlFooter.Controls.AddRange(new Control[] { btnQuit, btnFinish });
        pnlFooter.Dock = DockStyle.Bottom;
        pnlFooter.Height = 56;

        // ---- Form --------------------------------------------------------
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = btnQuit;
        ClientSize = new Size(660, 720);
        Controls.Add(pnlBody);
        Controls.Add(pnlFooter);
        Controls.Add(pnlHeader);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "frmFirstRun";
        Padding = new Padding(20);
        StartPosition = FormStartPosition.CenterScreen;
        Text = "PharmaX - Setup";

        ((System.ComponentModel.ISupportInitialize)numPort).EndInit();

        ResumeLayout(false);
        PerformLayout();
    }

    private static void Caption(Label label, string text, int x, int y)
    {
        label.AutoSize = true;
        label.Location = new Point(x, y);
        label.Text = text;
    }
}
