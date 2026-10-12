using System.Globalization;
using System.Text.RegularExpressions;

namespace PharmaX.Desktop.Mvp;

/// <summary>
/// Gives every button on a screen a function key, the same key meaning
/// the same thing on every screen, and writes it on the button.
/// </summary>
/// <remarks>
/// <para>
/// The till had its keys and nothing else did, so everywhere else the
/// pharmacist let go of the keyboard to reach for the mouse. Assigned
/// here, once, from what each button says it does, rather than typed into
/// forty-seven designers: a screen added next year gets its keys without
/// anyone remembering to give it them.
/// </para>
/// <para>
/// A button whose caption already names its key - the till's "F6 Pay",
/// Reports' "Preview (F12)" - keeps it. Close and Cancel take Escape.
/// Every other button is matched against the standard meanings below,
/// and whatever is left over takes the next free key, then Shift with
/// one, so no button is ever without.
/// </para>
/// </remarks>
public static class FunctionKeys
{
    /// The keys handed out, in the order they are handed out.
    private static readonly Keys[] Free =
    {
        Keys.F2, Keys.F3, Keys.F4, Keys.F5, Keys.F6, Keys.F7, Keys.F8,
        Keys.F9, Keys.F10, Keys.F11, Keys.F12
    };

    /// <summary>What each key means on every screen.</summary>
    /// <remarks>
    /// Checked in this order, and the first word of the caption counts
    /// first: "Reprint labels" is a reprint (F12), not a print (F10).
    /// </remarks>
    private static readonly (Keys Key, string[] Words)[] Meanings =
    {
        (Keys.F12, new[] { "reprint", "documents", "claim form",
                           "test page" }),
        (Keys.F2, new[] { "find", "search", "look up", "lookup" }),
        (Keys.F3, new[] { "add", "new", "capture", "open till" }),
        (Keys.F4, new[] { "edit", "view", "open", "correct", "change",
                          "amend" }),
        (Keys.F5, new[] { "refresh", "reload" }),
        (Keys.F6, new[] { "save", "issue", "pay", "take payment", "record",
                          "post", "settle", "ok", "apply", "finalise",
                          "confirm", "receive", "sell", "dispense",
                          "create script", "log in", "sign in", "merge",
                          "raise", "refund", "print labels" }),
        (Keys.F7, new[] { "remove", "delete", "deactivate", "reactivate",
                          "void", "reverse", "reject", "write off",
                          "cancel script", "cancel order", "discard",
                          "take out of use", "retire", "cancel" }),
        (Keys.F8, new[] { "hold", "park", "quote" }),
        (Keys.F9, new[] { "recall", "resume", "choose", "select", "pick" }),
        (Keys.F10, new[] { "print", "preview", "what prints" }),
        (Keys.F11, new[] { "export" })
    };

