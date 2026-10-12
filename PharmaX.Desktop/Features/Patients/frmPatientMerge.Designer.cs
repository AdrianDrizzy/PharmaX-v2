namespace PharmaX.Desktop.Features.Patients;

partial class frmPatientMerge
{
    private System.ComponentModel.IContainer components = null!;
    private TableLayoutPanel layRoot = null!;
    private Label lblIntro = null!;
    private Label lblKeep = null!;
    private Label lblMerge = null!;
    private Panel pnlKeep = null!;
    private Panel pnlMerge = null!;
    private TextBox txtKeep = null!;
    private TextBox txtMerge = null!;
    private Button btnChooseKeep = null!;
    private Button btnChooseMerge = null!;
    private Label lblEffect = null!;
    private Label lblReason = null!;
    private TextBox txtReason = null!;
    private FlowLayoutPanel pnlButtons = null!;
    private Button btnMerge = null!;
    private Button btnCancel = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        layRoot = new TableLayoutPanel();
        lblIntro = new Label();
        lblKeep = new Label();
        lblMerge = new Label();
        pnlKeep = new Panel();
        pnlMerge = new Panel();
        txtKeep = new TextBox();
        txtMerge = new TextBox();
        btnChooseKeep = new Button();
        btnChooseMerge = new Button();
        lblEffect = new Label();
        lblReason = new Label();
        txtReason = new TextBox();
        pnlButtons = new FlowLayoutPanel();
        btnMerge = new Button();
        btnCancel = new Button();
        SuspendLayout();

        lblIntro.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        lblIntro.AutoSize = true;
        lblIntro.Margin = new Padding(3, 0, 3, 12);
        lblIntro.MaximumSize = new Size(520, 0);
        lblIntro.Text =
            "Use this where the same person has two files. The history on "
            + "the duplicate moves onto the file you keep, and the "
            + "duplicate is retired rather than deleted so its old file "
            + "number still resolves.";

        lblKeep.Anchor = AnchorStyles.Left;
        lblKeep.AutoSize = true;
        lblKeep.Text = "File to &keep";

        txtKeep.Dock = DockStyle.Fill;
        txtKeep.Name = "txtKeep";
        txtKeep.PlaceholderText = "Choose the file that survives";
        txtKeep.ReadOnly = true;

        btnChooseKeep.Dock = DockStyle.Right;
        btnChooseKeep.Name = "btnChooseKeep";
        btnChooseKeep.Text = "F&ind...";
        btnChooseKeep.UseVisualStyleBackColor = true;
        btnChooseKeep.Width = 90;

        pnlKeep.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        pnlKeep.Controls.Add(txtKeep);
        pnlKeep.Controls.Add(btnChooseKeep);
        pnlKeep.Height = 26;
        pnlKeep.Name = "pnlKeep";
        pnlKeep.Width = 380;

        lblMerge.Anchor = AnchorStyles.Left;
        lblMerge.AutoSize = true;
        lblMerge.Text = "&Duplicate to merge";

        txtMerge.Dock = DockStyle.Fill;
        txtMerge.Name = "txtMerge";
        txtMerge.PlaceholderText = "Choose the file to retire";
        txtMerge.ReadOnly = true;

        btnChooseMerge.Dock = DockStyle.Right;
        btnChooseMerge.Name = "btnChooseMerge";
        btnChooseMerge.Text = "Fi&nd...";
        btnChooseMerge.UseVisualStyleBackColor = true;
        btnChooseMerge.Width = 90;

        pnlMerge.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        pnlMerge.Controls.Add(txtMerge);
        pnlMerge.Controls.Add(btnChooseMerge);
        pnlMerge.Height = 26;
        pnlMerge.Name = "pnlMerge";
        pnlMerge.Width = 380;

        // What is about to happen, in words, before it happens.
        lblEffect.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        lblEffect.AutoSize = true;
        lblEffect.Margin = new Padding(3, 12, 3, 12);
        lblEffect.MaximumSize = new Size(520, 0);
        lblEffect.Name = "lblEffect";
        lblEffect.Text = "Choose both files to see what will move.";

        lblReason.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        lblReason.AutoSize = true;
        lblReason.Margin = new Padding(3, 8, 3, 0);
        lblReason.Text = "&Reason";

        txtReason.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        txtReason.MaxLength = 300;
        txtReason.Multiline = true;
        txtReason.Name = "txtReason";
        txtReason.PlaceholderText = "e.g. Same patient opened twice at the counter";
        txtReason.Height = 60;
        txtReason.Width = 380;

        btnMerge.AutoSize = true;
        btnMerge.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnMerge.MinimumSize = new Size(110, 30);
        btnMerge.Name = "btnMerge";
        btnMerge.Text = "&Merge";
        btnMerge.UseVisualStyleBackColor = true;

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
        pnlButtons.Controls.Add(btnMerge);
        pnlButtons.Controls.Add(btnCancel);

        layRoot.AutoSize = true;
        layRoot.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        layRoot.ColumnCount = 2;
        layRoot.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layRoot.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layRoot.Name = "layRoot";
        layRoot.Padding = new Padding(20, 18, 20, 18);
        layRoot.RowCount = 6;
        for (var row = 0; row < 6; row++)
            layRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layRoot.Controls.Add(lblIntro, 0, 0);
        layRoot.SetColumnSpan(lblIntro, 2);
        layRoot.Controls.Add(lblKeep, 0, 1);
        layRoot.Controls.Add(pnlKeep, 1, 1);
        layRoot.Controls.Add(lblMerge, 0, 2);
        layRoot.Controls.Add(pnlMerge, 1, 2);
        layRoot.Controls.Add(lblEffect, 0, 3);
        layRoot.SetColumnSpan(lblEffect, 2);
        layRoot.Controls.Add(lblReason, 0, 4);
        layRoot.Controls.Add(txtReason, 1, 4);
        layRoot.Controls.Add(pnlButtons, 0, 5);
        layRoot.SetColumnSpan(pnlButtons, 2);

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = btnCancel;
        ClientSize = new Size(600, 460);
        Controls.Add(layRoot);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "frmPatientMerge";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Merge patient files";
        ResumeLayout(false);
        PerformLayout();
    }
}
