using System.ComponentModel;
using System.Globalization;

namespace PharmaX.Desktop.Controls;

/// <summary>
/// A text box that only ever holds money. Rejects the keystroke rather
/// than validating afterwards, because a cashier who can type letters
/// into an amount will, and the error message arrives too late to help.
/// </summary>
public sealed class MoneyTextBox : TextBox
{
    public MoneyTextBox()
    {
        TextAlign = HorizontalAlignment.Right;
        Font = new Font("Consolas", 11F);
        MaxLength = 14;
    }

    /// <summary>
    /// The amount, or null where the box is empty. Null and zero are
    /// different answers: an empty tender box is not a zero tender.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public decimal? Amount
    {
        get => decimal.TryParse(Text, NumberStyles.Number,
                                CultureInfo.CurrentCulture, out var value)
            ? value
            : null;

        set => Text = value?.ToString("N2", CultureInfo.CurrentCulture) ?? "";
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var separator = CultureInfo.CurrentCulture
            .NumberFormat.NumberDecimalSeparator[0];

        if (char.IsControl(e.KeyChar) || char.IsDigit(e.KeyChar))
        {
            base.OnKeyPress(e);
            return;
        }

        // One decimal separator, and a minus only at the very start.
        var allowed =
            (e.KeyChar == separator
             && !Text.Contains(separator, StringComparison.Ordinal))
            || (e.KeyChar == '-' && SelectionStart == 0
                && !Text.Contains('-', StringComparison.Ordinal));

        e.Handled = !allowed;
        base.OnKeyPress(e);
    }

    protected override void OnLeave(EventArgs e)
    {
        // Normalise on the way out, so 5 becomes 5.00 and the column of
        // amounts on screen lines up the way it will on the receipt.
        if (Amount is decimal value) Amount = value;

        base.OnLeave(e);
    }
}
