using System.Globalization;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Dispensing.Dtos;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Dispensing;

/// <summary>
/// Finds a script among every patient's: those captured on a day, today
/// to begin with, narrowed by what is typed.
/// </summary>
/// <remarks>
/// A dialog with no presenter, as the account picker is: it does one
/// thing and the screen that opened it is waiting for the answer.
/// </remarks>
public sealed class frmScriptPicker : PhxFormBase
{
    private readonly IScriptService? _scripts;

    private readonly DateTimePicker _day = new();
    private readonly TextBox _search = new();
    private readonly PhxGrid _grid = new();
    private readonly Label _count = new();
    private readonly Button _choose = new();
    private readonly Button _cancel = new();
    private readonly SearchSuggestions _suggestions;

    private readonly System.Windows.Forms.Timer _debounce =
        new() { Interval = 250 };

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.ScriptCapture;

    protected override bool MayOpen(
        Application.Security.IPermissionService permissions) =>
        Application.Security.PermissionServiceExtensions.HasAny(permissions,
            Domain.Security.PermissionCode.ScriptCapture,
            Domain.Security.PermissionCode.ScriptDispense);

    /// Designer and tests only.
    public frmScriptPicker()
    {
        BuildLayout();

        _suggestions = new SearchSuggestions(_search) { MinimumLength = 1 };
        _suggestions.Chosen += (_, chosen) =>
        {
            if (Suggest.SelectRow(_grid, item =>
                    item is ScriptRow r && Equals(r.Source, chosen.Value)))
                Accept();
        };

        _search.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };
        _day.ValueChanged += (_, _) => Operations.Enqueue(LoadAsync);
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            GridPager.Of(_grid)?.Restart();
            Operations.Enqueue(LoadAsync);
        };

        _choose.Click += (_, _) => Accept();
        _cancel.Click += (_, _) => CloseView(false);
        _grid.DoubleClick += (_, _) => Accept();

        PhxTheme.Apply(this);
    }

    /// Runtime. Resolved by the container.
    public frmScriptPicker(IScriptService scripts) : this() => _scripts = scripts;

    /// The script the operator picked, once the dialog returns OK.
    public ScriptDto? Chosen { get; private set; }

    /// <summary>
    /// Opens the picker on today's scripts, or on every day's matching
    /// <paramref name="term"/> where one was typed.
    /// </summary>
    public static ScriptDto? Pick(ViewFactory views, IWin32Window owner, string? term = null)
    {
        ArgumentNullException.ThrowIfNull(views);

        using var picker = views.Create<frmScriptPicker>();
        if (!string.IsNullOrWhiteSpace(term))
        {
            picker._day.Checked = false;
            picker._search.Text = term.Trim();
        }

        return picker.ShowDialog(owner) == DialogResult.OK ? picker.Chosen : null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _debounce.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // Queued with the day's and the search's own loads, so no two
        // share the database at once.
        Operations.Enqueue(LoadAsync);
        _search.Focus();
        _search.SelectionStart = _search.TextLength;
    }

    private async Task LoadAsync()
    {
        if (_scripts is null) return;

        DateOnly? day = _day.Checked ? DateOnly.FromDateTime(_day.Value) : null;
        var result = await _scripts.FindAsync(_search.Text.Trim(), day);

        if (result.IsFailure)
        {
            ShowError(result.Error.Message);
            return;
        }

        GridPager.Attach(_grid).Show(result.Value.Select(ScriptRow.From));

        _count.Text = string.Format(CultureInfo.CurrentCulture, "{0} script(s)",
                                    result.Value.Count);
        _suggestions.Offer(result.Value.Select(Suggest.Script));
        _choose.Enabled = result.Value.Count > 0;
        PhxTheme.MakePrimary(_choose);
    }

    private void Accept()
    {
        if (_grid.CurrentRow?.DataBoundItem is not ScriptRow row)
        {
            ShowInfo("Choose a script first.");
            return;
        }

        Chosen = row.Source;
        CloseView(true);
    }

    private void BuildLayout()
    {
        var top = new Panel { Dock = DockStyle.Top, Height = 48 };

        var dayLabel = new Label { AutoSize = true, Location = new Point(12, 17), Text = "&Day" };
        _day.Checked = true;
        _day.Format = DateTimePickerFormat.Custom;
        _day.CustomFormat = "dd/MM/yyyy";
        _day.Location = new Point(50, 13);
        _day.Name = "dtpDay";
        _day.ShowCheckBox = true;
        _day.Size = new Size(130, 23);
        _day.AccessibleDescription = "Untick for every day";

        var findLabel = new Label { AutoSize = true, Location = new Point(196, 17), Text = "&Find" };
        _search.Location = new Point(236, 13);
        _search.Name = "txtFindScript";
        _search.PlaceholderText = "Script number";
        _search.Size = new Size(220, 23);

        _count.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _count.AutoSize = false;
        _count.Location = new Point(560, 17);
        _count.Size = new Size(150, 20);
        _count.TextAlign = ContentAlignment.MiddleRight;

        top.Controls.AddRange(new Control[] { _count, _search, findLabel, _day, dayLabel });

        _grid.AutoGenerateColumns = false;
        _grid.Dock = DockStyle.Fill;
        _grid.MultiSelect = false;
        _grid.Name = "grdScripts";

        Column(nameof(ScriptRow.ScriptNumber), "Script", 80);
        Column(nameof(ScriptRow.Patient), "Patient", 140);
        Column(nameof(ScriptRow.Written), "Written", 70);
        Column(nameof(ScriptRow.Medicines), "Medicines", 220);
        Column(nameof(ScriptRow.Prescriber), "Prescriber", 120);
        Column(nameof(ScriptRow.Status), "Status", 70);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 52 };

        _choose.Location = new Point(12, 8);
        _choose.Size = new Size(130, 34);
        _choose.Text = "C&hoose";
        _choose.UseVisualStyleBackColor = true;

        _cancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _cancel.Location = new Point(590, 8);
        _cancel.Size = new Size(110, 34);
        _cancel.Text = "&Cancel";
        _cancel.UseVisualStyleBackColor = true;

        bottom.Controls.AddRange(new Control[] { _cancel, _choose });

        AcceptButton = _choose;
        CancelButton = _cancel;
        ClientSize = new Size(730, 460);
        Controls.Add(_grid);
        Controls.Add(top);
        Controls.Add(bottom);
        MinimizeBox = false;
        Name = "frmScriptPicker";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Find script";
    }

    private void Column(string property, string header, int weight)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            Name = property,
            HeaderText = header,
            FillWeight = weight,
            SortMode = DataGridViewColumnSortMode.Automatic
        });
    }

    private sealed record ScriptRow(ScriptDto Source, string ScriptNumber,
        string Patient, string Written, string Medicines, string Prescriber,
        string Status)
    {
        public static ScriptRow From(ScriptDto s) => new(
            s, s.ScriptNumber, s.Patient,
            s.PrescribedOn.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture),
            s.Items.Replace(Environment.NewLine, ", ", StringComparison.Ordinal),
            s.PrescriberName, s.Status);
    }
}
