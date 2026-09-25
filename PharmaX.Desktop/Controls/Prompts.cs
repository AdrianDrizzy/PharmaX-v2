using PharmaX.Desktop.Mvp;
using System.Globalization;

namespace PharmaX.Desktop.Controls;

/// <summary>
/// Asks for one number. A shared dialog rather than one per question,
/// because "quantity" and "discount" differ only in their caption and
/// the number of decimals they accept.
/// </summary>
public sealed class frmNumberPrompt : Form
{
    private readonly MoneyTextBox _input = new();
    private readonly int _decimals;

    private frmNumberPrompt(string title, string prompt, decimal current,
                            int decimals)
    {
        _decimals = decimals;
        Name = "frmNumberPrompt";

        var label = new Label
        {
            AutoSize = false,
            Location = new Point(16, 16),
            Name = "lblPrompt",
            Size = new Size(320, 20),
            Text = prompt
        };

        _input.Location = new Point(16, 42);
        _input.Name = "txtPromptInput";
        _input.Size = new Size(320, 26);
        _input.Text = current.ToString($"N{decimals}",
                                       CultureInfo.CurrentCulture);

        var ok = new Button
        {
            DialogResult = DialogResult.OK,
            Location = new Point(180, 82),
            Name = "btnOk",
            Size = new Size(75, 28),
            Text = "OK"
        };

        var cancel = new Button
        {
            DialogResult = DialogResult.Cancel,
            Location = new Point(261, 82),
            Name = "btnCancel",
            Size = new Size(75, 28),
            Text = "Cancel"
        };

        Controls.AddRange(new Control[] { label, _input, ok, cancel });

        AcceptButton = ok;
        CancelButton = cancel;
        ClientSize = new Size(352, 124);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = WindowTitles.Brand(title);

        // Selected on open, so the operator types over the current value
        // rather than having to clear it first.
        Shown += (_, _) => { _input.Focus(); _input.SelectAll(); };
    }

    public decimal? Value =>
        _input.Amount is decimal value
            ? Math.Round(value, _decimals, MidpointRounding.AwayFromZero)
            : null;

    public static decimal? Ask(IWin32Window owner, string title,
        string prompt, decimal current, int decimals)
    {
        using var dialog = new frmNumberPrompt(title, prompt, current,
                                               decimals);

        return dialog.ShowDialog(owner) == DialogResult.OK
            ? dialog.Value
            : null;
    }
}

/// <summary>
/// Asks for one line of text, and refuses to return a blank one. Used
/// wherever a reason is mandatory: voiding a line, explaining a variance,
/// overriding a clinical warning.
/// </summary>
public sealed class frmTextPrompt : Form
{
    private readonly TextBox _input = new();

    private frmTextPrompt(string title, string prompt, string? initial)
    {
        // Named, so that assistive technology and the end-to-end suite
        // can find them (DEF-0030).
        Name = "frmTextPrompt";

        // Grows to the prompt, which can be a clinical warning of two or
        // three lines rather than a one-line question.
        var label = new Label
        {
            AutoSize = false,
            Location = new Point(16, 16),
            MaximumSize = new Size(400, 0),
            Name = "lblPrompt",
            Size = new Size(400, 20),
            Text = prompt
        };

        label.Height = Math.Max(20, TextRenderer.MeasureText(prompt,
            label.Font, new Size(400, 0), TextFormatFlags.WordBreak).Height);

        var top = label.Bottom + 6;

        _input.Location = new Point(16, top);
        _input.MaxLength = 250;
        _input.Name = "txtPromptInput";
        _input.Size = new Size(400, 23);
        _input.Text = initial ?? "";

        var ok = new Button
        {
            DialogResult = DialogResult.OK,
            Location = new Point(260, top + 40),
            Name = "btnOk",
            Size = new Size(75, 28),
            Text = "OK"
        };

        var cancel = new Button
        {
            DialogResult = DialogResult.Cancel,
            Location = new Point(341, top + 40),
            Name = "btnCancel",
            Size = new Size(75, 28),
            Text = "Cancel"
        };

        Controls.AddRange(new Control[] { label, _input, ok, cancel });

        AcceptButton = ok;
        CancelButton = cancel;
        ClientSize = new Size(432, top + 82);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = WindowTitles.Brand(title);

        Shown += (_, _) => { _input.Focus(); _input.SelectAll(); };

        // A reason that is only spaces is not a reason. Refused here so
        // that no caller has to remember to check.
        FormClosing += (_, e) =>
        {
            if (DialogResult == DialogResult.OK
                && string.IsNullOrWhiteSpace(_input.Text))
                e.Cancel = true;
        };
    }

