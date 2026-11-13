using System.Runtime.InteropServices;
using PharmaX.Application.Assistant;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Assistant;

/// <summary>
/// Ask PharmaX: a conversation with the offline assistant, open over
/// whatever screen the operator is on.
/// </summary>
public partial class frmAssistant : PhxFormBase, IAssistantView
{
    private readonly AssistantPresenter? _presenter;
    private Font? _bold;
    private Font? _note;
    private Font? _heading;
    private Font? _gap;

    /// Not from inside itself: Ctrl+Shift+A here would open a second one.
    protected override bool OffersAssistant => false;

    /// Designer only. Never called at runtime.
    public frmAssistant()
    {
        InitializeComponent();
        lblDisclaimer.Text = ChatDisclaimer.Text;

        btnAsk.Click += (_, _) => AskRequested?.Invoke(this, EventArgs.Empty);
        btnClose.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        btnAttach.Click += (_, _) => ChooseFiles();
        btnSave.Click += (_, _) => mnuSave.Show(btnSave, Point.Empty, ToolStripDropDownDirection.AboveRight);
        btnNewChat.Click += (_, _) => SwitchTo(null);
        lstChats.SelectedIndexChanged += (_, _) =>
        {
            if (!_listing && lstChats.SelectedItem is AssistantHistory.Listed chat && chat.Id != _chatId) SwitchTo(chat);
        };
        lstChats.DrawItem += DrawChat;
        btnDeleteChat.Click += (_, _) => DeleteChat(lstChats.SelectedItem as AssistantHistory.Listed);
        AssistantHistory.Listed? pointedAt = null;
        mnuChat.Items.Add("Delete chat", null, (_, _) => DeleteChat(pointedAt));
        lstChats.MouseUp += (_, e) =>
        {
            // The chat under the pointer, without opening it.
            if (e.Button != MouseButtons.Right) return;
            var at = lstChats.IndexFromPoint(e.Location);
            if (at < 0) return;
            pointedAt = (AssistantHistory.Listed)lstChats.Items[at];
            mnuChat.Show(lstChats, e.Location);
        };
        lstChats.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Delete) return;
            e.Handled = true;
            DeleteChat(lstChats.SelectedItem as AssistantHistory.Listed);
        };
        rtbConversation.MouseUp += (_, _) => OpenPictureAtSelection();
        rtbConversation.HandleCreated += (_, _) => SetMargins();
        rtbConversation.Resize += (_, _) => { if (IsHandleCreated) BeginInvoke(SetMargins); };
        txtQuestion.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;

            // No ding: Enter in the question box asks it.
            e.SuppressKeyPress = true;
            AskRequested?.Invoke(this, EventArgs.Empty);
        };

        // Files dropped anywhere on the window are attached.
        foreach (var target in new Control[] { this, rtbConversation, txtQuestion })
        {
            target.AllowDrop = true;
            target.DragEnter += (_, e) =>
                e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
            target.DragDrop += (_, e) =>
            {
                if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) Attach(files);
            };
        }
    }

    /// Runtime. Resolved by the container.
    public frmAssistant(AssistantPresenter presenter,
                        Application.Security.IAuthenticationService auth,
                        Application.Abstractions.ICurrentUser user) : this()
    {
        _auth = auth;
        _user = user.Id;
        _presenter = presenter;
        _presenter.Attach(this);
    }

    private readonly Application.Security.IAuthenticationService? _auth;

    /// Whose conversations these are, and the day they are kept for.
    private readonly Guid? _user;
    private readonly DateOnly _day = DateOnly.FromDateTime(DateTime.Now);

    /// The conversation on show: kept once it has its first answer.
    private Guid _chatId = Guid.NewGuid();
    private string _chatTitle = "";
    private DateTime _chatStarted = DateTime.Now;

    /// Today's conversations, newest first, as the side lists them.
    private readonly List<AssistantHistory.Listed> _chats = new();

    /// True while the list is filled from code, not clicked.
    private bool _listing;

    /// Questions asked in this chat, and whether its answer has named it.
    private int _questions;
    private bool _named;

    /// <remarks>
    /// The first answer, or the second where the first only asked which
    /// period: a later question does not rename the chat.
    /// </remarks>
    public void NameChat(string title)
    {
        if (_named || _questions > 2 || string.IsNullOrWhiteSpace(title)) return;

        _named = true;
        _chatTitle = title.Trim();

        var at = _chats.FindIndex(c => c.Id == _chatId);
        if (at < 0) return;

        _chats[at] = _chats[at] with { Title = _chatTitle };
        ShowChats();
    }

    /// Deletes a chat, after asking; the one on show gives way to a new one.
    private void DeleteChat(AssistantHistory.Listed? chat)
    {
        if (_user is not { } user || chat is null) return;
        if (!Confirm($"Delete the chat \"{chat.Title}\"? It cannot be brought back.")) return;

        if (chat.Id == _chatId && _presenter?.StartOver() == false)
        {
            ShowStatus("Finish or cancel the job under way first - answer its question, or type cancel.");
            return;
        }

        AssistantHistory.Delete(user, _day, chat.Id);
        _chats.RemoveAll(c => c.Id == chat.Id);

        if (chat.Id == _chatId) SwitchTo(null);
        else ShowChats();
    }

    public Guid? PromptAuthorisation(string reason, string permissionCode)
    {
        if (_auth is null) return null;

        using var dialog = new Login.frmSupervisorPrompt(_auth, reason, permissionCode);

        return dialog.ShowDialog(this) == DialogResult.OK
            ? dialog.AuthoriserId
            : null;
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // After the theme has set the font, so these match it.
        _bold = new Font(rtbConversation.Font, FontStyle.Bold);
        _note = new Font(rtbConversation.Font.FontFamily, rtbConversation.Font.Size * 0.85f,
                         FontStyle.Italic);
        _heading = new Font(rtbConversation.Font.FontFamily, rtbConversation.Font.Size * 1.25f,
                            FontStyle.Bold);
        _gap = new Font(rtbConversation.Font.FontFamily, 4f);

        if (_user is { } user) _chats.AddRange(AssistantHistory.Today(user, _day));
        ShowChats();
        Welcome();

        txtQuestion.Focus();
        if (_presenter is not null) await _presenter.InitialiseAsync();
    }

    private void Welcome() =>
        Write("Ask how to do anything in PharmaX and the answer shows the screens, with what to click "
              + "outlined in red. Ask about medicines too. Attach pictures, PDFs, Excel, Word or text "
              + "files to ask about them, and save any answer as a PDF, Excel or Word file."
              + Environment.NewLine + Environment.NewLine
              + "Ask for a report (\"sales for March 2026\", \"cash-up report for yesterday\"), a document "
              + "(\"create an invoice\", \"create a quotation\", \"print labels\", \"create a purchase order\") "
              + "or a sum (\"230 + 15%\")."
              + Environment.NewLine + Environment.NewLine
              + "Your chats are listed on the left and kept for today only - they are deleted after "
              + "midnight. To keep an answer, save it as a PDF, Excel or Word file."
              + Environment.NewLine + Environment.NewLine, _note, SystemColors.GrayText, keep: false);

    /// <summary>
    /// Another of today's chats on show, or a new one where
    /// <paramref name="chat"/> is null. Not part way through a guided job:
    /// that would be left half done.
    /// </summary>
    private void SwitchTo(AssistantHistory.Listed? chat)
    {
        var saved = chat is null || _user is not { } user ? null : AssistantHistory.Load(user, _day, chat.Id);

        if (chat is not null && saved is null)
        {
            ShowStatus("That chat could not be opened.");
            ShowChats();
            return;
        }

        if (_presenter?.StartOver() == false)
        {
            ShowStatus("Finish or cancel the job under way first - answer its question, or type cancel.");
            ShowChats();
            return;
        }

        rtbConversation.Clear();
        _pictures.Clear();
        _answer.Clear();
        _answering = false;
        btnSave.Enabled = false;
        ClearAttachments();
        ShowOpens(Array.Empty<(string, string)>());
        ShowStatus("");

        _chatId = chat?.Id ?? Guid.NewGuid();
        _chatTitle = chat?.Title ?? "";
        _chatStarted = chat?.Started ?? DateTime.Now;
        _named = chat is not null;
        _questions = 0;

        if (saved is null) Welcome();
        else
        {
            // A read-only box drops the pictures in what it is given.
            rtbConversation.ReadOnly = false;
            rtbConversation.Rtf = saved.Rtf;
            rtbConversation.ReadOnly = true;
            _pictures.AddRange(saved.Pictures.Select(p => (p.At, p.Png)));
            rtbConversation.SelectionStart = rtbConversation.TextLength;
            rtbConversation.ScrollToCaret();
        }

        ShowChats();
        txtQuestion.Focus();
    }

    /// Today's chats down the side, the one on show selected.
    private void ShowChats()
    {
        _listing = true;
        lstChats.BeginUpdate();
        lstChats.Items.Clear();
        foreach (var chat in _chats) lstChats.Items.Add(chat);
        lstChats.SelectedIndex = _chats.FindIndex(c => c.Id == _chatId);
        lstChats.EndUpdate();
        btnDeleteChat.Enabled = lstChats.SelectedIndex >= 0;
        _listing = false;
    }

    /// A chat's title on one line, when it began under it.
    private void DrawChat(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || lstChats.Items[e.Index] is not AssistantHistory.Listed chat) return;

        var selected = (e.State & DrawItemState.Selected) != 0;
        using (var back = new SolidBrush(selected ? SystemColors.Highlight : lstChats.BackColor))
            e.Graphics.FillRectangle(back, e.Bounds);

        var pad = LogicalToDeviceUnits(8);
        var line = e.Bounds.Height / 2;
        var title = new Rectangle(e.Bounds.X + pad, e.Bounds.Y + LogicalToDeviceUnits(4), e.Bounds.Width - 2 * pad, line);
        var time = title with { Y = title.Bottom - LogicalToDeviceUnits(2) };

        TextRenderer.DrawText(e.Graphics, chat.Title, _bold ?? lstChats.Font, title,
            selected ? SystemColors.HighlightText : SystemColors.WindowText,
            TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(e.Graphics, chat.Started.ToString("HH:mm", System.Globalization.CultureInfo.CurrentCulture),
            lstChats.Font, time, selected ? SystemColors.HighlightText : SystemColors.GrayText,
            TextFormatFlags.SingleLine);
    }

    /// <summary>
    /// Closed with a guided job part way - the X, or Esc: held open while
    /// the job is stopped, then closed by the presenter.
    /// </summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (_presenter?.HasWorkUnderWay == true)
        {
            e.Cancel = true;
            _presenter.RequestClose();
            return;
        }

        base.OnFormClosing(e);
    }

    public string QuestionText => txtQuestion.Text;

    public void ClearQuestion() => txtQuestion.Clear();

    public void ShowUnavailable(string message)
    {
        txtQuestion.Enabled = false;
        btnAsk.Enabled = false;
        Write(message + Environment.NewLine, _note, SystemColors.GrayText);
    }

    public void ShowQuestion(string question)
    {
        _answering = false;
        _questions++;
        if (_chatTitle.Length == 0)
            _chatTitle = question.Length > 0
                ? ChatTitle.Summarise(question)
                : string.Join(", ", _attached.Select(Path.GetFileName));
        Write("You" + Environment.NewLine, _bold, SystemColors.WindowText);
        if (question.Length > 0)
            Write(question + Environment.NewLine, null, SystemColors.WindowText);
        if (_attached.Count > 0)
            Write("Attached: " + string.Join(", ", _attached.Select(Path.GetFileName)) + Environment.NewLine,
                  _note, SystemColors.GrayText);
        Write(Environment.NewLine, null, SystemColors.WindowText);
    }

    public void BeginAnswer()
    {
        ShowOpens(Array.Empty<(string, string)>());
        _answer.Clear();
        Write("PharmaX Assistant" + Environment.NewLine, _bold, SystemColors.Highlight);
        _answering = true;
    }

    public void ShowOpens(IReadOnlyList<(string Key, string Caption)> opens)
    {
        ArgumentNullException.ThrowIfNull(opens);

        pnlOpens.Controls.Clear();

        foreach (var (key, caption) in opens)
        {
            var button = new Button
            {
                AutoSize = true,
                Margin = new Padding(4, 4, 4, 4),
                Padding = new Padding(8, 2, 8, 2),
                Tag = key,
                Text = caption
            };
            button.Click += (_, _) => OpenRequested?.Invoke((string)button.Tag!);
            pnlOpens.Controls.Add(button);
        }

        var any = opens.Count > 0;
        pnlOpens.Visible = any;
        pnlOpens.Height = any ? 44 : 0;
    }

    public event Action<string>? OpenRequested;

    public void AppendAnswer(string text)
    {
        // The guide marks a button with **double asterisks**, which is
        // markdown. This box is not a markdown page, so those are drawn
        // bold and each numbered step is given a line of its own.
        foreach (var (part, bold) in AssistantText.Parts(text))
            Write(part, bold ? _bold : null, SystemColors.WindowText);
    }

    /// <summary>
    /// A section of the guide: its title as a heading, a picture of its
    /// menu path being clicked, and under each step that names something
    /// on the screen, that part of the screen with it outlined.
    /// </summary>
    public void AppendGuide(string section)
    {
        var guide = section.Replace("\r\n", "\n", StringComparison.Ordinal);
        var title = guide.Split('\n', 2)[0];
        var rest = guide[title.Length..].TrimStart('\n');
        Write(title.Trim() + Environment.NewLine, _heading, PhxTheme.Title);
        Write(Environment.NewLine, _gap, SystemColors.WindowText, keep: false);

        using var screen = GuideScreens.ScreenOf(guide) is { } key ? GuideScreens.Draw(key) : null;
        var menu = GuideScreens.MenuPathIn(rest);
        var pieces = screen is null
            ? new[] { (rest, (IReadOnlyList<string>?)null) }
            : GuideScreens.Split(rest, screen.Has);

        var shown = false;
        var at = 0;

        foreach (var (text, captions) in pieces)
        {
            if (menu is { } path && path.LineEnd > at && path.LineEnd <= at + text.Length)
            {
                AppendAnswer(text[..(path.LineEnd - at)]);
                shown |= ShowMenu(path.Names);
                AppendAnswer(text[(path.LineEnd - at)..]);
            }
            else AppendAnswer(text);

            at += text.Length;
            if (captions is null || screen is null) continue;

            using var picture = screen.Picture(captions);
            using var whole = screen.Outlined(captions);
            ShowPicture(picture, whole);
            shown = true;
        }

        if (shown)
            Write(Environment.NewLine + "Click a picture to see the whole window." + Environment.NewLine,
                  _note, SystemColors.GrayText, keep: false);
    }

    /// The main window's menu with <paramref name="path"/> opened on it.
    private bool ShowMenu(IReadOnlyList<string> path)
    {
        var menu = System.Windows.Forms.Application.OpenForms.OfType<Form>()
            .Select(f => f.MainMenuStrip)
            .FirstOrDefault(m => m is { IsHandleCreated: true });
        if (menu is null) return false;

        using var picture = MenuPicture.Draw(menu, path);
        if (picture is null) return false;

        ShowPicture(picture, null);
        return true;
    }

    /// Where each picture sits in the conversation, and the whole window
    /// it was cut from as PNG, for a click to open it.
    private readonly List<(int At, byte[] Png)> _pictures = new();

    /// <summary>
    /// A picture in the conversation on a line of its own, a little apart
    /// from the text, no wider than the box; <paramref name="whole"/>, the
    /// window it was cut from, opens on a click.
    /// </summary>
    private void ShowPicture(Bitmap picture, Bitmap? whole)
    {
        if (IsDisposed) return;

        using var png = new MemoryStream();
        picture.Save(png, System.Drawing.Imaging.ImageFormat.Png);

        byte[] wholePng;
        if (whole is null) wholePng = png.ToArray();
        else
        {
            using var stream = new MemoryStream();
            whole.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            wholePng = stream.ToArray();
        }

        var room = rtbConversation.ClientSize.Width - 2 * Inset() - LogicalToDeviceUnits(24);
        var width = Math.Min(picture.Width, Math.Max(240, room));
        var height = picture.Height * width / picture.Width;
        var dpi = Math.Max(96, DeviceDpi);

        var rtf = new System.Text.StringBuilder(@"{\rtf1{\pict\pngblip");
        rtf.Append(System.Globalization.CultureInfo.InvariantCulture,
            $@"\picw{picture.Width}\pich{picture.Height}\picwgoal{width * 1440 / dpi}\pichgoal{height * 1440 / dpi} ");
        rtf.Append(Convert.ToHexString(png.ToArray()));
        rtf.Append(@"}\par}");

        Write(Environment.NewLine, _gap, SystemColors.WindowText, keep: false);

        var at = rtbConversation.TextLength;
        rtbConversation.SelectionStart = at;
        rtbConversation.SelectionLength = 0;

        // A read-only box refuses a picture put in, even from code.
        rtbConversation.ReadOnly = false;
        rtbConversation.SelectedRtf = rtf.ToString();
        rtbConversation.ReadOnly = true;
        _pictures.Add((at, wholePng));
        if (_answering) _answer.Add(new AnswerPiece("", Picture: png.ToArray()));

        Write(Environment.NewLine, null, SystemColors.WindowText, keep: false);
    }

    /// A picture in the conversation, clicked: full size, in a window of its own.
    private void OpenPictureAtSelection()
    {
        if (rtbConversation.SelectionType != RichTextBoxSelectionTypes.Object) return;

        var found = _pictures.FirstOrDefault(p => p.At == rtbConversation.SelectionStart);
        if (found.Png is null) return;

        using var png = new MemoryStream(found.Png);
        using var picture = Image.FromStream(png);
        var area = Screen.FromControl(this).WorkingArea;

        using var viewer = new Form
        {
            ClientSize = new Size(Math.Min(picture.Width, area.Width * 9 / 10),
                                  Math.Min(picture.Height, area.Height * 9 / 10)),
            KeyPreview = true,
            MinimizeBox = false,
            ShowIcon = false,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.CenterParent,
            Text = "PharmaX - Picture (Esc to close)"
        };
        viewer.Controls.Add(new PictureBox { Dock = DockStyle.Fill, Image = picture, SizeMode = PictureBoxSizeMode.Zoom });
        viewer.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) viewer.Close();
        };
        viewer.ShowDialog(this);
    }

    public void EndAnswer(string disclaimer)
    {
        Write(Environment.NewLine + disclaimer + Environment.NewLine + Environment.NewLine,
              _note, SystemColors.GrayText);
        btnSave.Enabled = _answer.Any(p => p.Picture is not null || p.Text.Trim().Length > 0);
        KeepToday();
    }

    private void KeepToday()
    {
        if (_user is not { } user || IsDisposed) return;

        try
        {
            AssistantHistory.Save(user, _day, _chatId, new AssistantHistory.Chat(_chatTitle, _chatStarted,
                rtbConversation.Rtf ?? "", _pictures.Select(p => new AssistantHistory.DayPicture(p.At, p.Png)).ToList()));

            if (_chats.All(c => c.Id != _chatId))
            {
                _chats.Insert(0, new AssistantHistory.Listed(_chatId, _chatTitle, _chatStarted));
                ShowChats();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            // The answer is on screen; only reopening it later is lost.
            ErrorHandler.Log(ex);
        }
    }

    public void ShowStatus(string message) => lblStatus.Text = message;

    // ---- Files in and out ----------------------------------------------

    private readonly List<string> _attached = new();

    public IReadOnlyList<string> AttachedFiles => _attached.ToList();

    public void ClearAttachments()
    {
        _attached.Clear();
        ShowAttached();
    }

    private void ChooseFiles()
    {
        static string Patterns(IEnumerable<string> kinds) => string.Join(";", kinds.Select(k => "*" + k));

        using var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Title = "Attach to the question",
            Filter = $"Pictures, PDFs, Excel, Word and text|{Patterns(Attachments.Pictures.Concat(Attachments.Documents))}"
                     + $"|Pictures|{Patterns(Attachments.Pictures)}|PDF|*.pdf|Excel and CSV|*.xlsx;*.xlsm;*.csv"
                     + "|Word|*.docx|Text|*.txt;*.md"
        };

        if (dialog.ShowDialog(this) == DialogResult.OK) Attach(dialog.FileNames);
    }

    private void Attach(IEnumerable<string> paths)
    {
        var refused = new List<string>();

        foreach (var path in paths)
        {
            if (!Attachments.CanAttach(path)) refused.Add(Path.GetFileName(path));
            else if (!_attached.Contains(path, StringComparer.OrdinalIgnoreCase)) _attached.Add(path);
        }

        ShowAttached();
        ShowStatus(refused.Count == 0
            ? ""
            : $"Not attached: {string.Join(", ", refused)}. Pictures, PDFs, Excel, Word and text files can be - not videos.");
        txtQuestion.Focus();
    }

    /// One button per file attached; clicking it takes the file off.
    private void ShowAttached()
    {
        foreach (var old in pnlAttached.Controls.Cast<Control>().ToList()) old.Dispose();

        foreach (var path in _attached)
        {
            var button = new Button
            {
                AccessibleName = "Remove " + Path.GetFileName(path),
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(3, 4, 3, 0),
                Text = Path.GetFileName(path) + "   \u2715"
            };
            button.FlatAppearance.BorderColor = SystemColors.ControlDark;
            button.Click += (_, _) =>
            {
                _attached.Remove(path);
                ShowAttached();
            };
            pnlAttached.Controls.Add(button);
        }

        pnlAttached.Visible = _attached.Count > 0;
    }

    /// The last answer as written, for saving.
    private readonly List<AnswerPiece> _answer = new();

    /// Between the start of an answer and the next question: what is written is the answer.
    private bool _answering;

    public IReadOnlyList<AnswerPiece> LastAnswer => _answer.ToList();

    public string? AskSavePath(AnswerFormat format, string suggestedName)
    {
        var (extension, kind) = format switch
        {
            AnswerFormat.Pdf => ("pdf", "PDF"),
            AnswerFormat.Excel => ("xlsx", "Excel workbook"),
            _ => ("docx", "Word document")
        };

        using var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = extension,
            FileName = suggestedName,
            Filter = $"{kind}|*.{extension}",
            OverwritePrompt = true,
            Title = "Save the answer as " + kind
        };

        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    public event Action<AnswerFormat>? SaveRequested;

    public void AppendPicture(byte[] png)
    {
        using var stream = new MemoryStream(png);
        using var picture = new Bitmap(stream);
        ShowPicture(picture, null);
    }

    public void OfferToOpen(string path) => ShowSaved(path);

    // ---- Writing -------------------------------------------------------

    /// <summary>
    /// Adds text at the end of the conversation in the given style, and
    /// keeps the end in view. Kept as part of the answer for saving unless
    /// <paramref name="keep"/> says not.
    /// </summary>
    private void Write(string text, Font? font, Color colour, bool keep = true)
    {
        if (IsDisposed) return;

        rtbConversation.SelectionStart = rtbConversation.TextLength;
        rtbConversation.SelectionLength = 0;
        rtbConversation.SelectionFont = font ?? rtbConversation.Font;
        rtbConversation.SelectionColor = colour;
        rtbConversation.AppendText(text);
        rtbConversation.ScrollToCaret();

        if (keep && _answering) _answer.Add(new AnswerPiece(text, font == _bold || font == _heading));
    }

    /// The space kept clear inside the conversation's left and right edges.
    private int Inset() => LogicalToDeviceUnits(20);

    /// <summary>
    /// Room between the conversation and its edges. A rich edit box has
    /// no padding, so the area it writes in is set smaller than itself;
    /// it puts that back on every resize, hence again after each.
    /// </summary>
    private void SetMargins()
    {
        if (!rtbConversation.IsHandleCreated) return;

        var area = new Rect
        {
            Left = Inset(),
            Top = LogicalToDeviceUnits(10),
            Right = Math.Max(Inset() + 1, rtbConversation.ClientSize.Width - Inset()),
            Bottom = rtbConversation.ClientSize.Height
        };
        SendMessage(rtbConversation.Handle, SetRect, IntPtr.Zero, ref area);
    }

    private const int SetRect = 0xB3;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, ref Rect area);

    public event EventHandler? AskRequested;
    public event EventHandler? CloseRequested;
}
