namespace PharmaX.Desktop.Features.Assistant;

partial class frmAssistant
{
    private System.ComponentModel.IContainer components = null!;
    private Label lblDisclaimer = null!;
    private RichTextBox rtbConversation = null!;
    private Panel pnlAsk = null!;
    private TextBox txtQuestion = null!;
    private Button btnAsk = null!;
    private Button btnClose = null!;
    private Label lblStatus = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _bold?.Dispose();
            _note?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        lblDisclaimer = new Label();
        rtbConversation = new RichTextBox();
        pnlAsk = new Panel();
        txtQuestion = new TextBox();
        btnAsk = new Button();
        btnAsk.Name = "btnAsk";
        btnClose = new Button();
        btnClose.Name = "btnClose";
        lblStatus = new Label();
        SuspendLayout();

        // The disclaimer, always in view above the conversation.
        lblDisclaimer.Dock = DockStyle.Top;
        lblDisclaimer.Height = 44;
        lblDisclaimer.Name = "lblDisclaimer";
        lblDisclaimer.Padding = new Padding(10, 6, 10, 6);
        lblDisclaimer.ForeColor = SystemColors.GrayText;

        rtbConversation.BorderStyle = BorderStyle.None;
        rtbConversation.Dock = DockStyle.Fill;
        rtbConversation.Name = "rtbConversation";
        rtbConversation.ReadOnly = true;
        rtbConversation.TabStop = false;
        rtbConversation.BackColor = SystemColors.Window;

        // Sized to the window before its children are anchored, so the
        // right-hand anchors measure from the real edge.
        pnlAsk.Size = new Size(760, 84);

        txtQuestion.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        txtQuestion.Location = new Point(12, 12);
        txtQuestion.Name = "txtQuestion";
        txtQuestion.PlaceholderText = "Ask anything, e.g. what are the side effects of amlodipine?";
        txtQuestion.Size = new Size(520, 23);
        txtQuestion.TabIndex = 0;

        btnAsk.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnAsk.Location = new Point(544, 8);
        btnAsk.Size = new Size(96, 30);
        btnAsk.TabIndex = 1;
        btnAsk.Text = "&Ask";
        btnAsk.UseVisualStyleBackColor = true;

        btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnClose.Location = new Point(648, 8);
        btnClose.Size = new Size(100, 30);
        btnClose.TabIndex = 2;
        btnClose.Text = "&Close";
        btnClose.UseVisualStyleBackColor = true;

        lblStatus.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        lblStatus.AutoEllipsis = true;
        lblStatus.Location = new Point(12, 48);
        lblStatus.Name = "lblStatus";
        lblStatus.Size = new Size(736, 22);

        pnlAsk.Controls.AddRange(new Control[] { lblStatus, btnClose, btnAsk, txtQuestion });
        pnlAsk.Dock = DockStyle.Bottom;
        pnlAsk.Name = "pnlAsk";

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(760, 560);
        Controls.Add(rtbConversation);
        Controls.Add(lblDisclaimer);
        Controls.Add(pnlAsk);
        KeyPreview = true;
        Name = "frmAssistant";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Ask PharmaX";
        ResumeLayout(false);
    }
}
