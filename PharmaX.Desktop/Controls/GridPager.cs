using System.Runtime.CompilerServices;

namespace PharmaX.Desktop.Controls;

/// <summary>
/// Pages a list on a grid: the grid holds one page, the pager holds the
/// lot. A list of six thousand rows opens at once, and an export still
/// takes all six thousand rather than the page on screen.
/// </summary>
/// <remarks>
/// Attached to a grid a screen has already laid out, in the grid's own
/// place in that layout, so no designer file had to change to get one.
/// The bar itself is PagerBar, the one the product and patient lists use.
/// </remarks>
public sealed class GridPager : PagerBar
{
    public const int PageSize = 100;

    private static readonly ConditionalWeakTable<DataGridView, GridPager> Pagers = new();

    private readonly DataGridView _grid;

    private IReadOnlyList<object> _all = Array.Empty<object>();
    private Action<int, int> _bind = (_, _) => { };
    private int _page;

    private GridPager(DataGridView grid) : base(grid.Name + "Pager")
    {
        _grid = grid;

        PageRequested += (_, move) =>
        {
            var page = Target(_page, _all.Count, PageSize, move);
            if (page == _page) return;

            _page = page;
            Bind();
        };

        // From the grid rather than the screen: a screen with two paged
        // lists turns the one being read.
        grid.KeyDown += (_, e) =>
        {
            if (!Press(e.KeyData)) return;

            e.Handled = true;
            e.SuppressKeyPress = true;
        };

        Bind();
    }

    /// <summary>
    /// Puts a pager under the grid, taking the grid's place in its layout.
    /// The same pager again where the grid already has one.
    /// </summary>
    public static GridPager Attach(DataGridView grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        if (Pagers.TryGetValue(grid, out var existing)) return existing;

        var parent = grid.Parent
                     ?? throw new InvalidOperationException($"{grid.Name} is not on a form yet.");
        var pager = new GridPager(grid);

        var host = new Panel
        {
            Anchor = grid.Anchor,
            Bounds = grid.Bounds,
            Dock = grid.Dock,
            Margin = grid.Margin,
            Name = grid.Name + "Host",
            TabIndex = grid.TabIndex
        };

        parent.SuspendLayout();

        if (parent is TableLayoutPanel table)
        {
            var cell = table.GetCellPosition(grid);
            var columns = table.GetColumnSpan(grid);
            var rows = table.GetRowSpan(grid);

            table.Controls.Remove(grid);
            table.Controls.Add(host, cell.Column, cell.Row);
            table.SetColumnSpan(host, columns);
            table.SetRowSpan(host, rows);
        }
        else
        {
            var index = parent.Controls.GetChildIndex(grid);

            parent.Controls.Remove(grid);
            parent.Controls.Add(host);
            parent.Controls.SetChildIndex(host, index);
        }

        grid.Dock = DockStyle.Fill;

        // The grid last, so it is docked last and fills what the bar leaves.
        host.Controls.Add(grid);
        host.Controls.Add(pager);
        grid.BringToFront();

        parent.ResumeLayout(true);

        Pagers.Add(grid, pager);
        return pager;
    }

    /// The grid's pager, where it has one.
    public static GridPager? Of(DataGridView grid) =>
        grid is not null && Pagers.TryGetValue(grid, out var pager) ? pager : null;

    /// Every row of the list, not only the page on screen.
    public IReadOnlyList<object> All => _all;

    /// <summary>
    /// Shows a list a page at a time, bound to the grid as the screen bound
    /// it before. Stays on the page it was on where that page still exists,
    /// so a list refreshed after an edit does not jump back to the start.
    /// </summary>
    public void Show<T>(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var list = items as IReadOnlyList<T> ?? items.ToList();

        _all = list as IReadOnlyList<object> ?? list.Cast<object>().ToList();
        _bind = (skip, take) => _grid.DataSource = list.Skip(skip).Take(take).ToList();
        Bind();
    }

    /// <summary>
    /// The same for a grid filled a row at a time, each item the values of
    /// one row's cells in column order.
    /// </summary>
    public void ShowRows(IReadOnlyList<object?[]> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        _all = rows;
        _bind = (skip, take) =>
        {
            _grid.Rows.Clear();
            foreach (var row in rows.Skip(skip).Take(take)) _grid.Rows.Add(row);
        };
        Bind();
    }

    /// Back to the first page, for a new search.
    public void Restart() => _page = 0;

    /// The page on screen, from nought.
    public int Page => _page;

    private void Bind()
    {
        _page = Math.Clamp(_page, 0, Pages(_all.Count, PageSize) - 1);
        _bind(_page * PageSize, PageSize);

        ShowPage(_page, PageSize,
            Math.Clamp(_all.Count - _page * PageSize, 0, PageSize), _all.Count);
    }
}