    private static readonly Regex Written = new(
        @"^\s*(?:(?<shift>Shift\+)?F(?<n>\d{1,2})\b\s*|Esc\s+)|\((?<shift2>Shift\+)?F(?<n2>\d{1,2})\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Hands out the keys on a screen and writes them on its buttons.
    /// </summary>
    /// <param name="escape">
    /// Whether Escape may close the screen. Not at the till, where it
    /// voids a line.
    /// </param>
    /// <returns>Each key, and the button it presses.</returns>
    public static Dictionary<Keys, Button> Assign(Form form, bool escape = true)
    {
        ArgumentNullException.ThrowIfNull(form);

        var map = new Dictionary<Keys, Button>();
        var buttons = ButtonsOf(form).ToList();
        var waiting = new List<Button>();

        // What the captions already say.
        foreach (var button in buttons)
        {
            if (KeyWritten(button.Text) is Keys key && !map.ContainsKey(key))
            {
                map[key] = button;
                Keep(button, key, written: true);
            }
            else waiting.Add(button);
        }

        // Escape: the screen's own cancel button where it names one - a
        // "Cancel" that cancels a stock count is not the way out of the
        // window, and must not be put on the key that is.
        if (escape && !map.ContainsKey(Keys.Escape)
            && form.CancelButton is Button named && waiting.Contains(named))
        {
            map[Keys.Escape] = named;
            Keep(named, Keys.Escape, written: false);
            waiting.Remove(named);
        }

        // Otherwise Close or Cancel, as a dialog's Escape always has been.
        foreach (var button in waiting.ToList())
        {
            if (!escape || map.ContainsKey(Keys.Escape)
                || form.CancelButton is not null) break;

            var words = Words(button.Text);

            if (words is "close" or "cancel" or "exit" or "done"
                || words.StartsWith("close ", StringComparison.Ordinal))
            {
                map[Keys.Escape] = button;
                Keep(button, Keys.Escape, written: false);
                waiting.Remove(button);

                if (form.CancelButton is null) form.CancelButton = button;
            }
        }

        // The standard meanings.
        foreach (var button in waiting.ToList())
        {
            if (Meaning(button.Text) is Keys key && !map.ContainsKey(key))
            {
                map[key] = button;
                Keep(button, key, written: false);
                waiting.Remove(button);
            }
        }

        // Whatever is left: the next free key, then Shift with one.
        var spare = Free.Concat(Free.Select(k => Keys.Shift | k))
            .Where(k => !map.ContainsKey(k))
            .GetEnumerator();

        foreach (var button in waiting)
        {
            if (!spare.MoveNext()) break;

            map[spare.Current] = button;
            Keep(button, spare.Current, written: false);
        }

        return map;
    }

    /// <summary>A key as the button and the F1 list write it.</summary>
    public static string Name(Keys key)
    {
        if (key == Keys.Escape) return "Esc";

        var shift = (key & Keys.Shift) == Keys.Shift;
        var bare = key & ~Keys.Shift;

        return (shift ? "Shift+" : "") + bare.ToString();
    }

    /// <summary>
    /// The key a caption already names - "F6 Pay", "Preview (F12)",
    /// "Esc Void line" - or null.
    /// </summary>
    public static Keys? KeyWritten(string? caption)
    {
        var match = Written.Match(caption ?? "");
        if (!match.Success) return null;

        if (match.Value.TrimStart().StartsWith("Esc", StringComparison.OrdinalIgnoreCase))
            return Keys.Escape;

        var digits = match.Groups["n"].Success
            ? match.Groups["n"].Value
            : match.Groups["n2"].Value;

        if (!int.TryParse(digits, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var n) || n is < 1 or > 12)
            return null;

        var key = Keys.F1 + (n - 1);
        var shifted = match.Groups["shift"].Success || match.Groups["shift2"].Success;

        return shifted ? Keys.Shift | key : key;
    }

    /// <summary>The standard key for what a caption says, or null.</summary>
    public static Keys? Meaning(string? caption)
    {
        var words = Words(caption);
        if (words.Length == 0) return null;

        // The first word decides where it can: "Remove item" is a removal
        // whatever else the caption says.
        foreach (var (key, meanings) in Meanings)
        {
            if (meanings.Any(m => words == m
                    || words.StartsWith(m + " ", StringComparison.Ordinal)))
                return key;
        }

        foreach (var (key, meanings) in Meanings)
        {
            if (meanings.Any(m => (" " + words + " ").Contains(
                    " " + m + " ", StringComparison.Ordinal)))
                return key;
        }

        return null;
    }

    /// <summary>
    /// The caption as words: no mnemonic, no ellipsis, no key already
    /// written on it, lower case. PhxIcons reads captions the same way.
    /// </summary>
    internal static string Words(string? caption)
    {
        var text = Written.Replace(caption ?? "", " ")
            .Replace("&", "", StringComparison.Ordinal)
            .Replace("...", "", StringComparison.Ordinal)
            .Replace("…", "", StringComparison.Ordinal)
            .ToLowerInvariant();

        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    /// <summary>
    /// Every button on the form, top to bottom and left to right, so the
    /// keys handed out to the leftovers follow the order they are read.
    /// </summary>
    private static IEnumerable<Button> ButtonsOf(Form form) =>
        Descendants(form)
            .OfType<Button>()
            .OrderBy(b => Offset(b).Y / 20)
            .ThenBy(b => Offset(b).X);

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;

            foreach (var inner in Descendants(child))
                yield return inner;
        }
    }

