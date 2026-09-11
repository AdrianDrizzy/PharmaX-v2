namespace PharmaX.Desktop.Features.Login;

partial class frmLogin
{
    private System.ComponentModel.IContainer components = null!;
    private Label lblTitle = null!;
    private Panel pnlLogo = null!;
    private Label lblUsername = null!;
    private Label lblPassword = null!;
    private TextBox txtUsername = null!;
    private TextBox txtPassword = null!;
    private Button btnLogin = null!;
    private Button btnCancel = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        lblTitle = new Label();
        pnlLogo = new Panel();
        lblUsername = new Label();
        lblPassword = new Label();
        txtUsername = new TextBox();
        txtPassword = new TextBox();
        btnLogin = new Button();
        btnCancel = new Button();
        SuspendLayout();

        // lblTitle
        lblTitle.AutoSize = true;
        lblTitle.Font = new Font("Segoe UI", 16F, FontStyle.Bold);
        lblTitle.Location = new Point(24, 20);
        lblTitle.Name = "lblTitle";
        lblTitle.Size = new Size(110, 30);
        lblTitle.TabIndex = 0;
        lblTitle.Text = "PharmaX";

        // pnlLogo - the pharmacy's own mark, if it has set one.
        pnlLogo.Location = new Point(228, 12);
        pnlLogo.Name = "pnlLogo";
        pnlLogo.Size = new Size(152, 46);
        pnlLogo.Paint += (_, e) => PaintLogo(e);

        // lblUsername
        lblUsername.AutoSize = true;
        lblUsername.Location = new Point(24, 72);
        lblUsername.Name = "lblUsername";
        lblUsername.Size = new Size(63, 15);
        lblUsername.TabIndex = 1;
        lblUsername.Text = "&Username";

        // txtUsername
        txtUsername.Location = new Point(120, 69);
        txtUsername.Name = "txtUsername";
        txtUsername.Size = new Size(250, 23);
        txtUsername.TabIndex = 2;

        // lblPassword
        lblPassword.AutoSize = true;
        lblPassword.Location = new Point(24, 108);
        lblPassword.Name = "lblPassword";
        lblPassword.Size = new Size(60, 15);
        lblPassword.TabIndex = 3;
        lblPassword.Text = "&Password";

        // txtPassword
        txtPassword.Location = new Point(120, 105);
        txtPassword.Name = "txtPassword";
        txtPassword.Size = new Size(250, 23);
        txtPassword.TabIndex = 4;
        txtPassword.UseSystemPasswordChar = true;

        // btnLogin
        btnLogin.Location = new Point(214, 148);
        btnLogin.Name = "btnLogin";
        btnLogin.Size = new Size(75, 28);
        btnLogin.TabIndex = 5;
        btnLogin.Text = "&Log in";
        btnLogin.UseVisualStyleBackColor = true;

        // btnCancel
        btnCancel.Location = new Point(295, 148);
        btnCancel.Name = "btnCancel";
        btnCancel.Size = new Size(75, 28);
        btnCancel.TabIndex = 6;
        btnCancel.Text = "&Cancel";
        btnCancel.UseVisualStyleBackColor = true;

        // frmLogin
        AcceptButton = btnLogin;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = btnCancel;
        ClientSize = new Size(404, 201);
        Controls.Add(btnCancel);
        Controls.Add(btnLogin);
        Controls.Add(txtPassword);
        Controls.Add(lblPassword);
        Controls.Add(txtUsername);
        Controls.Add(lblUsername);
        Controls.Add(lblTitle);
        Controls.Add(pnlLogo);
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
