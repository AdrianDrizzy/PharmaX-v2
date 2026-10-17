using FluentAssertions;
using PharmaX.Desktop.Controls;

namespace PharmaX.Tests.Unit.Adapters;

/// <summary>
/// Tab along lines being written: from one cell that takes typing to the
/// next, over the read-only ones, and out after the last.
/// </summary>
public class CellTabGridTests
{
    /// Code, Product (read-only), Qty, Total (read-only), Directions.
    private static CellTabGrid Lines(int rows)
    {
        var grid = new CellTabGrid { AllowUserToAddRows = false };

        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Code" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Product", ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Qty" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Total", ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Directions" });

        for (var i = 0; i < rows; i++) grid.Rows.Add();
        return grid;
    }

    private static string At(DataGridViewCell? cell) =>
        cell is null ? "out" : $"{cell.RowIndex}:{cell.OwningColumn.Name}";

    [Fact]
    public void Tab_goes_over_the_read_only_cells_and_on_to_the_next_line()
    {
        using var grid = Lines(2);

        At(CellTabGrid.NextTypingCell(grid, 0, 0, forward: true)).Should().Be("0:Qty");
        At(CellTabGrid.NextTypingCell(grid, 0, 2, forward: true)).Should().Be("0:Directions");
        At(CellTabGrid.NextTypingCell(grid, 0, 4, forward: true)).Should().Be("1:Code");
        At(CellTabGrid.NextTypingCell(grid, 1, 4, forward: true)).Should().Be("out",
            "after the last cell the next field on the screen takes over");
    }

    [Fact]
    public void Shift_Tab_goes_back_the_same_way()
    {
        using var grid = Lines(2);

        At(CellTabGrid.NextTypingCell(grid, 1, 0, forward: false)).Should().Be("0:Directions");
        At(CellTabGrid.NextTypingCell(grid, 0, 4, forward: false)).Should().Be("0:Qty");
        At(CellTabGrid.NextTypingCell(grid, 0, 0, forward: false)).Should().Be("out");
    }

    [Fact]
    public void The_order_is_the_order_the_columns_are_shown_in()
    {
        using var grid = Lines(1);
        grid.Columns["Directions"]!.DisplayIndex = 1;

        At(CellTabGrid.NextTypingCell(grid, 0, 0, forward: true)).Should().Be("0:Directions");
    }

    [Fact]
    public void Only_a_grid_that_asks_for_it_tabs_through_cells()
    {
        using var lines = new CellTabGrid { TabsThroughCells = true };
        lines.StandardTab.Should().BeFalse("Tab is the grid's own while it moves along a line");

        using var list = new PhxGrid();
        list.TabsThroughCells.Should().BeFalse();
        list.StandardTab.Should().BeTrue("a list still leaves on the first Tab");
    }
}
