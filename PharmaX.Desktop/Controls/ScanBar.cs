namespace PharmaX.Desktop.Controls;

/// <summary>
/// Scanner handling is the most common source of intermittent
/// point-of-sale defects, so it belongs in one reusable control rather
/// than in every form.
/// </summary>
public partial class ScanBar : UserControl
{
    private readonly System.Text.StringBuilder _buffer = new();
    private readonly System.Windows.Forms.Timer _idle = new();

    public event EventHandler<string>? CodeEntered;

    private SearchSuggestions? _suggestions;

    public ScanBar()
    {
        InitializeComponent();

        // A scanner delivers a whole code in a few milliseconds and ends
        // with Enter. A human types slowly and may not press Enter at all,
        // so an idle flush covers manual entry.
        _idle.Interval = 300;
        _idle.Tick += (_, _) => Flush();

        txtScan.KeyPress += OnKeyPress;
    }

    public void Reset()
    {
        _buffer.Clear();
        txtScan.Clear();
        txtScan.Focus();
    }

    public void TakeFocus() => txtScan.Focus();

    /// <summary>
    /// Offers matching products under the box while a name is being
    /// typed, so an item can be rung up from its first few letters.
    /// </summary>
    /// <remarks>
    /// A name no longer goes off on its own after a pause: that sent the
    /// first half of it as a code and cleared the box under the typing.
    /// It waits for Enter or a pick from the list. Digits alone are still
    /// flushed as before, so a scanner - or a code keyed in without Enter
    /// - behaves exactly as it always did.
    /// </remarks>
    public void SuggestFrom(Func<string, CancellationToken,
        Task<IReadOnlyList<Suggestion>>> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        _suggestions?.Dispose();
        _suggestions = new SearchSuggestions(txtScan, source)
        {
            Wanted = Suggest.IsName
        };

        _suggestions.Chosen += (_, chosen) =>
        {
            _idle.Stop();
            _buffer.Clear();
            txtScan.Clear();

            if (chosen.Value is string code) CodeEntered?.Invoke(this, code);
        };
    }

    /// What the suggestions under the box are offering, for a test.
    internal IReadOnlyList<Suggestion> Suggestions =>
        _suggestions?.Showing ?? Array.Empty<Suggestion>();

    /// The prompt shown to the left of the input, so the same control
    /// reads correctly on the till and on the dispensing screen.
    public string Prompt
    {
        get => lblPrompt.Text;
        set
        {
            lblPrompt.Text = value;

            // The prompt differs by screen, so the label takes the width
            // its own text needs. A fixed width clips the longer ones,
            // and a clipped prompt is the kind of defect nobody reports
            // and everybody works around.
            lblPrompt.Width = Math.Max(90,
                TextRenderer.MeasureText(value, lblPrompt.Font).Width + 8);
        }
    }

    private void OnKeyPress(object? sender, KeyPressEventArgs e)
    {
        if (e.KeyChar == (char)Keys.Return)
        {
            _idle.Stop();
            Flush();
            e.Handled = true;
            return;
        }

        if (char.IsControl(e.KeyChar)) return;

        _buffer.Append(e.KeyChar);
        _idle.Stop();

        // A name being typed waits for Enter or a suggestion; see
        // SuggestFrom.
        if (_suggestions is not null && Suggest.IsName(txtScan.Text + e.KeyChar))
            return;

        _idle.Start();
    }

    private void Flush()
    {
        _idle.Stop();
        _suggestions?.Hide();

        // What is in the box, which is what the operator sees: a name
        // typed slowly may have been corrected with backspace, which the
        // keystroke buffer never hears about.
        var code = (_suggestions is null ? _buffer.ToString() : txtScan.Text)
            .Trim();

        _buffer.Clear();
        txtScan.Clear();

        if (code.Length > 0) CodeEntered?.Invoke(this, code);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _suggestions?.Dispose();
            _idle.Dispose();
            components?.Dispose();
        }

        base.Dispose(disposing);
    }
}
