namespace PharmaX.Desktop.Features.Login;

partial class frmChangePassword
{
    private System.ComponentModel.IContainer components = null!;
    private Label lblPrompt = null!;
    private Label lblCurrent = null!;
    private Label lblNew = null!;
    private Label lblConfirm = null!;
    private Label lblError = null!;
    private TextBox txtCurrent = null!;
    private TextBox txtNew = null!;
    private TextBox txtConfirm = null!;
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
        lblPrompt = new Label();
        lblCurrent = new Label();
        lblNew = new Label();
        lblConfirm = new Label();
        lblError = new Label();
        txtCurrent = new TextBox();
        txtNew = new TextBox();
        txtConfirm = new TextBox();
        btnOk = new Button();
        btnCancel = new Button();
        SuspendLayout();

        lblPrompt.AutoSize = false;
        lblPrompt.Location = new Point(20, 16);
        lblPrompt.Name = "lblPrompt";
        lblPrompt.Size = new Size(400, 34);
        lblPrompt.TabIndex = 0;

        lblCurrent.AutoSize = true;
        lblCurrent.Location = new Point(20, 62);
        lblCurrent.Name = "lblCurrent";
        lblCurrent.Size = new Size(103, 15);
        lblCurrent.TabIndex = 1;
        lblCurrent.Text = "C&urrent password";

        txtCurrent.Location = new Point(170, 59);
        txtCurrent.Name = "txtCurrent";
        txtCurrent.PlaceholderText = "The password you use now";
        txtCurrent.Size = new Size(250, 23);
        txtCurrent.TabIndex = 2;
        txtCurrent.UseSystemPasswordChar = true;

        lblNew.AutoSize = true;
        lblNew.Location = new Point(20, 96);
        lblNew.Name = "lblNew";
        lblNew.Size = new Size(87, 15);
        lblNew.TabIndex = 3;
        lblNew.Text = "&New password";

        txtNew.Location = new Point(170, 93);
        txtNew.Name = "txtNew";
        txtNew.PlaceholderText = "At least 8 characters, e.g. a short phrase";
        txtNew.Size = new Size(250, 23);
        txtNew.TabIndex = 4;
        txtNew.UseSystemPasswordChar = true;

        lblConfirm.AutoSize = true;
        lblConfirm.Location = new Point(20, 130);
        lblConfirm.Name = "lblConfirm";
        lblConfirm.Size = new Size(105, 15);
        lblConfirm.TabIndex = 5;
        lblConfirm.Text = "&Confirm password";

        txtConfirm.Location = new Point(170, 127);
        txtConfirm.Name = "txtConfirm";
        txtConfirm.PlaceholderText = "The new password again";
        txtConfirm.Size = new Size(250, 23);
        txtConfirm.TabIndex = 6;
        txtConfirm.UseSystemPasswordChar = true;

        lblError.AutoSize = false;
        lblError.ForeColor = Mvp.PhxTheme.ProblemText;
        lblError.Location = new Point(20, 158);
        lblError.Name = "lblError";
        lblError.Size = new Size(400, 32);
        lblError.TabIndex = 7;

        btnOk.Location = new Point(264, 196);
        btnOk.Name = "btnOk";
        btnOk.Size = new Size(75, 28);
        btnOk.TabIndex = 8;
        btnOk.Text = "&Change";
        btnOk.UseVisualStyleBackColor = true;

        btnCancel.DialogResult = DialogResult.Cancel;
        btnCancel.Location = new Point(345, 196);
        btnCancel.Name = "btnCancel";
        btnCancel.Size = new Size(75, 28);
        btnCancel.TabIndex = 9;
        btnCancel.Text = "Cancel";
        btnCancel.UseVisualStyleBackColor = true;

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(444, 240);
        Controls.Add(btnCancel);
        Controls.Add(btnOk);
        Controls.Add(lblError);
        Controls.Add(txtConfirm);
        Controls.Add(lblConfirm);
        Controls.Add(txtNew);
        Controls.Add(lblNew);
        Controls.Add(txtCurrent);
        Controls.Add(lblCurrent);
        Controls.Add(lblPrompt);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "frmChangePassword";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Change password";
        ResumeLayout(false);
        PerformLayout();
    }
}
