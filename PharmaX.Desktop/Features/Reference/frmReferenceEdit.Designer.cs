namespace PharmaX.Desktop.Features.Reference;

partial class frmReferenceEdit
{
    private System.ComponentModel.IContainer components = null!;
    private TableLayoutPanel layRoot = null!;
    private Label lblCode = null!;
    private Label lblName = null!;
    private Label lblDetail = null!;
    private TextBox txtCode = null!;
    private TextBox txtName = null!;
    private TextBox txtDetail = null!;
    private FlowLayoutPanel pnlButtons = null!;
    private Button btnSave = null!;
    private Button btnCancel = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>
    /// Laid out by a TableLayoutPanel, like the sign-in window: a small
    /// dialog with absolute coordinates is the one that gets cut off on
    /// a scaled display.
    /// </summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        layRoot = new TableLayoutPanel();
        lblCode = new Label();
        lblName = new Label();
        lblDetail = new Label();
        txtCode = new TextBox();
        txtName = new TextBox();
        txtDetail = new TextBox();
        pnlButtons = new FlowLayoutPanel();
        btnSave = new Button();
        btnCancel = new Button();
        SuspendLayout();

        lblCode.Anchor = AnchorStyles.Left;
        lblCode.AutoSize = true;
        lblCode.Text = "&Code";

        txtCode.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        txtCode.CharacterCasing = CharacterCasing.Upper;
        txtCode.MaxLength = 30;
        txtCode.Name = "txtCode";
        txtCode.Width = 300;

        lblName.Anchor = AnchorStyles.Left;
        lblName.AutoSize = true;
        lblName.Text = "&Name";

        txtName.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        txtName.MaxLength = 200;
        txtName.Name = "txtName";
        txtName.Width = 300;

        lblDetail.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        lblDetail.AutoSize = true;
        lblDetail.Margin = new Padding(3, 8, 3, 0);
        lblDetail.Name = "lblDetail";
        lblDetail.Text = "Detail";

        txtDetail.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        txtDetail.MaxLength = 400;
        txtDetail.Multiline = true;
        txtDetail.Name = "txtDetail";
        txtDetail.Height = 70;
        txtDetail.Width = 300;

        btnSave.AutoSize = true;
        btnSave.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnSave.MinimumSize = new Size(90, 30);
        btnSave.Name = "btnSave";
        btnSave.Text = "&Save";
        btnSave.UseVisualStyleBackColor = true;

        btnCancel.AutoSize = true;
        btnCancel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnCancel.Margin = new Padding(8, 3, 0, 3);
        btnCancel.MinimumSize = new Size(90, 30);
        btnCancel.Name = "btnCancel";
        btnCancel.Text = "Cancel";
        btnCancel.UseVisualStyleBackColor = true;

        pnlButtons.Anchor = AnchorStyles.Right;
        pnlButtons.AutoSize = true;
        pnlButtons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        pnlButtons.FlowDirection = FlowDirection.LeftToRight;
        pnlButtons.Margin = new Padding(0, 14, 0, 0);
        pnlButtons.WrapContents = false;
        pnlButtons.Controls.Add(btnSave);
        pnlButtons.Controls.Add(btnCancel);

        layRoot.AutoSize = true;
        layRoot.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        layRoot.ColumnCount = 2;
        layRoot.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layRoot.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layRoot.Name = "layRoot";
        layRoot.Padding = new Padding(20, 18, 20, 18);
        layRoot.RowCount = 4;
        for (var row = 0; row < 4; row++)
            layRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layRoot.Controls.Add(lblCode, 0, 0);
        layRoot.Controls.Add(txtCode, 1, 0);
        layRoot.Controls.Add(lblName, 0, 1);
        layRoot.Controls.Add(txtName, 1, 1);
        layRoot.Controls.Add(lblDetail, 0, 2);
        layRoot.Controls.Add(txtDetail, 1, 2);
        layRoot.Controls.Add(pnlButtons, 0, 3);
        layRoot.SetColumnSpan(pnlButtons, 2);

        AcceptButton = btnSave;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = btnCancel;
        ClientSize = new Size(420, 260);
        Controls.Add(layRoot);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "frmReferenceEdit";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX";
        ResumeLayout(false);
        PerformLayout();
    }
}
