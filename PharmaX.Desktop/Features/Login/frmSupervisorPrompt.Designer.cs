namespace PharmaX.Desktop.Features.Login;

partial class frmSupervisorPrompt
{
    private System.ComponentModel.IContainer components = null!;
    private Label lblReason = null!;
    private Label lblUsername = null!;
    private Label lblPassword = null!;
    private Label lblError = null!;
    private TextBox txtUsername = null!;
    private TextBox txtPassword = null!;
    private Button btnOk = null!;
    private Button btnCancel = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        lblReason = new Label();
        lblUsername = new Label();
        lblPassword = new Label();
        lblError = new Label();
        txtUsername = new TextBox();
        txtPassword = new TextBox();
        btnOk = new Button();
        btnCancel = new Button();
        SuspendLayout();

        lblReason.AutoSize = false;
        lblReason.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        lblReason.Location = new Point(20, 16);
        lblReason.Name = "lblReason";
        lblReason.Size = new Size(400, 40);
        lblReason.TabIndex = 0;

        lblUsername.AutoSize = true;
        lblUsername.Location = new Point(20, 68);
        lblUsername.Name = "lblUsername";
        lblUsername.Size = new Size(63, 15);
        lblUsername.TabIndex = 1;
        lblUsername.Text = "&Username";

        txtUsername.Location = new Point(140, 65);
        txtUsername.Name = "txtUsername";
        txtUsername.PlaceholderText = "Who is authorising, e.g. tmoyo";
        txtUsername.Size = new Size(280, 23);
        txtUsername.TabIndex = 2;

        lblPassword.AutoSize = true;
        lblPassword.Location = new Point(20, 102);
        lblPassword.Name = "lblPassword";
        lblPassword.Size = new Size(60, 15);
        lblPassword.TabIndex = 3;
        lblPassword.Text = "&Password";

        txtPassword.Location = new Point(140, 99);
        txtPassword.Name = "txtPassword";
        txtPassword.PlaceholderText = "Password for that username";
        txtPassword.Size = new Size(280, 23);
        txtPassword.TabIndex = 4;
        txtPassword.UseSystemPasswordChar = true;

        lblError.AutoSize = false;
        lblError.ForeColor = Mvp.PhxTheme.ProblemText;
        lblError.Location = new Point(20, 130);
        lblError.Name = "lblError";
        lblError.Size = new Size(400, 32);
        lblError.TabIndex = 5;

        btnOk.Location = new Point(264, 170);
        btnOk.Name = "btnOk";
        btnOk.Size = new Size(75, 28);
        btnOk.TabIndex = 6;
        btnOk.Text = "&Authorise";
        btnOk.UseVisualStyleBackColor = true;

        btnCancel.DialogResult = DialogResult.Cancel;
        btnCancel.Location = new Point(345, 170);
        btnCancel.Name = "btnCancel";
        btnCancel.Size = new Size(75, 28);
        btnCancel.TabIndex = 7;
        btnCancel.Text = "Cancel";
        btnCancel.UseVisualStyleBackColor = true;

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(444, 214);
        Controls.Add(btnCancel);
        Controls.Add(btnOk);
        Controls.Add(lblError);
        Controls.Add(txtPassword);
        Controls.Add(lblPassword);
        Controls.Add(txtUsername);
        Controls.Add(lblUsername);
        Controls.Add(lblReason);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "frmSupervisorPrompt";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Authorisation required";
        ResumeLayout(false);
        PerformLayout();
    }
}
