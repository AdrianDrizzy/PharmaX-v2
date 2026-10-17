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

    [Theory]
    [InlineData(0, 250, PageMove.Next, 1)]
    [InlineData(2, 250, PageMove.Next, 2)]
    [InlineData(0, 250, PageMove.Previous, 0)]
    [InlineData(2, 250, PageMove.First, 0)]
    [InlineData(0, 250, PageMove.Last, 2)]
    [InlineData(0, 200, PageMove.Last, 1)]
    [InlineData(0, 100, PageMove.Next, 0)]
    [InlineData(0, 0, PageMove.Last, 0)]
    [InlineData(5, 120, PageMove.Previous, 1)]
    public void A_turn_lands_on_a_page_the_list_has(int page, int total,
        PageMove move, int expected) =>
        PagerBar.Target(page, total, 100, move).Should().Be(expected);

    [Fact]
    public void Every_page_is_reached_and_the_bar_says_which()
    {
        using var form = new Form();
        var grid = new DataGridView { AllowUserToAddRows = false, Name = "grdRows" };
        grid.Columns.Add("Code", "Code");
        form.Controls.Add(grid);
        form.CreateControl();

        var pager = GridPager.Attach(grid);
        pager.ShowRows(Enumerable.Range(1, 250).Select(i => new object?[] { $"C{i}" }).ToList());

        Shown(pager).Should().Be("1 to 100 of 250  -  page 1 of 3");
        pager.Press(Keys.Control | Keys.PageUp).Should().BeFalse("there is no page before the first");

        pager.Press(Keys.Control | Keys.Shift | Keys.PageDown).Should().BeTrue();
        pager.Page.Should().Be(2);
        grid.Rows.Count.Should().Be(50);
        grid.Rows[0].Cells[0].Value.Should().Be("C201");
        Shown(pager).Should().Be("201 to 250 of 250  -  page 3 of 3");
        pager.Press(Keys.Control | Keys.PageDown).Should().BeFalse("there is no page after the last");

        pager.Press(Keys.Control | Keys.PageUp).Should().BeTrue();
        Shown(pager).Should().Be("101 to 200 of 250  -  page 2 of 3");

        pager.Press(Keys.Control | Keys.Shift | Keys.PageUp).Should().BeTrue();
        pager.Page.Should().Be(0);
        grid.Rows[0].Cells[0].Value.Should().Be("C1");
    }

    [Fact]
    public void A_new_search_starts_at_the_first_page_and_a_refresh_stays_put()
    {
        using var form = new Form();
        var grid = new DataGridView { AllowUserToAddRows = false, Name = "grdRows" };
        grid.Columns.Add("Code", "Code");
        form.Controls.Add(grid);
        form.CreateControl();

        var rows = Enumerable.Range(1, 250).Select(i => new object?[] { $"C{i}" }).ToList();
        var pager = GridPager.Attach(grid);
        pager.ShowRows(rows);
        pager.Press(Keys.Control | Keys.PageDown);

        pager.ShowRows(rows);
        pager.Page.Should().Be(1, "a refresh keeps the page being read");

        pager.Restart();
        pager.ShowRows(rows.Take(15).ToList());
        pager.Page.Should().Be(0);
        Shown(pager).Should().Be("15 shown");
    }

    /// <summary>
    /// The buttons are sized by their captions, so a display at 125 or 150
    /// per cent never cuts one short.
    /// </summary>
    [Theory]
    [InlineData(900, "|< First", "< Pre&vious", "Ne&xt >", "Last >|")]
    [InlineData(260, "|<", "<", ">", ">|")]
    public void Every_pager_button_is_as_big_as_its_caption_and_on_the_bar(
        int width, params string[] captions)
    {
        using var form = new Form { ClientSize = new Size(width, 300) };
        var bar = new PagerBar("pager");
        form.Controls.Add(bar);
        form.CreateControl();
        bar.ShowPage(0, 100, 100, 250);
        form.PerformLayout();

        var buttons = bar.Controls.Find("pagerButtons", false).Single().Controls
            .OfType<Button>().ToList();

        buttons.Select(b => b.Text).Should().Equal(captions);

        foreach (var button in buttons)
        {
            var wanted = button.GetPreferredSize(Size.Empty);
            button.Width.Should().BeGreaterThanOrEqualTo(wanted.Width, button.Text);
            button.Height.Should().BeGreaterThanOrEqualTo(wanted.Height, button.Text);

            var onBar = button.Bounds;
            onBar.Offset(button.Parent!.Location);
            bar.ClientRectangle.Contains(onBar).Should().BeTrue(
                $"{button.Text} must sit wholly on the bar, not at {onBar}");
        }
    }

    private static string Shown(PagerBar pager) =>
        pager.Controls.Find(pager.Name + "Shown", false).Single().Text;

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
