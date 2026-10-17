using PharmaX.Application.Abstractions;

namespace PharmaX.Desktop.Controls;

/// <summary>
/// A drop-down list that can be typed into: every word typed narrows the
/// list to the entries that have it, anywhere in the name, and the list
/// opens to show them.
/// </summary>
/// <remarks>
/// <para>
/// For the lists a product or a patient is filed against - department,
/// category, manufacturer, medical aid. They were fixed lists to scroll,
/// and Windows' own completion only matches the start of an entry, so
/// "pain" never found "Analgesics and pain". "pain" or "anal pa" does now.
/// </para>
/// <para>
/// The screen reads the choice as it always did, from SelectedItem. What
/// is typed and not picked is settled before anything reads it - when the
/// list loses the keyboard, or a button or its function key is pressed,
/// which validates first: the one entry it matches, or else the entry
/// chosen before the typing began. A half-typed word never becomes "no
/// department" on a save.
/// </para>
/// </remarks>
public static class WordFilter
{
    public static void Attach(ComboBox combo)
    {
        ArgumentNullException.ThrowIfNull(combo);

        // A list bound to a data source is the screen's to manage.
        if (combo.DataSource is not null) return;

        // Typed into, with Windows' own start-of-entry completion left off
        // (its default): the words are matched here instead.
        combo.DropDownStyle = ComboBoxStyle.DropDown;

        List<object>? all = null;       // the whole list while it is narrowed
        object? committed = combo.SelectedItem;
        var busy = false;

        List<object> Matching(string text) =>
            (all ?? combo.Items.Cast<object>().ToList())
                .Where(item => SearchTerms.Matches(text, item.ToString()))
                .ToList();

        void Fill(IEnumerable<object> items)
        {
            combo.BeginUpdate();

            try
            {
                combo.Items.Clear();
                combo.Items.AddRange(items.ToArray());
            }
            finally
            {
                combo.EndUpdate();
            }
        }

        combo.SelectedIndexChanged += (_, _) =>
        {
            if (!busy && combo.SelectedItem is not null) committed = combo.SelectedItem;
        };

        combo.TextUpdate += (_, _) =>
        {
            var text = combo.Text;
            all ??= combo.Items.Cast<object>().ToList();
            var matches = Matching(text);

            busy = true;

            try
            {
                Fill(matches);
                combo.DroppedDown = matches.Count > 0 && text.Trim().Length > 0;

                // Opening the list selects what was typed, and the next key
                // would replace it.
                combo.Text = text;
                combo.SelectionStart = text.Length;
                combo.SelectionLength = 0;
                Cursor.Current = Cursors.Default;
            }
            finally
            {
                busy = false;
            }
        };

        void Settle()
        {
            if (all is null) return;

            var text = combo.Text.Trim();
            var picked = combo.SelectedItem;

            if (picked is null)
            {
                var matches = Matching(text);

                picked = all.FirstOrDefault(item => string.Equals(
                             item.ToString(), text, StringComparison.OrdinalIgnoreCase))
                         ?? (matches.Count == 1 ? matches[0] : committed);
            }

            busy = true;

            try
            {
                combo.DroppedDown = false;
                Fill(all);
                all = null;
                combo.SelectedItem = picked;
            }
            finally
            {
                busy = false;
            }

            committed = combo.SelectedItem;
        }

        combo.Validating += (_, _) => Settle();

        // Taken from the open list, by click or by Enter.
        combo.SelectionChangeCommitted += (_, _) =>
        {
            if (combo.IsHandleCreated) combo.BeginInvoke(Settle);
        };
    }
}
