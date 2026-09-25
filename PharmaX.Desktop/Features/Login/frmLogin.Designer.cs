using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Login;

partial class frmLogin
{
    private System.ComponentModel.IContainer components = null!;
    private TableLayoutPanel layRoot = null!;
    private Label lblTitle = null!;
    private Label lblParticulars = null!;
    private Panel pnlLogo = null!;
    private Label lblUsername = null!;
    private Label lblPassword = null!;
    private TextBox txtUsername = null!;
    private TextBox txtPassword = null!;
    private FlowLayoutPanel pnlButtons = null!;
    private Button btnLogin = null!;
    private Button btnCancel = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>
    /// Laid out by a TableLayoutPanel rather than by absolute
    /// coordinates.
    /// </summary>
    /// <remarks>
    /// This window is the first thing anyone sees, and on a display at
    /// 125 or 150 per cent it was cut off down the right-hand side and
    /// along the bottom: the logo, both boxes and both buttons all ran
    /// past the frame. Absolute Location values are authored at one
    /// scale and the window is drawn at another, and no amount of
    /// adjusting the numbers fixes that for every display a pharmacy
    /// might own. A layout panel measures its own contents, and an
    /// auto-sizing form then takes whatever size that comes to, so the
    /// window fits its contents at any scaling rather than at one.
    /// </remarks>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        layRoot = new TableLayoutPanel();
        lblTitle = new Label();
        lblParticulars = new Label();
        pnlLogo = new Panel();
        lblUsername = new Label();
        lblPassword = new Label();
        txtUsername = new TextBox();
        txtPassword = new TextBox();
        pnlButtons = new FlowLayoutPanel();
        btnLogin = new Button();
        btnCancel = new Button();
        SuspendLayout();

        // pnlLogo - the pharmacy's own mark, across the top and centred.
        // Sized in logical units and painted to fit, so the image keeps
        // its proportions whatever the panel comes out as. Hidden by the
        // form when there is no logo, so there is no gap where one
        // would have been.
        pnlLogo.Anchor = AnchorStyles.None;
        pnlLogo.Margin = new Padding(0, 0, 0, 10);
        pnlLogo.Name = "pnlLogo";
        pnlLogo.Size = new Size(320, 110);
        pnlLogo.Paint += (_, e) => PaintLogo(e);

        // lblTitle - the pharmacy's trading name under its logo. Wraps
        // rather than widening the window for a long name.
        lblTitle.Anchor = AnchorStyles.None;
        lblTitle.AutoSize = true;
        lblTitle.Font = new Font("Segoe UI", 16F, FontStyle.Bold);
        lblTitle.Margin = new Padding(0, 0, 0, 2);
        lblTitle.MaximumSize = new Size(360, 0);
        lblTitle.Name = "lblTitle";
        lblTitle.Text = "Sign in";
        lblTitle.TextAlign = ContentAlignment.MiddleCenter;

        // lblParticulars - the address and VAT number, quietly, so the
        // operator can see which pharmacy's till this is.
        lblParticulars.Anchor = AnchorStyles.None;
        lblParticulars.AutoSize = true;
        lblParticulars.ForeColor = PhxTheme.Muted;
        lblParticulars.Margin = new Padding(0, 0, 0, 16);
        lblParticulars.MaximumSize = new Size(360, 0);
        lblParticulars.Name = "lblParticulars";
        lblParticulars.TextAlign = ContentAlignment.MiddleCenter;

        // lblUsername
        lblUsername.Anchor = AnchorStyles.Left;
        lblUsername.AutoSize = true;
        lblUsername.Name = "lblUsername";
        lblUsername.Text = "&Username";

        // txtUsername
        txtUsername.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        txtUsername.Name = "txtUsername";
        txtUsername.Width = 250;

        // lblPassword
        lblPassword.Anchor = AnchorStyles.Left;
        lblPassword.AutoSize = true;
        lblPassword.Name = "lblPassword";
        lblPassword.Text = "&Password";

        // txtPassword
        txtPassword.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        txtPassword.Name = "txtPassword";
        txtPassword.UseSystemPasswordChar = true;
        txtPassword.Width = 250;

        // btnLogin / btnCancel - the flow panel keeps them together and
        // to the right without either one owning a fixed coordinate.
        btnLogin.AutoSize = true;
        btnLogin.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnLogin.MinimumSize = new Size(80, 30);
        btnLogin.Name = "btnLogin";
        btnLogin.Text = "&Log in";
        btnLogin.UseVisualStyleBackColor = true;

        btnCancel.AutoSize = true;
        btnCancel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnCancel.Margin = new Padding(8, 3, 0, 3);
        btnCancel.MinimumSize = new Size(80, 30);
        btnCancel.Name = "btnCancel";
        btnCancel.Text = "&Cancel";
        btnCancel.UseVisualStyleBackColor = true;

        pnlButtons.AutoSize = true;
        pnlButtons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        pnlButtons.Anchor = AnchorStyles.Right;
        pnlButtons.FlowDirection = FlowDirection.LeftToRight;
        pnlButtons.Margin = new Padding(0, 14, 0, 0);
        pnlButtons.Name = "pnlButtons";
        pnlButtons.WrapContents = false;
        pnlButtons.Controls.Add(btnLogin);
        pnlButtons.Controls.Add(btnCancel);

        // layRoot
        layRoot.AutoSize = true;
        layRoot.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        layRoot.ColumnCount = 2;
        layRoot.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layRoot.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layRoot.Name = "layRoot";
        layRoot.Padding = new Padding(24, 20, 24, 20);
        layRoot.RowCount = 6;
        for (var row = 0; row < 6; row++)
            layRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layRoot.Controls.Add(pnlLogo, 0, 0);
        layRoot.SetColumnSpan(pnlLogo, 2);
        layRoot.Controls.Add(lblTitle, 0, 1);
        layRoot.SetColumnSpan(lblTitle, 2);
        layRoot.Controls.Add(lblParticulars, 0, 2);
        layRoot.SetColumnSpan(lblParticulars, 2);
        layRoot.Controls.Add(lblUsername, 0, 3);
        layRoot.Controls.Add(txtUsername, 1, 3);
        layRoot.Controls.Add(lblPassword, 0, 4);
        layRoot.Controls.Add(txtPassword, 1, 4);
        layRoot.Controls.Add(pnlButtons, 0, 5);
        layRoot.SetColumnSpan(pnlButtons, 2);

        // frmLogin
        AcceptButton = btnLogin;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;

        CancelButton = btnCancel;
        // A starting size only. PhxFormBase measures the layout panel
        // once the theme font is applied and grows the window to fit,
        // which is what keeps this window whole at any display scaling.
        ClientSize = new Size(404, 201);
        Controls.Add(layRoot);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "frmLogin";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "PharmaX - Sign in";
        ResumeLayout(false);
        PerformLayout();
    }
}
