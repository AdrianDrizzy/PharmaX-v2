using FluentAssertions;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Tests.Unit.Adapters;

/// <summary>
/// A list paged on screen and exported whole: the grid shows a page, the
/// workbook gets every row.
/// </summary>
public class GridPagerTests
{
    private sealed record Row(string Code, decimal Amount);

    [Fact]
    public void The_grid_holds_a_page_and_the_export_takes_every_row()
    {
        using var form = new Form();
        var grid = new DataGridView { AllowUserToAddRows = false, AutoGenerateColumns = false };
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Code", HeaderText = "Code", DataPropertyName = "Code" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Amount", HeaderText = "Amount", DataPropertyName = "Amount" });
        form.Controls.Add(grid);
        form.CreateControl();

        var rows = Enumerable.Range(1, 250).Select(i => new Row($"C{i}", i)).ToList();
        var pager = GridPager.Attach(grid);
        pager.Show(rows);

        grid.Rows.Count.Should().Be(GridPager.PageSize);
        GridPager.Attach(grid).Should().BeSameAs(pager, "a grid has one pager");

        var export = GridExport.Build(grid, "Rows", "", "test.user", GridPager.Of(grid)!.All);

        export.Rows.Should().HaveCount(250);
        export.Rows[249]["Code"].Should().Be("C250");
        export.Rows[249]["Amount"].Should().Be(250m);
        export.Provenance.DataCurrency.Should().Be("Every row listed, not only the page on screen");
    }

    [Fact]
    public void A_grid_filled_a_row_at_a_time_pages_and_exports_the_same_way()
    {
        using var form = new Form();
        var grid = new DataGridView { AllowUserToAddRows = false };
        grid.Columns.Add("Code", "Code");
        grid.Columns.Add("Amount", "Amount");
        form.Controls.Add(grid);
        form.CreateControl();

        var rows = Enumerable.Range(1, 130).Select(i => new object?[] { $"C{i}", (decimal)i }).ToList();
        GridPager.Attach(grid).ShowRows(rows);

        grid.Rows.Count.Should().Be(GridPager.PageSize);

        var export = GridExport.Build(grid, "Rows", "", "test.user", GridPager.Of(grid)!.All);
        export.Rows.Should().HaveCount(130);
        export.Rows[129]["Amount"].Should().Be(130m);
    }
}
