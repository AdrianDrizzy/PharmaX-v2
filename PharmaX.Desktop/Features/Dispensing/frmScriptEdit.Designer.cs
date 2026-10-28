namespace PharmaX.Desktop.Features.Dispensing;

partial class frmScriptEdit
{
    private System.ComponentModel.IContainer components = null!;
    private Label lblPatient = null!;
    private TextBox txtPrescriber = null!;
    private TextBox txtPrescriberNumber = null!;
    private DateTimePicker dtpPrescribedOn = null!;
    private CheckBox chkHasValidUntil = null!;
    private DateTimePicker dtpValidUntil = null!;
    private NumericUpDown numRepeats = null!;
    private TextBox txtFindProduct = null!;
    private PharmaX.Desktop.Controls.CellTabGrid grdItems = null!;
    private Button btnAddItem = null!;
    private Button btnRemoveItem = null!;
    private Button btnFromHistory = null!;
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
        lblPatient = new Label();
        txtPrescriber = new TextBox();
        txtPrescriber.Name = "txtPrescriber";
        txtPrescriberNumber = new TextBox();
        txtPrescriberNumber.Name = "txtPrescriberNumber";
        dtpPrescribedOn = new DateTimePicker();
        chkHasValidUntil = new CheckBox();
        dtpValidUntil = new DateTimePicker();
        numRepeats = new NumericUpDown();
        txtFindProduct = new TextBox();
        txtFindProduct.Name = "txtFindProduct";
        grdItems = new PharmaX.Desktop.Controls.CellTabGrid();
        btnAddItem = new Button();
        btnAddItem.Name = "btnAddItem";
        btnRemoveItem = new Button();
        btnRemoveItem.Name = "btnRemoveItem";
        btnFromHistory = new Button();
        btnFromHistory.Name = "btnFromHistory";
        btnSave = new Button();
        btnSave.Name = "btnSave";
        btnCancel = new Button();
        btnCancel.Name = "btnCancel";
        ((System.ComponentModel.ISupportInitialize)numRepeats).BeginInit();
        ((System.ComponentModel.ISupportInitialize)grdItems).BeginInit();
        SuspendLayout();

        lblPatient.AutoSize = false;
        lblPatient.AutoEllipsis = true;
        lblPatient.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
        lblPatient.Location = new Point(20, 16);
        lblPatient.Name = "lblPatient";
        lblPatient.Size = new Size(820, 24);

        Field("&Prescriber", txtPrescriber, 1, 300);
        txtPrescriber.MaxLength = 150;
        txtPrescriber.PlaceholderText = "Doctor, clinic, code or practice number";

        Field("Prescriber &no.", txtPrescriberNumber, 2, 160);
        txtPrescriberNumber.MaxLength = 30;
        txtPrescriberNumber.PlaceholderText = "e.g. 041234 (optional)";

        Label(20, 20 + 3 * 34 + 4, "Prescri&bed on");
        dtpPrescribedOn.Format = DateTimePickerFormat.Short;
        dtpPrescribedOn.Location = new Point(150, 20 + 3 * 34);
        dtpPrescribedOn.Name = "dtpPrescribedOn";
        dtpPrescribedOn.Size = new Size(120, 23);
        dtpPrescribedOn.MaxDate = DateTime.Today;
        dtpPrescribedOn.Value = DateTime.Today;
        dtpPrescribedOn.TabIndex = 3;

        Label(20, 20 + 4 * 34 + 4, "&Repeats");
        numRepeats.Location = new Point(150, 20 + 4 * 34);
        numRepeats.Maximum = 11;
        numRepeats.Name = "numRepeats";
        numRepeats.Size = new Size(60, 23);
        numRepeats.TabIndex = 4;

        // The default validity is six months from the prescription date
        // (specification Section 9.2), so the field stays off unless the
        // prescriber wrote something else.
        chkHasValidUntil.AutoSize = true;
        chkHasValidUntil.Location = new Point(240, 20 + 4 * 34 + 2);
        chkHasValidUntil.Name = "chkHasValidUntil";
        chkHasValidUntil.Text = "Prescriber set a &validity date";
        chkHasValidUntil.UseVisualStyleBackColor = true;
        chkHasValidUntil.TabIndex = 5;

        dtpValidUntil.Enabled = false;
        dtpValidUntil.Format = DateTimePickerFormat.Short;
        dtpValidUntil.Location = new Point(450, 20 + 4 * 34);
        dtpValidUntil.Name = "dtpValidUntil";
        dtpValidUntil.Size = new Size(120, 23);
        dtpValidUntil.Value = DateTime.Today.AddMonths(6);
        dtpValidUntil.TabIndex = 6;

        // Typed into like the dispensary's scan bar: a code, or a few
        // words of the name, with the matches and their details offered
        // underneath. Choosing one puts it on the script.
        Label(20, 20 + 5 * 34 + 4, "&Find product");
        txtFindProduct.Location = new Point(150, 20 + 5 * 34);
        txtFindProduct.PlaceholderText =
            "Type a code, or a few words of the name - \"losartan 50\"";
        txtFindProduct.Size = new Size(820, 23);
        txtFindProduct.TabIndex = 7;

        Label(20, 20 + 6 * 34 + 6, "&Items");

