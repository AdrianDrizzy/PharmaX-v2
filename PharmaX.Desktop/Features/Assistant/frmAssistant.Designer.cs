namespace PharmaX.Desktop.Features.Assistant;

partial class frmAssistant
{
    private System.ComponentModel.IContainer components = null!;
    private Label lblDisclaimer = null!;
    private RichTextBox rtbConversation = null!;
    private TableLayoutPanel pnlAsk = null!;
    private FlowLayoutPanel pnlOpens = null!;
    private FlowLayoutPanel pnlAttached = null!;
    private TextBox txtQuestion = null!;
    private Button btnAttach = null!;
    private Button btnAsk = null!;
    private Button btnSave = null!;
    private Button btnClose = null!;
    private Label lblHint = null!;
    private Label lblStatus = null!;
    private ContextMenuStrip mnuSave = null!;
    private Panel pnlChats = null!;
    private Button btnNewChat = null!;
    private Label lblChats = null!;
    private ListBox lstChats = null!;
    private Button btnDeleteChat = null!;
    private ContextMenuStrip mnuChat = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _bold?.Dispose();
            _note?.Dispose();
            _heading?.Dispose();
            _gap?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        lblDisclaimer = new Label();
        rtbConversation = new RichTextBox();
        pnlAsk = new TableLayoutPanel();
        pnlOpens = new FlowLayoutPanel();
        pnlAttached = new FlowLayoutPanel();
        txtQuestion = new TextBox();
        btnAttach = new Button();
        btnAttach.Name = "btnAttach";
        btnAsk = new Button();
        btnAsk.Name = "btnAsk";
        btnSave = new Button();
        btnSave.Name = "btnSave";
        btnClose = new Button();
        btnClose.Name = "btnClose";
        lblHint = new Label();
        lblStatus = new Label();
        mnuSave = new ContextMenuStrip(components);
        pnlChats = new Panel();
        btnNewChat = new Button();
        btnNewChat.Name = "btnNewChat";
        lblChats = new Label();
        lstChats = new ListBox();
        btnDeleteChat = new Button();
        btnDeleteChat.Name = "btnDeleteChat";
        mnuChat = new ContextMenuStrip(components);
        SuspendLayout();

        // Deletes the chat on show; right-click or Delete on any chat in the list does the same for it.
        btnDeleteChat.Dock = DockStyle.Bottom;
        btnDeleteChat.Enabled = false;
        btnDeleteChat.Height = 34;
        btnDeleteChat.TabIndex = 7;
        btnDeleteChat.Text = "Delete chat";
        btnDeleteChat.UseVisualStyleBackColor = true;

        // Today's chats down the left, newest first, with a new one at the top.
        btnNewChat.Dock = DockStyle.Top;
        btnNewChat.Height = 36;
        btnNewChat.TabIndex = 5;
        btnNewChat.Text = "New chat";
        btnNewChat.UseVisualStyleBackColor = true;

        lblChats.Dock = DockStyle.Top;
        lblChats.ForeColor = SystemColors.GrayText;
        lblChats.Height = 44;
        lblChats.Name = "lblChats";
        lblChats.Padding = new Padding(2, 10, 2, 0);
        lblChats.Text = "Today (kept until midnight)";

        lstChats.BorderStyle = BorderStyle.None;
        lstChats.Dock = DockStyle.Fill;
        lstChats.DrawMode = DrawMode.OwnerDrawFixed;
        lstChats.IntegralHeight = false;
        lstChats.ItemHeight = 44;
        lstChats.Name = "lstChats";
        lstChats.TabIndex = 6;

        pnlChats.Controls.Add(lstChats);
        pnlChats.Controls.Add(lblChats);
        pnlChats.Controls.Add(btnNewChat);
        pnlChats.Controls.Add(btnDeleteChat);
        pnlChats.Dock = DockStyle.Left;
        pnlChats.Name = "pnlChats";
        pnlChats.Padding = new Padding(10, 10, 6, 10);
        pnlChats.Width = 230;

        // The disclaimer, always in view above the conversation.
        lblDisclaimer.Dock = DockStyle.Top;
        lblDisclaimer.Height = 44;
        lblDisclaimer.Name = "lblDisclaimer";
        lblDisclaimer.Padding = new Padding(16, 6, 16, 6);
        lblDisclaimer.ForeColor = SystemColors.GrayText;

        rtbConversation.BorderStyle = BorderStyle.None;
        rtbConversation.Dock = DockStyle.Fill;
        rtbConversation.Name = "rtbConversation";
        rtbConversation.ReadOnly = true;
        rtbConversation.TabStop = false;
        rtbConversation.BackColor = SystemColors.Window;

