using PharmaX.Desktop.Controls;

namespace PharmaX.Desktop.Features.Inventory;

partial class frmProductEdit
{
    private System.ComponentModel.IContainer components = null!;
    private TextBox txtCode = null!;
    private TextBox txtDescription = null!;
    private TextBox txtGenericName = null!;
    private TextBox txtTaxCode = null!;
    private TextBox txtPackSize = null!;
    private MoneyTextBox txtRetail = null!;
    private MoneyTextBox txtCost = null!;
    private MoneyTextBox txtVatRate = null!;
    private MoneyTextBox txtReorderLevel = null!;
    private NumericUpDown numSchedule = null!;
    private ComboBox cboDepartment = null!;
    private ComboBox cboCategory = null!;
    private ComboBox cboManufacturer = null!;
    private CheckBox chkBatchTracked = null!;
    private Label lblOnHand = null!;
    private Label lblOnHandValue = null!;
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
        txtCode.Name = "txtCode";
        txtDescription = new TextBox();
        txtDescription.Name = "txtDescription";
        txtGenericName = new TextBox();
        txtGenericName.Name = "txtGenericName";
        txtTaxCode = new TextBox();
        txtTaxCode.Name = "txtTaxCode";
        txtPackSize = new TextBox();
        txtPackSize.Name = "txtPackSize";
        txtRetail = new MoneyTextBox();
        txtRetail.Name = "txtRetail";
        txtCost = new MoneyTextBox();
        txtCost.Name = "txtCost";
        txtVatRate = new MoneyTextBox();
        txtVatRate.Name = "txtVatRate";
        txtReorderLevel = new MoneyTextBox();
        txtReorderLevel.Name = "txtReorderLevel";
        numSchedule = new NumericUpDown();
        cboDepartment = new ComboBox();
        cboDepartment.Name = "cboDepartment";
        cboCategory = new ComboBox();
        cboCategory.Name = "cboCategory";
        cboManufacturer = new ComboBox();
        cboManufacturer.Name = "cboManufacturer";
        chkBatchTracked = new CheckBox();
        lblOnHand = new Label();
        lblOnHandValue = new Label();
        btnSave = new Button();
        btnCancel = new Button();
        ((System.ComponentModel.ISupportInitialize)numSchedule).BeginInit();
        SuspendLayout();

        Field("&Code", txtCode, 0, 160);
        Field("&Description", txtDescription, 1, 380);
        Field("&Generic name", txtGenericName, 2, 380);
        Field("&Retail price", txtRetail, 3, 140);
        Field("Cost &price", txtCost, 4, 140);
        Field("&VAT rate %", txtVatRate, 5, 140);
        Field("&Tax code", txtTaxCode, 6, 60);
        Field("Sc&hedule", numSchedule, 7, 60);
        Field("Reorder &level", txtReorderLevel, 8, 140);
        Field("Pack si&ze", txtPackSize, 9, 140);

        // Where the product is filed. Three lists the pharmacy
        // maintains itself, so they are chosen rather than typed.
        Field("Dep&artment", cboDepartment, 10, 240);
        Field("Categor&y", cboCategory, 11, 240);
        Field("&Manufacturer", cboManufacturer, 12, 240);

        foreach (var combo in new[]
                 { cboDepartment, cboCategory, cboManufacturer })
            combo.DropDownStyle = ComboBoxStyle.DropDownList;

        txtCode.MaxLength = 30;
        txtDescription.MaxLength = 200;
        txtGenericName.MaxLength = 200;
        txtTaxCode.MaxLength = 5;
        txtTaxCode.CharacterCasing = CharacterCasing.Upper;
        txtPackSize.MaxLength = 50;

        numSchedule.Maximum = 8;
        numSchedule.Minimum = 0;
        numSchedule.Name = "numSchedule";

        chkBatchTracked.AutoSize = true;
        chkBatchTracked.Location = new Point(150, 20 + 13 * 34);
        chkBatchTracked.Name = "chkBatchTracked";
        chkBatchTracked.Size = new Size(180, 19);
        chkBatchTracked.TabIndex = 13;
        chkBatchTracked.Text = "&Batch tracked (FEFO applies)";
        chkBatchTracked.UseVisualStyleBackColor = true;

        // Read-only on purpose: stock moves through receipts, sales and
        // adjustments, never by typing a new figure into a form.
        lblOnHand.AutoSize = true;
        lblOnHand.Location = new Point(20, 24 + 14 * 34);
        lblOnHand.Text = "On hand";

        lblOnHandValue.AutoSize = false;
        lblOnHandValue.Font = new Font("Consolas", 10F);
        lblOnHandValue.Location = new Point(150, 24 + 14 * 34);
        lblOnHandValue.Name = "lblOnHandValue";
        lblOnHandValue.Size = new Size(140, 20);
        lblOnHandValue.Text = "0.000";

        btnSave.Location = new Point(388, 40 + 15 * 34);
        btnSave.Name = "btnSave";
        btnSave.Size = new Size(80, 30);
        btnSave.TabIndex = 14;
        btnSave.Text = "&Save";
        btnSave.UseVisualStyleBackColor = true;

        btnCancel.Location = new Point(474, 40 + 15 * 34);
        btnCancel.Name = "btnCancel";
        btnCancel.Size = new Size(80, 30);
        btnCancel.TabIndex = 15;
        btnCancel.Text = "Cancel";
        btnCancel.UseVisualStyleBackColor = true;

        Controls.Add(chkBatchTracked);
        Controls.Add(lblOnHand);
        Controls.Add(lblOnHandValue);
        Controls.Add(btnSave);
        Controls.Add(btnCancel);

        AcceptButton = btnSave;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = btnCancel;
        ClientSize = new Size(574, 90 + 15 * 34);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "frmProductEdit";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Product";
        ((System.ComponentModel.ISupportInitialize)numSchedule).EndInit();
        ResumeLayout(false);
        PerformLayout();
    }

    /// <summary>
    /// One labelled field on a fixed grid. A helper rather than sixty
    /// lines of Location arithmetic that nobody will keep aligned.
    /// </summary>
    private void Field(string caption, Control input, int row, int width)
    {
        var y = 20 + row * 34;

        Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(20, y + 4),
            Text = caption
        });

        input.Location = new Point(150, y);
        input.Size = new Size(width, 23);
        input.TabIndex = row;
        Controls.Add(input);
    }
}
