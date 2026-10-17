namespace PharmaX.Desktop.Controls;

/// <summary>
/// A grid typed into line by line. With TabsThroughCells on, Tab and
/// Shift+Tab go from one cell that can be typed into to the next - over
/// the read-only ones, such as a product's name filled in beside its code
/// - along the line and on to the next, and out to the next field on the
/// screen after the last one.
/// </summary>
/// <remarks>
/// A plain grid stopped on every cell, read-only ones included, so an
/// operator tabbing along a line landed on the product name and typed
/// into nothing. A list grid (PhxGrid) leaves on the first Tab, which is
/// right for a list and wrong for lines being written.
/// </remarks>
public class CellTabGrid : DataGridView
{
    private bool _tabsThroughCells;

    /// <summary>Tab moves between the cells that take typing.</summary>
    public bool TabsThroughCells
    {
        get => _tabsThroughCells;
        set
        {
            _tabsThroughCells = value;
            if (value) StandardTab = false;
        }
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (_tabsThroughCells && (keyData & Keys.KeyCode) == Keys.Tab
            && (keyData & (Keys.Control | Keys.Alt)) == 0)
            return Tab((keyData & Keys.Shift) == 0);

        return base.ProcessDialogKey(keyData);
    }

    protected override bool ProcessDataGridViewKey(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (_tabsThroughCells && e.KeyCode == Keys.Tab && !e.Control && !e.Alt)
            return Tab(!e.Shift);

        return base.ProcessDataGridViewKey(e);
    }

    private bool Tab(bool forward)
    {
        var next = CurrentCell is DataGridViewCell from
            ? NextTypingCell(this, from.RowIndex, from.ColumnIndex, forward)
            : null;

        if (next is null)
        {
            // Past the last cell: what was typed is kept and the next
            // field on the screen takes over.
            EndEdit();
            return FindForm()?.SelectNextControl(this, forward,
                       tabStopOnly: true, nested: true, wrap: true) ?? false;
        }

        try
        {
            CurrentCell = next;
        }
        catch (InvalidOperationException)
        {
            // The cell left would not let go - its value was refused - and
            // the operator stays on it to put it right.
            return true;
        }

        if (next is not DataGridViewCheckBoxCell) BeginEdit(true);
        return true;
    }

    /// <summary>
    /// The next cell along that can be typed into, in the order the
    /// columns are shown, or null past the last.
    /// </summary>
    public static DataGridViewCell? NextTypingCell(DataGridView grid,
        int rowIndex, int columnIndex, bool forward)
    {
        ArgumentNullException.ThrowIfNull(grid);

        var columns = grid.Columns.Cast<DataGridViewColumn>()
            .Where(c => c.Visible)
            .OrderBy(c => c.DisplayIndex)
            .ToList();

        var at = columns.FindIndex(c => c.Index == columnIndex);
        var row = rowIndex;

        while (true)
        {
            at += forward ? 1 : -1;

            if (at >= columns.Count) { at = 0; row++; }
            else if (at < 0) { at = columns.Count - 1; row--; }

            if (row < 0 || row >= grid.Rows.Count) return null;
            if (!grid.Rows[row].Visible) continue;

            var cell = grid.Rows[row].Cells[columns[at].Index];
            if (!cell.ReadOnly) return cell;
        }
    }
}
