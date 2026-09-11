namespace PharmaX.Desktop.Controls;

/// <summary>
/// A DataGridView with the settings every list screen in PharmaX wants,
/// applied once. Set in the constructor rather than written into forty
/// designer files, where they drift apart within a month.
/// </summary>
public sealed class PhxGrid : DataGridView
{
    public PhxGrid()
    {
        ReadOnly = true;
        AllowUserToAddRows = false;
        AllowUserToDeleteRows = false;
        AllowUserToResizeRows = false;
        SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        MultiSelect = false;
        RowHeadersVisible = false;
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        StandardTab = true;
        // 26 pixels: Section 18.1 asks for 24, which fits a full basket
        // on a 1024 by 768 till screen. Two more makes the rows legible
        // on the 1080p screens actually being bought, and a full basket
        // still fits.
        RowTemplate.Height = 26;

        // The rest of the look comes from the theme, so this grid and
        // the editable plain ones cannot drift apart.
        Mvp.PhxTheme.ApplyToGrid(this);
    }

    /// <summary>
    /// Adds a column, right-aligning and formatting it in one call so a
    /// screen cannot define a money column that is left-aligned.
    /// </summary>
    public DataGridViewTextBoxColumn AddColumn(string name, string header,
        int fillWeight = 100, string? format = null, bool rightAlign = false)
    {
        var column = new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            FillWeight = fillWeight,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };

        if (format is not null)
            column.DefaultCellStyle.Format = format;

        if (rightAlign)
            column.DefaultCellStyle.Alignment =
                DataGridViewContentAlignment.MiddleRight;

        Columns.Add(column);
        return column;
    }

    /// <summary>
    /// The selected row's value in one column, or null. Saves every
    /// screen writing the same three null checks.
    /// </summary>
    public T? SelectedValue<T>(string columnName) where T : struct
    {
        if (CurrentRow is null) return null;

        var value = CurrentRow.Cells[columnName].Value;
        return value is T typed ? typed : null;
    }
}
