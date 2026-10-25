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

    /// Not from inside itself: Ctrl+Shift+A here would open a second one.
    protected override bool OffersAssistant => false;

    /// Designer only. Never called at runtime.
    public frmAssistant()
    {
        InitializeComponent();
        lblDisclaimer.Text = ChatDisclaimer.Text;

        btnAsk.Click += (_, _) => AskRequested?.Invoke(this, EventArgs.Empty);
        btnClose.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        txtQuestion.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;

            // No ding: Enter in the question box asks it.
            e.SuppressKeyPress = true;
            AskRequested?.Invoke(this, EventArgs.Empty);
        };
    }

    /// Runtime. Resolved by the container.
    public frmAssistant(AssistantPresenter presenter,
                        Application.Security.IAuthenticationService auth) : this()
    {
        _auth = auth;
        _presenter = presenter;
        _presenter.Attach(this);
    }

    private readonly Application.Security.IAuthenticationService? _auth;

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

        txtQuestion.Focus();
        if (_presenter is not null) await _presenter.InitialiseAsync();
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
        Write("You" + Environment.NewLine, _bold, SystemColors.WindowText);
        Write(question + Environment.NewLine + Environment.NewLine, null, SystemColors.WindowText);
    }

    public void BeginAnswer() =>
        Write("PharmaX Assistant" + Environment.NewLine, _bold, SystemColors.Highlight);

    public void AppendAnswer(string text) => Write(text, null, SystemColors.WindowText);

    public void EndAnswer(string disclaimer) =>
        Write(Environment.NewLine + disclaimer + Environment.NewLine + Environment.NewLine,
              _note, SystemColors.GrayText);

    public void ShowStatus(string message) => lblStatus.Text = message;

    /// Adds text at the end of the conversation in the given style, and
    /// keeps the end in view.
    private void Write(string text, Font? font, Color colour)
    {
        if (IsDisposed) return;

        rtbConversation.SelectionStart = rtbConversation.TextLength;
        rtbConversation.SelectionLength = 0;
        rtbConversation.SelectionFont = font ?? rtbConversation.Font;
        rtbConversation.SelectionColor = colour;
        rtbConversation.AppendText(text);
        rtbConversation.ScrollToCaret();
    }

    public event EventHandler? AskRequested;
    public event EventHandler? CloseRequested;
}
