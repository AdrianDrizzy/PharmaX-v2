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
    private MoneyTextBox txtUnitsPerPack = null!;
    private TextBox txtBarcodes = null!;
    private MoneyTextBox txtRetail = null!;
    private MoneyTextBox txtCost = null!;
    private MoneyTextBox txtVatRate = null!;
    private MoneyTextBox txtReorderLevel = null!;
    private NumericUpDown numSchedule = null!;
    private ComboBox cboDepartment = null!;
    private ComboBox cboCategory = null!;
    private ComboBox cboManufacturer = null!;
    private CheckBox chkBatchTracked = null!;
    private CheckBox chkActive = null!;
    private MoneyTextBox txtOnHand = null!;
    private Label lblOnHandNote = null!;
    private Label lblBatches = null!;
    private PhxGrid grdBatches = null!;
    private Label lblBatchHint = null!;
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
        txtCode.PlaceholderText = "e.g. PAN500";
        txtDescription = new TextBox();
        txtDescription.Name = "txtDescription";
        txtDescription.PlaceholderText = "e.g. Panado 500mg tablets 20s";
        txtGenericName = new TextBox();
        txtGenericName.Name = "txtGenericName";
        txtGenericName.PlaceholderText = "e.g. Paracetamol (optional)";
        txtTaxCode = new TextBox();
        txtTaxCode.Name = "txtTaxCode";
        txtTaxCode.PlaceholderText = "e.g. A";
        txtPackSize = new TextBox();
        txtPackSize.Name = "txtPackSize";
        txtPackSize.PlaceholderText = "e.g. 20 tablets";
        txtUnitsPerPack = new MoneyTextBox();
        txtUnitsPerPack.Name = "txtUnitsPerPack";
        txtUnitsPerPack.PlaceholderText = "e.g. 20";
        txtBarcodes = new TextBox();
        txtBarcodes.Name = "txtBarcodes";
        txtRetail = new MoneyTextBox();
        txtRetail.Name = "txtRetail";
        txtRetail.PlaceholderText = "0.00";
        txtCost = new MoneyTextBox();
        txtCost.Name = "txtCost";
        txtCost.PlaceholderText = "0.00";
        txtVatRate = new MoneyTextBox();
        txtVatRate.Name = "txtVatRate";
        txtVatRate.PlaceholderText = "e.g. 15";
        txtReorderLevel = new MoneyTextBox();
        txtReorderLevel.Name = "txtReorderLevel";
        txtReorderLevel.PlaceholderText = "e.g. 30";
        numSchedule = new NumericUpDown();
        cboDepartment = new ComboBox();
        cboDepartment.Name = "cboDepartment";
        cboCategory = new ComboBox();
        cboCategory.Name = "cboCategory";
        cboManufacturer = new ComboBox();
        cboManufacturer.Name = "cboManufacturer";
        chkBatchTracked = new CheckBox();
        chkActive = new CheckBox();
        txtOnHand = new MoneyTextBox();
        txtOnHand.Name = "txtOnHand";
        txtOnHand.PlaceholderText = "e.g. 30";
        lblOnHandNote = new Label();
        lblBatches = new Label();
        grdBatches = new PhxGrid();
        lblBatchHint = new Label();
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

        // Beside the pack description: how many tablets, capsules or
        // millilitres the priced pack holds, which is what a script for
        // fewer is priced against.
        Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(310, 24 + 9 * 34),
            Text = "U&nits per pack"
        });
        txtUnitsPerPack.AccessibleName = "Units per pack";
        txtUnitsPerPack.Location = new Point(410, 20 + 9 * 34);
        txtUnitsPerPack.Size = new Size(100, 23);
        txtUnitsPerPack.TabIndex = 9 * 2 + 1;
        Controls.Add(txtUnitsPerPack);

        // Where the product is filed. Three lists the pharmacy
        // maintains itself, so they are chosen rather than typed.
        Field("Dep&artment", cboDepartment, 10, 240);
        Field("Categor&y", cboCategory, 11, 240);
        Field("&Manufacturer", cboManufacturer, 12, 240);

        foreach (var combo in new[]
                 { cboDepartment, cboCategory, cboManufacturer })
            combo.DropDownStyle = ComboBoxStyle.DropDownList;

        // What the till and the dispensary scan. Several, because the
        // same product comes in from more than one supplier's packing.
        // Two lines tall and taking a return, so a scanner that ends
        // each code with Enter adds a line rather than saving the form.
        Field("Barcod&es", txtBarcodes, 13, 380);
        txtBarcodes.AcceptsReturn = true;
        txtBarcodes.MaxLength = 500;
        txtBarcodes.Multiline = true;
        txtBarcodes.PlaceholderText =
            "Scan or type; one per line, or separated by commas";
        txtBarcodes.ScrollBars = ScrollBars.Vertical;
        txtBarcodes.Size = new Size(380, 57);

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
        chkBatchTracked.Location = new Point(150, 20 + 15 * 34);
        chkBatchTracked.Name = "chkBatchTracked";
        chkBatchTracked.Size = new Size(180, 19);
        chkBatchTracked.TabIndex = 15 * 2;
        chkBatchTracked.Text = "&Batch tracked (FEFO applies)";
        chkBatchTracked.UseVisualStyleBackColor = true;

        // Beside batch tracking: unticked is the product discontinued,
        // kept for the history it already has.
        chkActive.AutoSize = true;
        chkActive.Checked = true;
        chkActive.Location = new Point(360, 20 + 15 * 34);
        chkActive.Name = "chkActive";
        chkActive.Size = new Size(160, 19);
        chkActive.TabIndex = 15 * 2 + 1;
        chkActive.Text = "Act&ive (offered for sale)";
        chkActive.UseVisualStyleBackColor = true;

        // A count, typed as one. Saving a changed figure posts the
        // difference as a stock adjustment, the same as the list's cell
        // does, so the movement report still explains the shelf. Once
        // the stock is on batches this box is their total, and the
        // counting is done a batch at a time on the right.
        Field("&On hand", txtOnHand, 16, 140);

        lblOnHandNote.AutoSize = false;
        lblOnHandNote.ForeColor = SystemColors.GrayText;
        lblOnHandNote.Location = new Point(300, 24 + 16 * 34);
        lblOnHandNote.Name = "lblOnHandNote";
        lblOnHandNote.Size = new Size(250, 20);

        // The same shelf a batch at a time, as the list's batch view
        // shows it, and corrected the same way: the number on the pack,
        // the date on it (or the days it has left), and how many are
        // there. Typing on the last row adds a batch.
        lblBatches.AutoSize = true;
        lblBatches.Location = new Point(570, 24);
        lblBatches.TabIndex = 16 * 2 + 1;
        lblBatches.Text = "Batches and e&xpiry";

        grdBatches.AccessibleName = "Batches and expiry";
        grdBatches.Location = new Point(570, 48);
        grdBatches.Name = "grdBatches";
        grdBatches.Size = new Size(390, 20 + 15 * 34 - 48);
        grdBatches.TabIndex = 16 * 2 + 1;

        var batchNumber = grdBatches.AddColumn("BatchNumber", "Batch", 110);
        batchNumber.MaxInputLength = 50;
        grdBatches.AddColumn("Expiry", "Expires", 90);
        grdBatches.AddColumn("DaysLeft", "Days left", 70, rightAlign: true);
        grdBatches.AddColumn("QuantityOnHand", "On hand", 80,
            rightAlign: true);

        // A small form of its own rather than a list: typed into, a cell
        // at a time, and Tab moves along the row. The permissions close
        // it again for whoever may not change it.
        grdBatches.ReadOnly = false;
        grdBatches.AllowUserToAddRows = true;
        grdBatches.AllowUserToDeleteRows = true;
        grdBatches.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
        grdBatches.SelectionMode = DataGridViewSelectionMode.CellSelect;
        grdBatches.StandardTab = false;

        lblBatchHint.AutoSize = false;
        lblBatchHint.ForeColor = SystemColors.GrayText;
        lblBatchHint.Location = new Point(570, 24 + 15 * 34);
        lblBatchHint.Name = "lblBatchHint";
        lblBatchHint.Size = new Size(390, 54);
        lblBatchHint.Text =
            "Type on the last row to add a batch. Days left moves the "
            + "expiry date. Set a batch's On hand to 0 to write it off.";

        btnSave.Location = new Point(794, 40 + 17 * 34);
        btnSave.Name = "btnSave";
        btnSave.Size = new Size(80, 30);
        btnSave.TabIndex = 17 * 2;
        btnSave.Text = "&Save";
        btnSave.UseVisualStyleBackColor = true;

        btnCancel.Location = new Point(880, 40 + 17 * 34);
        btnCancel.Name = "btnCancel";
        btnCancel.Size = new Size(80, 30);
        btnCancel.TabIndex = 17 * 2 + 1;
        btnCancel.Text = "Cancel";
        btnCancel.UseVisualStyleBackColor = true;

        Controls.Add(chkBatchTracked);
        Controls.Add(chkActive);
        Controls.Add(lblOnHandNote);
        Controls.Add(lblBatches);
        Controls.Add(grdBatches);
        Controls.Add(lblBatchHint);
        Controls.Add(btnSave);
        Controls.Add(btnCancel);

        AcceptButton = btnSave;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = btnCancel;
        ClientSize = new Size(980, 90 + 17 * 34);
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
    /// <remarks>
    /// The input is named after its own caption. Left to work it out,
    /// assistive technology took the label before each box in tab order,
    /// and the units-per-pack box sharing a tab stop pushed every list
    /// below it one caption out - Department was announced as "Units per
    /// pack" (ADM-OBS-05). Tab stops go up in twos so that a field beside
    /// another can sit between them.
    /// </remarks>
    private void Field(string caption, Control input, int row, int width)
    {
        var y = 20 + row * 34;

        Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(20, y + 4),
            TabIndex = row * 2,
            Text = caption
        });

        input.AccessibleName =
            caption.Replace("&", "", StringComparison.Ordinal);
        input.Location = new Point(150, y);
        input.Size = new Size(width, 23);
        input.TabIndex = row * 2;
        Controls.Add(input);
    }
}
