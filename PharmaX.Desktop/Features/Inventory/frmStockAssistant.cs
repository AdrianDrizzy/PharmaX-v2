using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Inventory;

/// <summary>
/// The stock assistant's pop-up: what is out, low and expiring, and the
/// draft orders it made for them, with a way to each.
/// </summary>
/// <remarks>
/// Every word and figure here is worked out by code (StockAlertAssistant,
/// DraftNotice) - the language model is not asked, because it gets
/// numbers wrong. Nothing on this window sends anything to a supplier.
/// </remarks>
public sealed class frmStockAssistant : PhxFormBase
{
    public enum Choice { None, Drafts, Alerts }

    private readonly Label _heading = new();
    private readonly TextBox _summary = new();
    private readonly LinkLabel _drafts = new();
    private readonly Label _missing = new();
    private readonly Button _alerts = new();
    private readonly Button _view = new();
    private readonly Button _close = new();
    private readonly Panel _foot = new();

    public frmStockAssistant()
    {
        Name = "frmStockAssistant";
        Text = "PharmaX - Stock assistant";

        _heading.Dock = DockStyle.Top;
        _heading.Height = 34;
        _heading.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
        _heading.ForeColor = PhxTheme.Title;
        _heading.Padding = new Padding(10, 6, 0, 0);
        _heading.Name = "lblHeading";

        _summary.Dock = DockStyle.Fill;
        _summary.Multiline = true;
        _summary.ReadOnly = true;
        _summary.ScrollBars = ScrollBars.Vertical;
        _summary.Font = new Font("Segoe UI", 10F);
        _summary.Name = "txtSummary";
        _summary.TabStop = false;

        var foot = _foot;
        foot.Dock = DockStyle.Bottom;
        foot.Height = 190;
        foot.Padding = new Padding(10, 6, 10, 6);

        _drafts.Dock = DockStyle.Top;
        _drafts.Height = 110;
        _drafts.Font = new Font("Segoe UI", 10F);
        _drafts.Name = "lnkDrafts";
        _drafts.Visible = false;
        _drafts.LinkClicked += (_, _) => Choose(Choice.Drafts);

        _missing.Dock = DockStyle.Top;
        _missing.Height = 34;
        _missing.ForeColor = PhxTheme.ProblemText;
        _missing.Name = "lblMissing";

        _alerts.Location = new Point(10, 152);
        _alerts.Size = new Size(160, 30);
        _alerts.Name = "btnAlerts";
        _alerts.Text = "Open Stock &Alerts";
        _alerts.Click += (_, _) => Choose(Choice.Alerts);

        _view.Location = new Point(180, 152);
        _view.Size = new Size(170, 30);
        _view.Name = "btnDrafts";
        _view.Text = "&View draft orders";
        _view.Visible = false;
        _view.Click += (_, _) => Choose(Choice.Drafts);

        _close.Location = new Point(510, 152);
        _close.Size = new Size(100, 30);
        _close.Name = "btnClose";
        _close.Text = "Close";
        _close.DialogResult = DialogResult.Cancel;
        _close.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        foot.Controls.Add(_missing);
        foot.Controls.Add(_drafts);
        foot.Controls.AddRange(new Control[] { _alerts, _view, _close });

        Controls.Add(_summary);
        Controls.Add(_heading);
        Controls.Add(foot);

        CancelButton = _close;
        ClientSize = new Size(620, 560);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
    }

    /// What the operator asked for on the way out.
    public Choice Chosen { get; private set; }

    /// <param name="summary">The alert summary; empty leaves the box out.</param>
    /// <param name="drafts">The drafts notice; empty leaves the link out.</param>
    /// <param name="missing">What could not be drafted, or empty.</param>
    public void Prepare(string heading, string summary, string drafts,
                        string missing, bool canOpenAlerts)
    {
        _heading.Text = heading;
        _summary.Text = summary;
        _summary.Visible = summary.Length > 0;

        // A LinkLabel counts a line break as one character, so the link is
        // placed in the text with "\n" breaks or it lands short of its words.
        drafts = drafts.ReplaceLineEndings("\n");

        _drafts.Text = drafts;
        _drafts.Visible = drafts.Length > 0;
        _view.Visible = drafts.Length > 0;

        // The notice reads as sentences; the link is the one that says
        // "click here".
        var at = drafts.IndexOf("Click here", StringComparison.Ordinal);
        if (at >= 0)
        {
            var end = drafts.IndexOf('.', at);
            _drafts.LinkArea = new LinkArea(at, (end < 0 ? drafts.Length : end + 1) - at);
        }

        _missing.Text = missing;
        _missing.Visible = missing.Length > 0;
        _alerts.Visible = canOpenAlerts && summary.Length > 0;
    }

    /// <summary>
    /// The notices below the summary as tall as their text, measured once
    /// the window has been scaled to the display: at 150 per cent a fixed
    /// height cut the drafts notice off above its "Click here".
    /// </summary>
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        var width = _foot.ClientSize.Width - _foot.Padding.Horizontal;

        int Fit(Control control) => control.Visible
            ? TextRenderer.MeasureText(control.Text, control.Font,
                  new Size(width, int.MaxValue), TextFormatFlags.WordBreak).Height
              + control.Font.Height / 2
            : 0;

        _drafts.Height = Fit(_drafts);
        _missing.Height = Fit(_missing);

        var gap = _close.Height / 3;
        _foot.Height = _foot.Padding.Vertical + _drafts.Height + _missing.Height
                       + gap + _close.Height;
        _alerts.Top = _view.Top = _close.Top = _foot.Height - _foot.Padding.Bottom - _close.Height;

        // With no alerts to list, the window is only as tall as its notices.
        if (!_summary.Visible)
            ClientSize = new Size(ClientSize.Width, _heading.Height + _foot.Height);
    }

    private void Choose(Choice choice)
    {
        Chosen = choice;
        DialogResult = DialogResult.OK;
        Close();
    }
}