        // Tab goes along a line through the cells that take typing - code,
        // quantity, pack, price, directions, generic - and on to the next.
        grdItems.TabsThroughCells = true;
        grdItems.AllowUserToAddRows = false;
        grdItems.AllowUserToResizeRows = false;
        grdItems.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grdItems.Location = new Point(150, 20 + 6 * 34);
        grdItems.Name = "grdItems";
        grdItems.RowHeadersVisible = false;
        grdItems.RowTemplate.Height = 24;
        grdItems.Size = new Size(820, 150);
        grdItems.TabIndex = 8;

        grdItems.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ProductCode", HeaderText = "Code",
            ToolTipText = "A product code, or a few words of its name",
            FillWeight = 72, SortMode = DataGridViewColumnSortMode.NotSortable
        });

        // What the code is, and its details, so the item can be checked
        // against the script without knowing the catalogue by heart.
        var described = new DataGridViewCellStyle
        {
            BackColor = SystemColors.Control,
            ForeColor = SystemColors.ControlText
        };

        grdItems.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Product", HeaderText = "Product", FillWeight = 118,
            ReadOnly = true, DefaultCellStyle = described,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        grdItems.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Details", HeaderText = "Pack size, stock",
            FillWeight = 92, ReadOnly = true, DefaultCellStyle = described,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        var number = new DataGridViewCellStyle
        {
            Format = "0.###",
            Alignment = DataGridViewContentAlignment.MiddleRight
        };

        grdItems.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Quantity", HeaderText = "Qty (tabs)", FillWeight = 48,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            DefaultCellStyle = number,
            ToolTipText = "The number of tablets, capsules or ml handed over - not packs. "
                          + "The charge is worked out from the pack price and rounded up."
        });

        // The pack this line is priced by and that pack's price: the
        // product's own until changed here, for this script only. The
        // catalogue is not touched. Hidden (25 October 2026): the quantity
        // is counted in tablets and priced from the product's own pack, so
        // the dispenser enters the tablets alone. Kept, so a script that
        // set its own pack keeps it when it is corrected.
        grdItems.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "PackUnits", HeaderText = "Pack", FillWeight = 45, Visible = false,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            DefaultCellStyle = number,
            ToolTipText = "How many the price is for - the product's own pack "
                          + "unless changed for this script"
        });

        grdItems.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "PackPrice", HeaderText = "Price", FillWeight = 52,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Format = "N2",
                Alignment = DataGridViewContentAlignment.MiddleRight
            },
            ToolTipText = "The price of that pack on this script - the shelf "
                          + "price unless changed"
        });

        // What the line comes to: price x quantity / pack. Hidden with the
        // pack: the dispensing works the charge out, rounded up.
        grdItems.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Charge", HeaderText = "Charge", FillWeight = 52,
            ReadOnly = true, Visible = false,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Format = "N2",
                Alignment = DataGridViewContentAlignment.MiddleRight,
                BackColor = SystemColors.Control,
                ForeColor = SystemColors.ControlText
            }
        });

        grdItems.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Directions", HeaderText = "Directions (on label)",
            FillWeight = 118,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                WrapMode = DataGridViewTriState.True
            }
        });
        grdItems.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;

        grdItems.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "AllowSubstitution", HeaderText = "Generic",
            ToolTipText = "A generic may be given instead",
            FillWeight = 60,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        btnAddItem.Location = new Point(150, 20 + 6 * 34 + 158);
        btnAddItem.Size = new Size(90, 26);
        btnAddItem.Text = "A&dd";
        btnAddItem.UseVisualStyleBackColor = true;
        btnAddItem.TabIndex = 9;

        btnRemoveItem.Location = new Point(246, 20 + 6 * 34 + 158);
        btnRemoveItem.Size = new Size(90, 26);
        btnRemoveItem.Text = "Re&move";
        btnRemoveItem.UseVisualStyleBackColor = true;
        btnRemoveItem.TabIndex = 10;

        // What the patient has had before - imported history included -
        // picked onto this script and changed here like any other line.
        btnFromHistory.Location = new Point(342, 20 + 6 * 34 + 158);
        btnFromHistory.Size = new Size(150, 26);
        btnFromHistory.Text = "From &history...";
        btnFromHistory.UseVisualStyleBackColor = true;
        btnFromHistory.TabIndex = 11;

        btnSave.Location = new Point(804, 20 + 6 * 34 + 196);
        btnSave.Size = new Size(80, 30);
        btnSave.Text = "&Save";
        btnSave.UseVisualStyleBackColor = true;
        btnSave.TabIndex = 12;

        btnCancel.Location = new Point(890, 20 + 6 * 34 + 196);
        btnCancel.Size = new Size(80, 30);
        btnCancel.Text = "Cancel";
        btnCancel.UseVisualStyleBackColor = true;
        btnCancel.TabIndex = 13;

        AcceptButton = btnSave;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = btnCancel;
        ClientSize = new Size(1000, 20 + 6 * 34 + 244);
        Controls.AddRange(new Control[]
        {
            lblPatient, dtpPrescribedOn, numRepeats, chkHasValidUntil,
            dtpValidUntil, txtFindProduct, grdItems, btnAddItem,
            btnRemoveItem, btnFromHistory, btnSave, btnCancel
        });
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "frmScriptEdit";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Capture script";
        ((System.ComponentModel.ISupportInitialize)numRepeats).EndInit();
        ((System.ComponentModel.ISupportInitialize)grdItems).EndInit();
        ResumeLayout(false);
        PerformLayout();
    }

    private void Field(string caption, Control input, int row, int width)
    {
        var y = 20 + row * 34;
        Label(20, y + 4, caption);

        input.Location = new Point(150, y);
        input.Size = new Size(width, 23);
        input.TabIndex = row;
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
