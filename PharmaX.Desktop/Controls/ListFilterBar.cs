using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using PharmaX.Application.Abstractions;

namespace PharmaX.Desktop.Controls;

/// <summary>
/// The strip under a maintenance list's search box: Sort always on show,
/// and Filter opening a drawer for when a record was added or last changed.
/// </summary>
/// <remarks>
/// The list is sorted and filtered in its query rather than on the screen,
/// so every page, the count and an export agree. The Sort is remembered for
/// each list on this computer; the dates go back to All time each time the
/// list opens.
/// </remarks>
public sealed class ListFilterBar : TableLayoutPanel
{
    private static readonly (ListSort Sort, string Caption)[] Sorts =
    [
        (ListSort.NameAToZ, "Name A→Z"),
        (ListSort.NameZToA, "Name Z→A"),
        (ListSort.NewestAdded, "Newest added"),
        (ListSort.OldestAdded, "Oldest added"),
        (ListSort.RecentlyModified, "Recently modified"),
        (ListSort.PriceHighest, "Price, highest first"),
        (ListSort.PriceLowest, "Price, lowest first")
    ];

    /// The ranges on offer, by index; the last is the From and To pickers.
    internal static readonly string[] Ranges =
        ["All time", "Today", "Last 7 days", "Last 30 days", "This month", "From - To"];

    private const int Custom = 5;

    private readonly ComboBox _sort = Pick("Sort");
    private readonly Button _filter = new() { AutoSize = true, Text = "Filter ▾" };
    private readonly Label _summary = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly FlowLayoutPanel _drawer = Row();
    private readonly ComboBox _field = Pick("Date field");
    private readonly ComboBox _range = Pick("Date range");
    private readonly DateTimePicker _from = Date("From");
    private readonly DateTimePicker _to = Date("To");
    private readonly Button _clear = new() { AutoSize = true, Text = "Clear" };

    /// Set while the bar is being changed in code, so that is not taken
    /// as the operator choosing something.
    private bool _quiet;

    private IServiceScopeFactory? _scopes;
    private string? _key;

    /// Raised when the Sort or the date range changes.
    public event EventHandler? Changed;

    /// <param name="prices">Offer the price sorts: the product list only.</param>
    public ListFilterBar(string name, bool prices = false)
    {
        Name = name;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Dock = DockStyle.Top;
        ColumnCount = 1;
        Padding = new Padding(LogicalToDeviceUnits(8), 0, 0, LogicalToDeviceUnits(4));

        // Sized for the display, as the pager is, rather than in pixels at 100 per cent.
        _sort.Width = _field.Width = _range.Width = LogicalToDeviceUnits(150);
        _from.Width = _to.Width = LogicalToDeviceUnits(110);

        _sort.Items.AddRange(Sorts
            .Where(s => prices || s.Sort is not (ListSort.PriceHighest or ListSort.PriceLowest))
            .Select(s => (object)s.Caption).ToArray());
        _sort.SelectedIndex = 0;

        _field.Items.AddRange(["Date added", "Date modified"]);
        _field.SelectedIndex = 0;
        _range.Items.AddRange(Ranges);
        _range.SelectedIndex = 0;

        var row = Row();
        row.Controls.AddRange([Caption("Sort"), _sort, _filter, _summary]);
        _drawer.Controls.AddRange([_field, _range, Caption("From"), _from, Caption("To"), _to, _clear]);
        _drawer.Visible = false;
        Controls.Add(row);
        Controls.Add(_drawer);

        _filter.Click += (_, _) =>
        {
            _drawer.Visible = !_drawer.Visible;
            _filter.Text = _drawer.Visible ? "Filter ▴" : "Filter ▾";
        };

        _sort.SelectedIndexChanged += (_, _) =>
        {
            if (_quiet) return;
            SaveSort();
            Raise();
        };

        _field.SelectedIndexChanged += (_, _) => Raise();
        _range.SelectedIndexChanged += (_, _) => Raise();
        _from.ValueChanged += (_, _) => { if (_range.SelectedIndex == Custom) Raise(); };
        _to.ValueChanged += (_, _) => { if (_range.SelectedIndex == Custom) Raise(); };

        _clear.Click += (_, _) =>
        {
            Quietly(() => { _field.SelectedIndex = 0; _range.SelectedIndex = 0; });
            Raise();
        };

        ShowRange();
    }

