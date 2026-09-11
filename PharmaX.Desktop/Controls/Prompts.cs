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

        var label = new Label
        {
            AutoSize = false,
            Location = new Point(16, 16),
            Size = new Size(320, 20),
            Text = prompt
        };

        _input.Location = new Point(16, 42);
        _input.Size = new Size(320, 26);
        _input.Text = current.ToString($"N{decimals}",
                                       CultureInfo.CurrentCulture);

        var ok = new Button
        {
            DialogResult = DialogResult.OK,
            Location = new Point(180, 82),
            Size = new Size(75, 28),
            Text = "OK"
        };

        var cancel = new Button
        {
            DialogResult = DialogResult.Cancel,
            Location = new Point(261, 82),
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
        Text = title;

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

    private frmTextPrompt(string title, string prompt)
    {
        var label = new Label
        {
            AutoSize = false,
            Location = new Point(16, 16),
            Size = new Size(400, 20),
            Text = prompt
        };

        _input.Location = new Point(16, 42);
        _input.MaxLength = 250;
        _input.Size = new Size(400, 23);

        var ok = new Button
        {
            DialogResult = DialogResult.OK,
            Location = new Point(260, 82),
            Size = new Size(75, 28),
            Text = "OK"
        };

        var cancel = new Button
        {
            DialogResult = DialogResult.Cancel,
            Location = new Point(341, 82),
            Size = new Size(75, 28),
            Text = "Cancel"
        };

        Controls.AddRange(new Control[] { label, _input, ok, cancel });

        AcceptButton = ok;
        CancelButton = cancel;
        ClientSize = new Size(432, 124);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = title;

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
                              string prompt)
    {
        using var dialog = new frmTextPrompt(title, prompt);

        return dialog.ShowDialog(owner) == DialogResult.OK
            ? dialog._input.Text.Trim()
            : null;
    }
}
