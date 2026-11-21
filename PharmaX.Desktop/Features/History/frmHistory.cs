using PharmaX.Application.History;
using PharmaX.Desktop.Mvp;
using PharmaX.Domain.Security;

namespace PharmaX.Desktop.Features.History;

/// <summary>
/// One day at a time: what was received, ordered, collected, dispensed,
/// opened, returned, changed, sold, or posted to an account.
/// </summary>
public sealed class frmHistory : PhxFormBase
{
    private readonly IDayHistory _history;
    private readonly DateTimePicker _day = new() { Format = DateTimePickerFormat.Short };
    private readonly ComboBox _subject = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        RowHeadersVisible = false
    };

    private readonly Label _empty = new() { AutoSize = true, Dock = DockStyle.Top };

    protected override string? RequiredPermission => PermissionCode.HistoryView;

    public frmHistory(IDayHistory history)
    {
        _history = history;
        Icon = PhxIcons.AppIcon;
        Text = WindowTitles.Brand("Day history");
        Name = "frmHistory";
        Width = 760;
        Height = 520;

        _subject.Items.AddRange(new object[]
        {
            "Goods received", "Purchase orders", "Scripts collected", "Dispensed",
            "Patient files opened", "Returns", "Products changed", "Sales",
            "Account movements"
        });
        _subject.SelectedIndex = 0;
        _day.Value = DateTime.Today;

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(12, 12, 12, 8),
            WrapContents = false
        };
        bar.Controls.Add(new Label { Text = "&Date", AutoSize = true, Padding = new Padding(0, 6, 8, 0) });
        bar.Controls.Add(_day);
        bar.Controls.Add(new Label { Text = "&Show", AutoSize = true, Padding = new Padding(16, 6, 8, 0) });
        _subject.Width = 180;
        bar.Controls.Add(_subject);
        var show = new Button { Text = "Show", AutoSize = true };
        PhxTheme.MakePrimary(show);
        show.Click += async (_, _) => await LoadAsync();
        bar.Controls.Add(show);

        _grid.Columns.Add("Reference", "Reference");
        _grid.Columns.Add("Detail", "Detail");
        _empty.Padding = new Padding(12, 4, 12, 4);

        Controls.Add(_grid);
        Controls.Add(_empty);
        Controls.Add(bar);
        PhxTheme.Apply(this);
        Shown += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var subject = (HistorySubject)_subject.SelectedIndex;
        var result = await _history.ForAsync(subject, DateOnly.FromDateTime(_day.Value));
        _grid.Rows.Clear();

        if (result.IsFailure)
        {
            _empty.Text = result.Error.Message;
            return;
        }

        foreach (var line in result.Value)
            _grid.Rows.Add(line.Reference, line.Detail);

        _empty.Text = result.Value.Count == 0
            ? "Nothing on that day."
            : $"{result.Value.Count} on {_day.Value:dd MMM yyyy}.";
    }
}