    private static Point Offset(Control control)
    {
        var at = control.Location;

        for (var parent = control.Parent;
             parent is not null && parent is not Form;
             parent = parent.Parent)
            at.Offset(parent.Location);

        return at;
    }

    /// <summary>
    /// Writes the key on the button, and keeps it written when the screen
    /// changes the caption later - "Deactivate" becoming "Reactivate",
    /// "Edit" becoming "View".
    /// </summary>
    private static void Keep(Button button, Keys key, bool written)
    {
        button.AccessibleDescription = Name(key);

        if (written) return;

        var label = Name(key) + " ";
        var busy = false;

        void Label()
        {
            if (busy) return;
            if (button.Text.StartsWith(label, StringComparison.Ordinal)) return;

            busy = true;

            try
            {
                button.Text = label + (KeyWritten(button.Text) is null
                    ? button.Text
                    : Written.Replace(button.Text, "").Trim());

                FitCaption(button);
            }
            finally
            {
                busy = false;
            }
        }

        Label();
        button.TextChanged += (_, _) => Label();
    }

    /// <summary>
    /// A step smaller where the key has made the caption too long for its
    /// button, rather than cutting the end of the caption off.
    /// </summary>
    private static void FitCaption(Button button)
    {
        var room = button.Width - button.Padding.Horizontal - 12;
        if (room <= 0) return;

        var font = button.Font;

        while (font.Size > 7.5f
               && TextRenderer.MeasureText(button.Text, font).Width > room)
            font = Smaller(font);

        if (!ReferenceEquals(font, button.Font)) button.Font = font;
    }

    private static readonly Dictionary<(string, float, FontStyle), Font> Fonts = new();

    private static Font Smaller(Font font)
    {
        var key = (font.FontFamily.Name, font.Size - 0.5f, font.Style);

        lock (Fonts)
        {
            if (!Fonts.TryGetValue(key, out var smaller))
            {
                smaller = new Font(font.FontFamily, key.Item2, font.Style);
                Fonts[key] = smaller;
            }

            return smaller;
        }
    }

    /// <summary>
    /// Whether a key belongs to the control that has the focus rather
    /// than to a button: F2 edits a grid cell, F4 opens a list or a date.
    /// </summary>
    public static bool BelongsToControl(Control? focused, Keys key)
    {
        for (var control = focused; control is not null; control = control.Parent)
        {
            if (key == Keys.F2 && control is DataGridView) return true;

            if (key == Keys.F4 && control is ComboBox or DateTimePicker)
                return true;
        }

        return false;
    }

    /// <summary>The keys of a screen, as F1 lists them.</summary>
    public static string Describe(IReadOnlyDictionary<Keys, Button> map)
    {
        ArgumentNullException.ThrowIfNull(map);

        var lines = map
            .OrderBy(pair => pair.Key == Keys.Escape ? 99 : 0)
            .ThenBy(pair => (pair.Key & Keys.Shift) == Keys.Shift ? 1 : 0)
            .ThenBy(pair => pair.Key & ~Keys.Shift)
            .Select(pair => string.Format(CultureInfo.CurrentCulture,
                "{0,-10}{1}", Name(pair.Key),
                Words(pair.Value.Text) is { Length: > 0 } words
                    ? char.ToUpper(words[0], CultureInfo.CurrentCulture) + words[1..]
                    : ""));

        return "F1        These keys" + Environment.NewLine
               + string.Join(Environment.NewLine, lines);
    }
}
