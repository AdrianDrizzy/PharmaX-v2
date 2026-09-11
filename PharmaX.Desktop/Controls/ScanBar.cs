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

    /// The prompt shown to the left of the input, so the same control
    /// reads correctly on the till and on the dispensing screen.
    public string Prompt
    {
        get => lblPrompt.Text;
        set => lblPrompt.Text = value;
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
        _idle.Start();
    }

    private void Flush()
    {
        _idle.Stop();

        var code = _buffer.ToString().Trim();
        _buffer.Clear();
        txtScan.Clear();

        if (code.Length > 0) CodeEntered?.Invoke(this, code);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _idle.Dispose();
            components?.Dispose();
        }

        base.Dispose(disposing);
    }
}