    /// <summary>
    /// Docked as its own strip directly under <paramref name="top"/>, the
    /// panel holding the search box, so that row is not crowded.
    /// </summary>
    public static ListFilterBar Under(Control top, bool prices = false)
    {
        ArgumentNullException.ThrowIfNull(top);

        var bar = new ListFilterBar(top.Name + "Filter", prices);
        var parent = top.Parent!;

        // Docked in reverse order: one place lower than the panel docks
        // straight after it, so beneath it.
        parent.Controls.Add(bar);
        parent.Controls.SetChildIndex(bar, parent.Controls.GetChildIndex(top));

        return bar;
    }

    /// The Sort and date range as chosen, the dates as UTC for the query.
    public ListFilter Filter
    {
        get
        {
            var (from, to) = Range(_range.SelectedIndex, DateTime.Today,
                _from.Value.Date, _to.Value.Date);

            return new ListFilter(Sort, _field.SelectedIndex == 1,
                from?.ToUniversalTime(), to?.ToUniversalTime());
        }
    }

    private ListSort Sort =>
        Sorts.First(s => s.Caption == (string?)_sort.SelectedItem).Sort;

    /// <summary>
    /// The range as local midnights, the end exclusive, so "Today" is the
    /// whole of today and "Last 7 days" is today and the six before it.
    /// </summary>
    internal static (DateTime? From, DateTime? To) Range(int range,
        DateTime today, DateTime from, DateTime to)
    {
        var tomorrow = today.AddDays(1);

        return range switch
        {
            1 => (today, tomorrow),
            2 => (today.AddDays(-6), tomorrow),
            3 => (today.AddDays(-29), tomorrow),
            4 => (new DateTime(today.Year, today.Month, 1, 0, 0, 0, today.Kind), tomorrow),

            // Either way round: a From after the To still means between them.
            Custom => from <= to ? (from, to.AddDays(1)) : (to, from.AddDays(1)),
            _ => (null, null)
        };
    }

    /// <summary>
    /// Remembers the Sort as <paramref name="list"/>'s on this computer, and
    /// puts back the one chosen there last time. Call before the first load.
    /// </summary>
    /// <remarks>
    /// Read and written in a scope of its own: the list's own context may be
    /// part way through a search when the Sort is changed.
    /// </remarks>
    public async Task RememberSortAsync(IServiceScopeFactory scopes, string list)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        _scopes = scopes;
        _key = "list.sort." + list;

        try
        {
            using var scope = scopes.CreateScope();
            var saved = await scope.ServiceProvider
                .GetRequiredService<IWorkstationSettings>().GetAsync(_key);

            var caption = Sorts.FirstOrDefault(s => s.Sort.ToString() == saved).Caption;
            if (caption is not null && _sort.Items.Contains(caption))
                Quietly(() => _sort.SelectedItem = caption);
        }
        catch (Exception)
        {
            // A Sort not remembered is not worth keeping the list from opening.
        }
    }

    private async void SaveSort()
    {
        if (_scopes is null || _key is null) return;

        try
        {
            using var scope = _scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IWorkstationSettings>()
                .SetAsync(_key, Sort.ToString());
        }
        catch (Exception)
        {
            // As above: the list is sorted either way; only the memory of it is lost.
        }
    }

    private void Raise()
    {
        if (_quiet) return;

        ShowRange();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// The pickers are only for From - To, and the strip says what is
    /// filtered while the drawer is closed.
    private void ShowRange()
    {
        _from.Enabled = _to.Enabled = _range.SelectedIndex == Custom;

        if (_range.SelectedIndex == 0)
        {
            _summary.Text = "";
            return;
        }

        var range = _range.SelectedIndex == Custom
            ? string.Format(CultureInfo.CurrentCulture, "{0:dd/MM/yyyy} to {1:dd/MM/yyyy}",
                _from.Value, _to.Value)
            : (string)_range.SelectedItem!;

        _summary.Text = (_field.SelectedIndex == 1 ? "Modified: " : "Added: ") + range;
    }

    private void Quietly(Action change)
    {
        _quiet = true;

        try
        {
            change();
        }
        finally
        {
            _quiet = false;
        }

        ShowRange();
    }

    private static FlowLayoutPanel Row() => new()
    {
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        WrapContents = false,
        Margin = Padding.Empty
    };

    private static Label Caption(string text) =>
        new() { AutoSize = true, Text = text, Anchor = AnchorStyles.Left };

    private static ComboBox Pick(string name) => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        AccessibleName = name
    };

    private static DateTimePicker Date(string name) => new()
    {
        Format = DateTimePickerFormat.Custom,
        CustomFormat = "dd/MM/yyyy",
        AccessibleName = name
    };
}