        // A row of buttons and the question, then what is attached, what
        // can be done here, and what the assistant is doing.
        pnlAsk.AutoSize = true;
        pnlAsk.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        pnlAsk.ColumnCount = 5;
        pnlAsk.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pnlAsk.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        pnlAsk.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pnlAsk.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pnlAsk.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pnlAsk.Dock = DockStyle.Bottom;
        pnlAsk.Name = "pnlAsk";
        pnlAsk.Padding = new Padding(12, 8, 12, 6);
        pnlAsk.RowCount = 4;
        for (var i = 0; i < 4; i++) pnlAsk.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        btnAttach.AutoSize = true;
        btnAttach.MinimumSize = new Size(110, 32);
        btnAttach.TabIndex = 0;
        btnAttach.Text = "Attach...";
        btnAttach.UseVisualStyleBackColor = true;

        txtQuestion.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        txtQuestion.Margin = new Padding(6, 3, 6, 3);
        txtQuestion.Name = "txtQuestion";
        txtQuestion.PlaceholderText = "Ask anything, or attach a file and ask about it";
        txtQuestion.TabIndex = 1;

        // The key written on it: Attach, added before it, would otherwise take F2.
        btnAsk.AutoSize = true;
        btnAsk.MinimumSize = new Size(96, 32);
        btnAsk.TabIndex = 2;
        btnAsk.Text = "F2 Ask";
        btnAsk.UseVisualStyleBackColor = true;

        btnSave.AutoSize = true;
        btnSave.Enabled = false;
        btnSave.MinimumSize = new Size(130, 32);
        btnSave.TabIndex = 3;
        btnSave.Text = "Save answer...";
        btnSave.UseVisualStyleBackColor = true;

        btnClose.AutoSize = true;
        btnClose.MinimumSize = new Size(100, 32);
        btnClose.TabIndex = 4;
        btnClose.Text = "&Close";
        btnClose.UseVisualStyleBackColor = true;

        mnuSave.Items.Add("As a PDF", null, (_, _) => SaveRequested?.Invoke(Application.Assistant.AnswerFormat.Pdf));
        mnuSave.Items.Add("As an Excel workbook", null, (_, _) => SaveRequested?.Invoke(Application.Assistant.AnswerFormat.Excel));
        mnuSave.Items.Add("As a Word document", null, (_, _) => SaveRequested?.Invoke(Application.Assistant.AnswerFormat.Word));

        // One button per file attached. Hidden until there is one.
        pnlAttached.AutoSize = true;
        pnlAttached.Dock = DockStyle.Fill;
        pnlAttached.Name = "pnlAttached";
        pnlAttached.Visible = false;
        pnlAttached.WrapContents = true;

        lblHint.AutoSize = true;
        lblHint.Dock = DockStyle.Fill;
        lblHint.ForeColor = SystemColors.GrayText;
        lblHint.Margin = new Padding(3, 6, 3, 0);
        lblHint.Name = "lblHint";
        lblHint.Text = "Attach pictures, PDFs, Excel, Word or text files (not videos) to ask about them, or drop them here. "
                       + "Save any answer as a PDF, Excel or Word file with Save answer, or ask \"save that as a PDF\". "
                       + "Conversations are kept for today only - save what you want to keep.";

        lblStatus.AutoEllipsis = true;
        lblStatus.Dock = DockStyle.Fill;
        lblStatus.Height = 22;
        lblStatus.Name = "lblStatus";

        pnlAsk.Controls.Add(btnAttach, 0, 0);
        pnlAsk.Controls.Add(txtQuestion, 1, 0);
        pnlAsk.Controls.Add(btnAsk, 2, 0);
        pnlAsk.Controls.Add(btnSave, 3, 0);
        pnlAsk.Controls.Add(btnClose, 4, 0);
        pnlAsk.Controls.Add(pnlAttached, 0, 1);
        pnlAsk.SetColumnSpan(pnlAttached, 5);
        pnlAsk.Controls.Add(lblHint, 0, 2);
        pnlAsk.SetColumnSpan(lblHint, 5);
        pnlAsk.Controls.Add(lblStatus, 0, 3);
        pnlAsk.SetColumnSpan(lblStatus, 5);

        // One button per screen the answer names. Hidden until there is one.
        pnlOpens.AutoScroll = true;
        pnlOpens.Dock = DockStyle.Bottom;
        pnlOpens.Height = 0;
        pnlOpens.Name = "pnlOpens";
        pnlOpens.Padding = new Padding(12, 4, 12, 0);
        pnlOpens.Visible = false;
        pnlOpens.WrapContents = true;

        AllowDrop = true;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1080, 660);
        MinimumSize = new Size(820, 480);
        Controls.Add(rtbConversation);
        Controls.Add(lblDisclaimer);
        Controls.Add(pnlOpens);
        Controls.Add(pnlAsk);
        // Last, so it docks first and runs the window's full height.
        Controls.Add(pnlChats);
        KeyPreview = true;
        Name = "frmAssistant";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Ask PharmaX";
        ResumeLayout(false);
        PerformLayout();
    }
}
