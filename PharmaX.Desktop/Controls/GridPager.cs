using System.Globalization;
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
/// Ctrl+Page Up and Ctrl+Page Down turn the page from the grid, as on the
/// product and patient lists.
/// </remarks>
public sealed class GridPager : Panel
{
    public const int PageSize = 100;

    private static readonly ConditionalWeakTable<DataGridView, GridPager> Pagers = new();

    private readonly DataGridView _grid;
    private readonly Button _first = new() { Text = "|<", Width = 44 };
    private readonly Button _previous = new() { Text = "< Pre&vious", Width = 104 };
    private readonly Button _next = new() { Text = "Ne&xt >", Width = 104 };
    private readonly Button _last = new() { Text = ">|", Width = 44 };
    private readonly Label _shown = new();

    private IReadOnlyList<object> _all = Array.Empty<object>();
    private Action<int, int> _bind = (_, _) => { };
    private int _page;

    private GridPager(DataGridView grid)
    {
        _grid = grid;

        Dock = DockStyle.Bottom;
        Height = 40;
        Name = grid.Name + "Pager";
        Padding = new Padding(0, 4, 0, 4);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        foreach (var button in new[] { _first, _previous, _next, _last })
        {
            button.Height = 30;
            button.Margin = new Padding(4, 0, 0, 0);
            button.UseVisualStyleBackColor = true;
            buttons.Controls.Add(button);
        }

        _shown.AutoSize = false;
        _shown.Dock = DockStyle.Fill;
        _shown.Name = grid.Name + "Shown";
        _shown.TextAlign = ContentAlignment.MiddleLeft;

        Controls.Add(_shown);
        Controls.Add(buttons);

        _first.Click += (_, _) => Turn(-_page);
        _previous.Click += (_, _) => Turn(-1);
        _next.Click += (_, _) => Turn(1);
        _last.Click += (_, _) => Turn(LastPage - _page);

        grid.KeyDown += (_, e) =>
        {
            if (!e.Control || e.KeyCode is not (Keys.PageUp or Keys.PageDown)) return;

            Turn(e.KeyCode == Keys.PageDown ? 1 : -1);
            e.Handled = true;
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

    private int LastPage => Math.Max(0, (_all.Count - 1) / PageSize);

    private void Turn(int by)
    {
        var page = Math.Clamp(_page + by, 0, LastPage);
        if (page == _page) return;

        _page = page;
        Bind();
    }

    private void Bind()
    {
        _page = Math.Clamp(_page, 0, LastPage);
        _bind(_page * PageSize, PageSize);

        var from = _all.Count == 0 ? 0 : _page * PageSize + 1;
        var to = Math.Min(_all.Count, (_page + 1) * PageSize);

        _shown.Text = string.Format(CultureInfo.CurrentCulture,
            _all.Count <= PageSize ? "{2:N0} shown" : "{0:N0} to {1:N0} of {2:N0}  -  page {3:N0} of {4:N0}",
            from, to, _all.Count, _page + 1, LastPage + 1);

        _first.Enabled = _previous.Enabled = _page > 0;
        _next.Enabled = _last.Enabled = _page < LastPage;
    }
}
