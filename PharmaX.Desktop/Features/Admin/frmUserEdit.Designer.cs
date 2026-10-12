using PharmaX.Desktop.Controls;

namespace PharmaX.Desktop.Features.Admin;

partial class frmUserEdit
{
    private System.ComponentModel.IContainer components = null!;
    private Label lblUsername = null!;
    private Label lblDisplayName = null!;
    private Label lblRoles = null!;
    private Label lblDiscountLimit = null!;
    private Label lblRegistration = null!;
    private Label lblState = null!;
    private TextBox txtUsername = null!;
    private TextBox txtDisplayName = null!;
    private TextBox txtRegistration = null!;
    private MoneyTextBox txtDiscountLimit = null!;
    private CheckedListBox lstRoles = null!;
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
        lblUsername = new Label();
        lblDisplayName = new Label();
        lblRoles = new Label();
        lblDiscountLimit = new Label();
        lblRegistration = new Label();
        lblState = new Label();
        txtUsername = new TextBox();
        txtDisplayName = new TextBox();
        txtRegistration = new TextBox();
        txtDiscountLimit = new MoneyTextBox();
        lstRoles = new CheckedListBox();
        btnSave = new Button();
        btnSave.Name = "btnSave";
        btnCancel = new Button();
        btnCancel.Name = "btnCancel";
        SuspendLayout();

        lblUsername.AutoSize = true;
        lblUsername.Location = new Point(20, 24);
        lblUsername.Text = "&Username";

        txtUsername.Location = new Point(150, 20);
        txtUsername.MaxLength = 50;
        txtUsername.Name = "txtUsername";
        txtUsername.PlaceholderText = "e.g. t.moyo";
        txtUsername.Size = new Size(220, 23);
        txtUsername.TabIndex = 0;

        lblDisplayName.AutoSize = true;
        lblDisplayName.Location = new Point(20, 58);
        lblDisplayName.Text = "&Name";

        txtDisplayName.Location = new Point(150, 54);
        txtDisplayName.MaxLength = 120;
        txtDisplayName.Name = "txtDisplayName";
        txtDisplayName.PlaceholderText = "e.g. Tendai Moyo";
        txtDisplayName.Size = new Size(300, 23);
        txtDisplayName.TabIndex = 1;

        lblRoles.AutoSize = true;
        lblRoles.Location = new Point(20, 92);
        lblRoles.Text = "&Roles";

        // Checked list rather than a single dropdown: a small pharmacy
        // has one person who is both the manager and the pharmacist.
        lstRoles.CheckOnClick = true;
        lstRoles.IntegralHeight = false;
        lstRoles.Location = new Point(150, 88);
        lstRoles.Name = "lstRoles";
        lstRoles.Size = new Size(300, 130);
        lstRoles.TabIndex = 2;

        lblDiscountLimit.AutoSize = true;
        lblDiscountLimit.Location = new Point(20, 232);
        lblDiscountLimit.Text = "&Discount limit %";

        txtDiscountLimit.Location = new Point(150, 228);
        txtDiscountLimit.Name = "txtDiscountLimit";
        txtDiscountLimit.PlaceholderText = "e.g. 10";
        txtDiscountLimit.Size = new Size(90, 26);
        txtDiscountLimit.TabIndex = 3;

        lblRegistration.AutoSize = true;
        lblRegistration.Location = new Point(20, 266);
        lblRegistration.Name = "lblRegistration";
        lblRegistration.Text = "Re&gistration no.";
        lblRegistration.Visible = false;

        txtRegistration.Location = new Point(150, 262);
        txtRegistration.MaxLength = 30;
        txtRegistration.Name = "txtRegistration";
        txtRegistration.PlaceholderText = "e.g. P00123";
        txtRegistration.Size = new Size(160, 23);
        txtRegistration.TabIndex = 4;
        txtRegistration.Visible = false;

        lblState.AutoSize = false;
        lblState.ForeColor = SystemColors.GrayText;
        lblState.Location = new Point(20, 300);
        lblState.Name = "lblState";
        lblState.Size = new Size(440, 20);

        btnSave.Location = new Point(284, 332);
        btnSave.Size = new Size(80, 30);
        btnSave.TabIndex = 5;
        btnSave.Text = "&Save";
        btnSave.UseVisualStyleBackColor = true;

        btnCancel.Location = new Point(370, 332);
        btnCancel.Size = new Size(80, 30);
        btnCancel.TabIndex = 6;
        btnCancel.Text = "Cancel";
        btnCancel.UseVisualStyleBackColor = true;

        AcceptButton = btnSave;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = btnCancel;
        ClientSize = new Size(474, 378);
        Controls.AddRange(new Control[]
        {
            lblUsername, txtUsername, lblDisplayName, txtDisplayName,
            lblRoles, lstRoles, lblDiscountLimit, txtDiscountLimit,
            lblRegistration, txtRegistration, lblState, btnSave, btnCancel
        });
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "frmUserEdit";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - User";
        ResumeLayout(false);
        PerformLayout();
    }
}
