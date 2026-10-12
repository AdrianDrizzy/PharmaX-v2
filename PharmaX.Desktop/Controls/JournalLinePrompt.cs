using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Controls;

/// <summary>
/// One journal line - account, which side, how much, a note - asked for
/// in a dialog rather than typed into the grid.
/// </summary>
/// <remarks>
/// The journal grid can only be typed into with the keyboard focus in its
/// cells, which a screen reader and UI Automation cannot give it, so a
/// journal could not be keyed without the mouse and the eyes on the
/// grid (ADM-BLK-G04). The grid still takes typing; this is the other
/// way in.
/// </remarks>
public sealed class frmJournalLinePrompt : Form
{
    private readonly ComboBox _account = new();
    private readonly RadioButton _debit = new();
    private readonly RadioButton _credit = new();
    private readonly MoneyTextBox _amount = new();
    private readonly TextBox _note = new();
    private readonly Label _problem = new();

    private frmJournalLinePrompt(
        IReadOnlyList<(string Code, string Label)> accounts)
    {
        Icon = PhxIcons.AppIcon;
        PhxTheme.Follow(this);
        Name = "frmJournalLinePrompt";

        Label Caption(string text, int y) => new()
        {
            AutoSize = false,
            Location = new Point(16, y + 3),
            Size = new Size(90, 20),
            Text = text
        };

        _account.AccessibleName = "Account";
        _account.DropDownStyle = ComboBoxStyle.DropDownList;
        _account.Location = new Point(110, 16);
        _account.Name = "cboAccount";
        _account.Size = new Size(300, 23);

        foreach (var (code, label) in accounts)
            _account.Items.Add(new Choice(code, label));

        _debit.AutoSize = true;
        _debit.Checked = true;
        _debit.Location = new Point(110, 52);
        _debit.Name = "optDebit";
        _debit.Text = "&Debit";

        _credit.AutoSize = true;
        _credit.Location = new Point(200, 52);
        _credit.Name = "optCredit";
        _credit.Text = "C&redit";

        _amount.AccessibleName = "Amount";
        _amount.Location = new Point(110, 84);
        _amount.Name = "txtAmount";
        _amount.PlaceholderText = "0.00";
        _amount.Size = new Size(160, 26);

        _note.AccessibleName = "Note";
        _note.Location = new Point(110, 120);
        _note.MaxLength = 300;
        _note.Name = "txtNote";
        _note.PlaceholderText = "e.g. Bank charges for October (optional)";
        _note.Size = new Size(300, 23);

        _problem.AutoSize = false;
        _problem.ForeColor = PhxTheme.Negative;
        _problem.Location = new Point(16, 152);
        _problem.Name = "lblProblem";
        _problem.Size = new Size(394, 20);

        var ok = new Button
        {
            DialogResult = DialogResult.OK,
            Location = new Point(254, 180),
            Name = "btnOk",
            Size = new Size(75, 28),
            Text = "OK"
        };

        var cancel = new Button
        {
            DialogResult = DialogResult.Cancel,
            Location = new Point(335, 180),
            Name = "btnCancel",
            Size = new Size(75, 28),
            Text = "Cancel"
        };

        Controls.AddRange(new Control[]
        {
            Caption("&Account", 16), _account,
            Caption("Side", 52), _debit, _credit,
            Caption("A&mount", 84), _amount,
            Caption("&Note", 120), _note,
            _problem, ok, cancel
        });

        AcceptButton = ok;
        CancelButton = cancel;
        ClientSize = new Size(428, 222);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = WindowTitles.Brand("PharmaX - Journal line");

        Shown += (_, _) => _account.Focus();

        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK) return;

            var problem = _account.SelectedItem is null
                ? "Choose the account."
                : _amount.Amount is not decimal value || value <= 0m
                    ? "Enter an amount greater than zero."
                    : null;

            if (problem is null) return;

            _problem.Text = problem;
            e.Cancel = true;
        };
    }

    public static (string Code, decimal Amount, bool IsDebit, string? Note)? Ask(
        IWin32Window owner, IReadOnlyList<(string Code, string Label)> accounts)
    {
        using var dialog = new frmJournalLinePrompt(accounts);

        if (dialog.ShowDialog(owner) != DialogResult.OK
            || dialog._account.SelectedItem is not Choice chosen
            || dialog._amount.Amount is not decimal amount)
            return null;

        var note = dialog._note.Text.Trim();

        return (chosen.Code, amount, dialog._debit.Checked,
                note.Length == 0 ? null : note);
    }

    private sealed record Choice(string Code, string Label)
    {
        public override string ToString() => Label;
    }
}
