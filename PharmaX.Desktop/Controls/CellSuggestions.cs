namespace PharmaX.Desktop.Controls;

/// <summary>
/// Suggestions under a grid's cell while it is typed into: a product code
/// cell that also takes the product's name, a few words of it at a time.
/// </summary>
/// <remarks>
/// <para>
/// A grid edits every text cell in the one text box, handed from cell to
/// cell, so the suggestions go on that box once and answer only in the
/// column they were given. Nothing is offered for what a cell already held
/// when the edit began, only for what is typed into it, and nothing for
/// digits alone: a barcode scanned into the cell is left to finish.
/// </para>
/// <para>
/// Choosing a match writes its value - the product's code - into the cell
/// and ends the edit, so the screen looks it up exactly as it would a code
/// typed in full.
/// </para>
/// </remarks>
public static class CellSuggestions
{
    public static void Attach(DataGridView grid, string column,
        Func<string, CancellationToken, Task<IReadOnlyList<Suggestion>>> source)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(source);

        TextBox? editor = null;
        SearchSuggestions? suggestions = null;
        var typed = false;

        bool InColumn() =>
            grid.CurrentCell?.OwningColumn?.Name == column && grid.IsCurrentCellInEditMode;

        grid.EditingControlShowing += (_, e) =>
        {
            typed = false;

            if (e.Control is not TextBox box || ReferenceEquals(box, editor)) return;

            editor = box;
            suggestions = new SearchSuggestions(box, source)
            {
                Wanted = text => typed && InColumn() && Suggest.IsName(text)
            };

            box.KeyPress += (_, _) => typed = true;

            suggestions.Chosen += (_, chosen) =>
            {
                if (!InColumn() || chosen.Value is not string value) return;

                box.Text = value;
                grid.EndEdit();
            };
        };

        grid.CellEndEdit += (_, _) =>
        {
            typed = false;
            suggestions?.Hide();
        };
    }
}
