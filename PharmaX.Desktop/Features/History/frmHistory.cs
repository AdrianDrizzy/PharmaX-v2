using PharmaX.Application.History;
using PharmaX.Desktop.Controls;
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
        AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        RowHeadersVisible = false,
        DefaultCellStyle = { WrapMode = DataGridViewTriState.True }
    };

    private readonly Label _empty = new() { AutoSize = true, Dock = DockStyle.Top };
    private readonly ViewFactory _views;

    /// What is on the grid, one line a row; and the subject and day it is for.
    private IReadOnlyList<HistoryLine> _lines = Array.Empty<HistoryLine>();
    private HistorySubject _shownSubject;
    private DateOnly _shownDay;

    protected override string? RequiredPermission => PermissionCode.HistoryView;

    public frmHistory(IDayHistory history, ViewFactory views)
    {
        _history = history;
        _views = views;
        Icon = PhxIcons.AppIcon;
        Text = WindowTitles.Brand("Day history");
        Name = "frmHistory";
        Width = 980;
        Height = 560;

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

        // The chosen document in its own window, where it is changed,
        // cancelled or deleted as that window allows; or all of it here.
        var open = new Button { Text = "&Open", AutoSize = true, Margin = new Padding(24, 3, 3, 3) };
        open.Click += async (_, _) => await OpenAsync(detailsIfNone: false);
        bar.Controls.Add(open);
        var details = new Button { Text = "D&etails", AutoSize = true };
        details.Click += (_, _) => ShowDetails();
        bar.Controls.Add(details);
        _grid.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex >= 0) await OpenAsync(detailsIfNone: true);
        };

        _grid.Columns.Add("When", "Time");
        _grid.Columns.Add("Reference", "Reference");
        _grid.Columns.Add("Detail", "Detail");
        _grid.Columns.Add("Amount", "Amount");
        // Header sort reorders rows away from _lines, so Open and Details hit the wrong record.
        foreach (DataGridViewColumn column in _grid.Columns)
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
        _grid.Columns["When"]!.FillWeight = 90;
        _grid.Columns["When"]!.MinimumWidth = 150;
        _grid.Columns["Reference"]!.FillWeight = 70;
        _grid.Columns["Reference"]!.MinimumWidth = 110;
        _grid.Columns["Detail"]!.FillWeight = 220;
        _grid.Columns["Amount"]!.FillWeight = 50;
        _grid.Columns["Amount"]!.MinimumWidth = 80;
        _grid.Columns["Amount"]!.DefaultCellStyle.Alignment =
            DataGridViewContentAlignment.MiddleRight;
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
        var day = DateOnly.FromDateTime(_day.Value);
        var result = await _history.ForAsync(subject, day);
        _grid.Rows.Clear();
        _lines = Array.Empty<HistoryLine>();

        if (result.IsFailure)
        {
            _empty.Text = result.Error.Message;
            return;
        }

        _lines = result.Value;
        _shownSubject = subject;
        _shownDay = day;

        foreach (var line in result.Value)
        {
            var index = _grid.Rows.Add(line.When, line.Reference, line.Detail, line.Amount);

            if (!line.IsItem) continue;

            _grid.Rows[index].DefaultCellStyle.BackColor = Color.FromArgb(236, 244, 241);
            _grid.Rows[index].DefaultCellStyle.ForeColor = Color.FromArgb(35, 70, 64);
        }

        var documents = result.Value.Count(line => !line.IsItem);
        _empty.Text = documents == 0
            ? "Nothing on that day."
            : $"{documents} on {_day.Value:dd MMM yyyy}.";
    }

    /// The document the chosen row is, or is a line of.
    private int? SelectedDocument()
    {
        var row = _grid.CurrentRow?.Index ?? -1;
        if (row < 0 || row >= _lines.Count) return null;

        while (row > 0 && _lines[row].IsItem) row--;
        return _lines[row].IsItem ? null : row;
    }

    private async Task OpenAsync(bool detailsIfNone)
    {
        if (SelectedDocument() is not int at)
        {
            ShowInfo("Choose a row first.");
            return;
        }

        var doc = _lines[at];

        try
        {
            var opened = OpenIn(doc);

            if (opened) await LoadAsync();
            else if (detailsIfNone) ShowDetails();
            else ShowInfo("This record has no window of its own. Details shows all of it.");
        }
        catch (Application.Security.PermissionDeniedException ex)
        {
            ShowError(ex.Message);
        }
    }

    /// Opens the window that keeps this record. False where there is none.
    private bool OpenIn(HistoryLine doc)
    {
        switch (_shownSubject)
        {
            case HistorySubject.Dispensed or HistorySubject.Collected
                when doc.OwnerId is Guid patient:
                ShowWindow<Dispensing.frmScriptList>(f => f.LoadForPatient(patient, select: doc.Id));
                return true;

            case HistorySubject.Patients when doc.Id is Guid patientId:
                ShowWindow<Patients.frmPatientEdit>(f => f.LoadPatient(patientId));
                return true;

            case HistorySubject.Products when doc.Id is Guid productId:
                ShowWindow<Inventory.frmProductEdit>(f => f.LoadProduct(productId));
                return true;

            case HistorySubject.Orders when doc.OwnerId is Guid supplier:
                ShowWindow<Suppliers.frmPurchaseOrderList>(f => f.LoadForSupplier(supplier));
                return true;

            case HistorySubject.Sales when doc.Reference.Length > 0:
                ShowWindow<Sales.frmReceipts>(f => f.FindReceipt(_shownDay, doc.Reference));
                return true;

            case HistorySubject.Accounts when doc.OwnerId is not null && doc.Reference.Length > 0:
                ShowWindow<Patients.frmAccounts>(f => f.FindAccount(doc.Reference));
                return true;

            default:
                return false;
        }
    }

    private void ShowWindow<TForm>(Action<TForm> setUp) where TForm : Form
    {
        using var form = _views.Create<TForm>();
        setUp(form);
        form.ShowDialog(this);
    }

    /// The chosen document and every line under it, in one window.
    private void ShowDetails()
    {
        if (SelectedDocument() is not int at)
        {
            ShowInfo("Choose a row first.");
            return;
        }

        var body = new System.Text.StringBuilder();

        for (var i = at; i < _lines.Count && (i == at || _lines[i].IsItem); i++)
        {
            var line = _lines[i];
            var parts = new[] { line.When, line.Reference, line.Detail, line.Amount }
                .Where(p => !string.IsNullOrWhiteSpace(p));

            body.Append(line.IsItem ? "    " : "").AppendLine(string.Join("   ", parts));
        }

        frmTextViewer.Show(this, $"{_subject.Items[(int)_shownSubject]} - {_lines[at].Reference}", body.ToString());
    }
}
