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
    private readonly Label _problem = new();
    private readonly int _decimals;
    private readonly decimal? _percentOf;

    private frmNumberPrompt(string title, string prompt, decimal current,
                            int decimals, decimal? percentOf = null)
    {
        _decimals = decimals;
        _percentOf = percentOf;
        _input.AllowsPercent = percentOf is not null;
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

        // Where the answer is not a number, say so and stay open. OK with
        // "20%" in the box used to close the prompt and do nothing, which
        // looked exactly like the discount had been refused (ADM-OBS-08).
        _problem.AutoSize = false;
        _problem.ForeColor = PhxTheme.Negative;
        _problem.Location = new Point(16, 70);
        _problem.Name = "lblProblem";
        _problem.Size = new Size(160, 40);
        Controls.Add(_problem);

        // A question longer than a line - a product's whole name and its
        // pack - wraps and moves the rest down, rather than being cut off
        // before it has asked anything.
        var needed = TextRenderer.MeasureText(prompt ?? "", label.Font,
            new Size(label.Width, 0),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
        var extra = Math.Max(0, needed - label.Height);

        if (extra > 0)
        {
            label.Height += extra;

            foreach (var below in new Control[] { _input, ok, cancel, _problem })
                below.Top += extra;

            ClientSize = new Size(ClientSize.Width, ClientSize.Height + extra);
        }

        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK) return;

            Interpret(_input.Text, _percentOf, _decimals, out var problem);
            if (problem is null) return;

            _problem.Text = problem;
            e.Cancel = true;
        };
    }

    public decimal? Value => Interpret(_input.Text, _percentOf, _decimals,
                                       out _);

    /// <summary>
    /// What was typed, as a number: an amount, or - where the prompt takes
    /// one - a percentage of the figure it was asked about.
    /// </summary>
    internal static decimal? Interpret(string text, decimal? percentOf,
        int decimals, out string? problem)
    {
        problem = null;
        var typed = (text ?? "").Trim();

        if (percentOf is decimal whole
            && typed.EndsWith('%'))
        {
            if (!decimal.TryParse(typed[..^1].Trim(), NumberStyles.Number,
                    CultureInfo.CurrentCulture, out var percent)
                || percent < 0m || percent > 100m)
            {
                problem = "A percentage from 0% to 100%.";
                return null;
            }

            return Math.Round(whole * percent / 100m, decimals,
                              MidpointRounding.AwayFromZero);
        }

        if (!decimal.TryParse(typed, NumberStyles.Number,
                CultureInfo.CurrentCulture, out var value))
        {
            problem = percentOf is null
                ? "Enter a number."
                : "Enter an amount, or a percentage such as 10%.";
            return null;
        }

        return Math.Round(value, decimals, MidpointRounding.AwayFromZero);
    }

    public static decimal? Ask(IWin32Window owner, string title,
        string prompt, decimal current, int decimals)
    {
        using var dialog = new frmNumberPrompt(title, prompt, current,
                                               decimals);

        return dialog.ShowDialog(owner) == DialogResult.OK
            ? dialog.Value
            : null;
    }

    /// <summary>
    /// Asks for an amount off something, typed as money or as a
    /// percentage of it.
    /// </summary>
    public static decimal? AskAmountOrPercent(IWin32Window owner,
        string title, string prompt, decimal of)
    {
        using var dialog = new frmNumberPrompt(title, prompt, 0m, 2, of);

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
/// Asks for a new password: typed twice, and never shown.
/// </summary>
/// <remarks>
/// The starting and reset passwords were taken in the plain text prompt,
/// readable by anyone standing at the screen while they were typed
/// (ADM-DEF-02). Masked, a password has to be typed twice, because a slip
/// nobody can see would lock the new user out of an account before they
/// ever reached it. Returned exactly as typed: a space is part of a
/// password, and trimming one would set a different password from the
/// one the administrator handed over.
/// </remarks>
public sealed class frmPasswordPrompt : Form
{
    private readonly TextBox _password = new();
    private readonly TextBox _confirm = new();
    private readonly Label _problem = new();

    private frmPasswordPrompt(string title, string prompt)
    {
        Name = "frmPasswordPrompt";

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

        var top = label.Bottom + 8;

        var passwordLabel = new Label
        {
            AutoSize = false,
            Location = new Point(16, top + 3),
            Size = new Size(120, 20),
            Text = "&Password"
        };

        _password.AccessibleName = "Password";
        _password.Location = new Point(140, top);
        _password.MaxLength = 128;
        _password.Name = "txtPassword";
        _password.Size = new Size(276, 23);
        _password.UseSystemPasswordChar = true;

        var confirmLabel = new Label
        {
            AutoSize = false,
            Location = new Point(16, top + 35),
            Size = new Size(120, 20),
            Text = "Type it &again"
        };

        _confirm.AccessibleName = "Type it again";
        _confirm.Location = new Point(140, top + 32);
        _confirm.MaxLength = 128;
        _confirm.Name = "txtConfirmPassword";
        _confirm.Size = new Size(276, 23);
        _confirm.UseSystemPasswordChar = true;

        _problem.AutoSize = false;
        _problem.ForeColor = PhxTheme.Negative;
        _problem.Location = new Point(16, top + 64);
        _problem.Name = "lblProblem";
        _problem.Size = new Size(400, 20);

        var ok = new Button
        {
            DialogResult = DialogResult.OK,
            Location = new Point(260, top + 92),
            Name = "btnOk",
            Size = new Size(75, 28),
            Text = "OK"
        };

        var cancel = new Button
        {
            DialogResult = DialogResult.Cancel,
            Location = new Point(341, top + 92),
            Name = "btnCancel",
            Size = new Size(75, 28),
            Text = "Cancel"
        };

        Controls.AddRange(new Control[]
        {
            label, passwordLabel, _password, confirmLabel, _confirm,
            _problem, ok, cancel
        });

        AcceptButton = ok;
        CancelButton = cancel;
        ClientSize = new Size(432, top + 134);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = WindowTitles.Brand(title);

        Shown += (_, _) => _password.Focus();

        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK) return;

            var problem = Refuse(_password.Text, _confirm.Text);
            if (problem is null) return;

            _problem.Text = problem;
            e.Cancel = true;
        };
    }

    /// Why the two boxes cannot be accepted, or null where they can. The
    /// strength rules themselves belong to the service that sets it.
    internal static string? Refuse(string password, string confirm) =>
        string.IsNullOrEmpty(password)
            ? "Type the new password."
            : !string.Equals(password, confirm, StringComparison.Ordinal)
                ? "The two passwords are not the same."
                : null;

    public static string? Ask(IWin32Window owner, string title,
                              string prompt)
    {
        using var dialog = new frmPasswordPrompt(title, prompt);

        return dialog.ShowDialog(owner) == DialogResult.OK
            ? dialog._password.Text
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
    private frmTextViewer(string title, string body,
        IReadOnlyList<(string Text, Action Run)>? actions = null)
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

        // What can be done with the page besides reading it: print it,
        // send it on.
        var x = 12;

        foreach (var (caption, run) in actions ?? Array.Empty<(string, Action)>())
        {
            var button = new Button
            {
                Location = new Point(x, 8),
                Size = new Size(140, 30),
                Text = caption,
                UseVisualStyleBackColor = true
            };

            button.Click += (_, _) => run();
            footer.Controls.Add(button);
            x += 148;
        }

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

    public static void Show(IWin32Window owner, string title, string body,
        IReadOnlyList<(string Text, Action Run)> actions)
    {
        using var dialog = new frmTextViewer(title, body, actions);
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
