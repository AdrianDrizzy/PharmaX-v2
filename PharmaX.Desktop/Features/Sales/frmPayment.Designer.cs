using PharmaX.Desktop.Controls;

namespace PharmaX.Desktop.Features.Sales;

partial class frmPayment
{
    private System.ComponentModel.IContainer components = null!;
    private Label lblTender = null!;
    private Label lblAmount = null!;
    private Label lblReference = null!;
    private Label lblDue = null!;
    private Label lblDueValue = null!;
    private Label lblOutstanding = null!;
    private Label lblOutstandingValue = null!;
    private Label lblError = null!;
    private ComboBox cboTender = null!;
    private MoneyTextBox txtAmount = null!;
    private TextBox txtReference = null!;
    private PhxGrid grdPayments = null!;
    private Button btnAdd = null!;
    private Button btnRemove = null!;
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
        lblTender = new Label();
        lblAmount = new Label();
        lblReference = new Label();
        lblDue = new Label();
        lblDueValue = new Label();
        lblOutstanding = new Label();
        lblOutstandingValue = new Label();
        lblError = new Label();
        cboTender = new ComboBox();
        txtAmount = new MoneyTextBox();
        txtReference = new TextBox();
        grdPayments = new PhxGrid();
        btnAdd = new Button();
        btnRemove = new Button();
        btnOk = new Button();
        btnCancel = new Button();
        SuspendLayout();

        lblDue.AutoSize = true;
        lblDue.Font = new Font("Segoe UI", 11F);
        lblDue.Location = new Point(16, 14);
        lblDue.Text = "Amount due";

        lblDueValue.AutoSize = false;
        lblDueValue.Font = new Font("Consolas", 16F, FontStyle.Bold);
        lblDueValue.Location = new Point(300, 8);
        lblDueValue.Size = new Size(180, 30);
        lblDueValue.TextAlign = ContentAlignment.MiddleRight;

        lblTender.AutoSize = true;
        lblTender.Location = new Point(16, 56);
        lblTender.Text = "&Tender";

        cboTender.DropDownStyle = ComboBoxStyle.DropDownList;
        cboTender.Location = new Point(100, 52);
        cboTender.Name = "cboTender";
        cboTender.Size = new Size(160, 23);
        cboTender.TabIndex = 0;

        lblAmount.AutoSize = true;
        lblAmount.Location = new Point(16, 90);
        lblAmount.Text = "&Amount";

        txtAmount.Location = new Point(100, 86);
        txtAmount.Name = "txtAmount";
        txtAmount.Size = new Size(160, 26);
        txtAmount.TabIndex = 1;

        lblReference.AutoSize = true;
        lblReference.Location = new Point(16, 124);
        lblReference.Text = "&Reference";

        txtReference.Location = new Point(100, 120);
        txtReference.MaxLength = 100;
        txtReference.Name = "txtReference";
        txtReference.Size = new Size(240, 23);
        txtReference.TabIndex = 2;

        btnAdd.Location = new Point(356, 84);
        btnAdd.Name = "btnAdd";
        btnAdd.Size = new Size(124, 30);
        btnAdd.TabIndex = 3;
        btnAdd.Text = "A&dd tender";
        btnAdd.UseVisualStyleBackColor = true;

        grdPayments.Location = new Point(16, 158);
        grdPayments.Name = "grdPayments";
        grdPayments.Size = new Size(464, 140);
        grdPayments.TabIndex = 4;
        grdPayments.AddColumn("Tender", "Tender", 100);
        grdPayments.AddColumn("Amount", "Amount", 80, "N2", rightAlign: true);
        grdPayments.AddColumn("Reference", "Reference", 140);

        btnRemove.Location = new Point(16, 306);
        btnRemove.Name = "btnRemove";
        btnRemove.Size = new Size(124, 28);
        btnRemove.TabIndex = 5;
        btnRemove.Text = "&Remove";
        btnRemove.UseVisualStyleBackColor = true;

        lblOutstanding.AutoSize = true;
        lblOutstanding.Font = new Font("Segoe UI", 11F);
        lblOutstanding.Location = new Point(180, 312);
        lblOutstanding.Text = "Outstanding";

        lblOutstandingValue.AutoSize = false;
        lblOutstandingValue.Font = new Font("Consolas", 12F, FontStyle.Bold);
        lblOutstandingValue.Location = new Point(300, 308);
        lblOutstandingValue.Size = new Size(180, 24);
        lblOutstandingValue.TextAlign = ContentAlignment.MiddleRight;

        lblError.AutoSize = false;
        lblError.ForeColor = Color.FromArgb(168, 32, 32);
        lblError.Location = new Point(16, 342);
        lblError.Size = new Size(464, 34);

        btnOk.Location = new Point(324, 382);
        btnOk.Name = "btnOk";
        btnOk.Size = new Size(75, 30);
        btnOk.TabIndex = 6;
        btnOk.Text = "&Pay";
        btnOk.UseVisualStyleBackColor = true;

        btnCancel.DialogResult = DialogResult.Cancel;
        btnCancel.Location = new Point(405, 382);
        btnCancel.Name = "btnCancel";
        btnCancel.Size = new Size(75, 30);
        btnCancel.TabIndex = 7;
        btnCancel.Text = "Cancel";
        btnCancel.UseVisualStyleBackColor = true;

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(496, 426);
        Controls.AddRange(new Control[]
        {
            lblDue, lblDueValue, lblTender, cboTender, lblAmount, txtAmount,
            lblReference, txtReference, btnAdd, grdPayments, btnRemove,
            lblOutstanding, lblOutstandingValue, lblError, btnOk, btnCancel
        });
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "frmPayment";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Payment";
        ResumeLayout(false);
        PerformLayout();
    }
}
