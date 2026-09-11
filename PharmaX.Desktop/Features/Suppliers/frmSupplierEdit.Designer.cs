namespace PharmaX.Desktop.Features.Suppliers;

partial class frmSupplierEdit
{
    private System.ComponentModel.IContainer components = null!;
    private TextBox txtCode = null!;
    private TextBox txtName = null!;
    private TextBox txtContactName = null!;
    private TextBox txtTelephone = null!;
    private TextBox txtEmail = null!;
    private TextBox txtAccountNumber = null!;
    private NumericUpDown numTermsDays = null!;
    private NumericUpDown numLeadTimeDays = null!;
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
        txtCode = new TextBox();
        txtName = new TextBox();
        txtContactName = new TextBox();
        txtTelephone = new TextBox();
        txtEmail = new TextBox();
        txtAccountNumber = new TextBox();
        numTermsDays = new NumericUpDown();
        numLeadTimeDays = new NumericUpDown();
        btnSave = new Button();
        btnCancel = new Button();
        ((System.ComponentModel.ISupportInitialize)numTermsDays).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numLeadTimeDays).BeginInit();
        SuspendLayout();

        Field("&Code", txtCode, 0, 140, 30);
        Field("&Name", txtName, 1, 300, 150);
        Field("Co&ntact", txtContactName, 2, 240, 120);
        Field("&Telephone", txtTelephone, 3, 180, 30);
        Field("&Email", txtEmail, 4, 300, 150);
        Field("&Account no.", txtAccountNumber, 5, 200, 50);

        Label(20, 20 + 6 * 34 + 4, "Terms (&days)");
        numTermsDays.Location = new Point(150, 20 + 6 * 34);
        numTermsDays.Maximum = 365;
        numTermsDays.Name = "numTermsDays";
        numTermsDays.Size = new Size(70, 23);
        numTermsDays.TabIndex = 6;
        numTermsDays.Value = 30;

        // The reorder calculation adds this to today to decide when to
        // order, so it is a supplier fact rather than a product one.
        Label(20, 20 + 7 * 34 + 4, "&Lead time (days)");
        numLeadTimeDays.Location = new Point(150, 20 + 7 * 34);
        numLeadTimeDays.Maximum = 365;
        numLeadTimeDays.Name = "numLeadTimeDays";
        numLeadTimeDays.Size = new Size(70, 23);
        numLeadTimeDays.TabIndex = 7;
        numLeadTimeDays.Value = 2;

        btnSave.Location = new Point(300, 20 + 8 * 34 + 10);
        btnSave.Size = new Size(80, 30);
        btnSave.TabIndex = 8;
        btnSave.Text = "&Save";
        btnSave.UseVisualStyleBackColor = true;

        btnCancel.Location = new Point(386, 20 + 8 * 34 + 10);
        btnCancel.Size = new Size(80, 30);
        btnCancel.TabIndex = 9;
        btnCancel.Text = "Cancel";
        btnCancel.UseVisualStyleBackColor = true;

        Controls.Add(numTermsDays);
        Controls.Add(numLeadTimeDays);
        Controls.Add(btnSave);
        Controls.Add(btnCancel);

        AcceptButton = btnSave;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = btnCancel;
        ClientSize = new Size(490, 20 + 8 * 34 + 56);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "frmSupplierEdit";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Supplier";
        ((System.ComponentModel.ISupportInitialize)numTermsDays).EndInit();
        ((System.ComponentModel.ISupportInitialize)numLeadTimeDays).EndInit();
        ResumeLayout(false);
        PerformLayout();
    }

    private void Field(string caption, Control input, int row, int width,
                       int maxLength)
    {
        var y = 20 + row * 34;
        Label(20, y + 4, caption);

        input.Location = new Point(150, y);
        input.Size = new Size(width, 23);
        input.TabIndex = row;

        if (input is TextBox box) box.MaxLength = maxLength;

        Controls.Add(input);
    }

    private void Label(int x, int y, string text) =>
        Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(x, y),
            Text = text
        });
}