    public static string? Ask(IWin32Window owner, string title,
                              string prompt, string? initial = null)
    {
        using var dialog = new frmTextPrompt(title, prompt, initial);

        return dialog.ShowDialog(owner) == DialogResult.OK
            ? dialog._input.Text.Trim()
            : null;
    }
}

/// <summary>
/// A read-only window for a block of text somebody needs to look at:
/// the postings behind a trial-balance figure, and anything else that is
/// a page rather than a grid.
/// </summary>
/// <remarks>
/// Monospaced and selectable, so the columns line up and the operator
/// can copy a line into an email to their accountant - which is the
/// commonest thing anybody does with a screen like this.
/// </remarks>
public sealed class frmTextViewer : Form
{
    private frmTextViewer(string title, string body)
    {
        var text = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9.75F),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            Text = body,
            WordWrap = false
        };

        var close = new Button
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            DialogResult = DialogResult.Cancel,
            Location = new Point(660, 8),
            Size = new Size(110, 30),
            Text = "&Close"
        };

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 46 };
        footer.Controls.Add(close);

        Controls.Add(text);
        Controls.Add(footer);

        CancelButton = close;
        ClientSize = new Size(790, 520);
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = WindowTitles.Brand(title);

        // The caret starts at the top rather than wherever a selection
        // left it, and nothing is selected on opening.
        Shown += (_, _) =>
        {
            text.SelectionStart = 0;
            text.SelectionLength = 0;
        };
    }

    public static void Show(IWin32Window owner, string title, string body)
    {
        using var dialog = new frmTextViewer(title, body);
        dialog.ShowDialog(owner);
    }
}

/// <summary>
/// Asks the operator to pick one of a short list.
/// </summary>
/// <remarks>
/// A dialog rather than a drop-down on a form, for the same reason as
/// the prompts above: the question is asked from three places and a
/// fourth copy of a combo box is a fourth place for it to be wrong.
/// Generic in the value, so the caller gets its own type back rather
/// than an index it has to interpret.
/// </remarks>
public sealed class frmChoicePrompt : Form
{
    private readonly ListBox _list = new();

    private frmChoicePrompt(string title, string prompt,
                            IEnumerable<string> choices)
    {
        var label = new Label
        {
            AutoSize = false,
            Location = new Point(16, 16),
            Size = new Size(400, 20),
            Text = prompt
        };

        _list.Location = new Point(16, 42);
        _list.Size = new Size(400, 140);
        _list.Items.AddRange(choices.Cast<object>().ToArray());
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;

        var ok = new Button
        {
            DialogResult = DialogResult.OK,
            Location = new Point(260, 196),
            Size = new Size(75, 28),
            Text = "OK"
        };

        var cancel = new Button
        {
            DialogResult = DialogResult.Cancel,
            Location = new Point(341, 196),
            Size = new Size(75, 28),
            Text = "Cancel"
        };

        Controls.AddRange(new Control[] { label, _list, ok, cancel });

        // Double-clicking a row is how everybody expects a list like
        // this to be answered.
        _list.DoubleClick += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };

        AcceptButton = ok;
        CancelButton = cancel;
        ClientSize = new Size(432, 238);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = WindowTitles.Brand(title);
    }

    public static TValue? Ask<TValue>(IWin32Window owner, string title,
        string prompt, IReadOnlyList<(string Text, TValue Value)> choices)
        where TValue : struct
    {
        ArgumentNullException.ThrowIfNull(choices);

        using var dialog = new frmChoicePrompt(title, prompt,
            choices.Select(c => c.Text));

        if (dialog.ShowDialog(owner) != DialogResult.OK) return null;

        var index = dialog._list.SelectedIndex;

        return index >= 0 && index < choices.Count
            ? choices[index].Value
            : null;
    }
}
