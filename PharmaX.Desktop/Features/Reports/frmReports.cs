using System.Globalization;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Reporting;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Reports;

/// <summary>
/// One dialog, many presets. RPT-001 and RPT-002 in code: branch, date
/// range, report, slicers, F12 to preview, F7 to close.
/// </summary>
public partial class frmReports : PhxFormBase, IReportsView
{
    private readonly ReportsPresenter? _presenter;
    private readonly Dictionary<string, Control> _slicerInputs =
        new(StringComparer.Ordinal);

    /// True while the assistant, not the operator, is choosing a report.
    private bool _selectingQuietly;

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.ReportOperational;

    /// Any report permission opens the screen; it lists only those.
    protected override bool MayOpen(
        Application.Security.IPermissionService permissions) =>
        Application.Security.PermissionServiceExtensions.HasAny(permissions,
            Domain.Security.PermissionCode.ReportOperational,
            Domain.Security.PermissionCode.ReportManagement,
            Domain.Security.PermissionCode.ReportFinancial,
            Domain.Security.PermissionCode.ReportClinical);

    /// Designer only. Never called at runtime.
    public frmReports()
    {
        InitializeComponent();

        trePresets.AfterSelect += (_, _) =>
        {
            ApplyClock();
            if (!_selectingQuietly) PresetChanged?.Invoke(this, EventArgs.Empty);
        };

        btnPreview.Click += (_, _) =>
            PreviewRequested?.Invoke(this, EventArgs.Empty);
        btnExport.Click += (_, _) =>
            ExportRequested?.Invoke(this, EventArgs.Empty);
        btnExportPdf.Click += (_, _) =>
            ExportPdfRequested?.Invoke(this, EventArgs.Empty);
        chkGraph.CheckedChanged += (_, _) => ShowGraphPanel();
        splGraph.SplitterMoved += (_, _) => _graphWidened = true;
        btnEnlargeGraph.Click += (_, _) =>
            EnlargeGraphRequested?.Invoke(this, EventArgs.Empty);
        picGraph.DoubleClick += (_, _) =>
            EnlargeGraphRequested?.Invoke(this, EventArgs.Empty);
        btnClose.Click += (_, _) =>
            CloseRequested?.Invoke(this, EventArgs.Empty);

        btnAsk.Click += (_, _) =>
            AskRequested?.Invoke(this, EventArgs.Empty);
        txtAsk.TextChanged += (_, _) =>
            AskStarted?.Invoke(this, EventArgs.Empty);
        txtAsk.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;

            // No ding: Enter in a single-line box is a request here.
            e.SuppressKeyPress = true;
            AskRequested?.Invoke(this, EventArgs.Empty);
        };
    }

    /// Runtime. Resolved by the container.
    public frmReports(ReportsPresenter presenter, PharmaXOptions options)
        : this()
    {
        _presenter = presenter;
        txtBranch.Text = options.Workstation.BranchCode;
        _presenter.Attach(this);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        SeatSplitter();
        if (_presenter is not null) await _presenter.InitialiseAsync();
    }

    /// <summary>
    /// F12 previews and F7 closes. Both bindings come from the incumbent
    /// system and cost nothing to preserve (specification Section 2.4).
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.F12:
                PreviewRequested?.Invoke(this, EventArgs.Empty);
                return true;
            case Keys.F7:
                CloseRequested?.Invoke(this, EventArgs.Empty);
                return true;
            default:
                return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    public string BranchCode => txtBranch.Text.Trim();
    public DateTime FromDate => dtpFrom.Value.Date;
    public DateTime ToDate => dtpTo.Value.Date;

    public bool UsesClock =>
        string.Equals(SelectedPresetKey,
                      PharmaX.Reporting.Definitions.SalesSummaryReport.PresetKey,
                      StringComparison.Ordinal);

    public DateTime FromMoment => UsesClock ? dtpFrom.Value : dtpFrom.Value.Date;
    public DateTime ToMoment => UsesClock ? dtpTo.Value : dtpTo.Value.Date;

    /// <summary>
    /// Sales summary takes a day, a month, a year, an hour and a minute.
    /// The other reports stay as a date. Turning the clock on for a pair
    /// of midnights takes the end through 23:59, so "today" is the whole day
    /// until the time is changed.
    /// </summary>
    private void ApplyClock()
    {
        var clock = UsesClock;

        dtpFrom.Format = clock
            ? DateTimePickerFormat.Custom
            : DateTimePickerFormat.Short;
        dtpTo.Format = dtpFrom.Format;
        dtpFrom.CustomFormat = "dd/MM/yyyy HH:mm";
        dtpTo.CustomFormat = dtpFrom.CustomFormat;
        dtpFrom.ShowUpDown = clock;
        dtpTo.ShowUpDown = clock;

        if (clock
            && dtpFrom.Value.TimeOfDay == TimeSpan.Zero
            && dtpTo.Value.TimeOfDay == TimeSpan.Zero)
            dtpTo.Value = dtpTo.Value.Date.AddHours(23).AddMinutes(59);
    }

    public string? SelectedPresetKey =>
        trePresets.SelectedNode?.Tag as string;

    /// <summary>
    /// The preset to land on, set by a caller before ShowDialog.
    /// </summary>
    /// <remarks>
    /// Several menu entries are a report the staff already know by
    /// another name - Inventory's "Stock" is Stock On Hand, and
    /// "Expired Stock" is Stock Expiring. Opening this screen on the
    /// right report is what makes those entries lead somewhere useful
    /// without a second copy of a list that already exists.
    /// </remarks>
    public string? OpenOnPreset { get; set; }

    public void ShowPresets(IReadOnlyList<ReportPresetDto> presets)
    {
        ArgumentNullException.ThrowIfNull(presets);

        trePresets.BeginUpdate();
        trePresets.Nodes.Clear();

        // What each report shows, on hover: the same description the
        // assistant reads.
        trePresets.ShowNodeToolTips = true;

        foreach (var group in presets.GroupBy(p => p.Group)
                                     .OrderBy(g => g.Key))
        {
            var parent = trePresets.Nodes.Add(group.Key);

            foreach (var preset in group.OrderBy(p => p.DisplayName))
                parent.Nodes.Add(new TreeNode(preset.DisplayName)
                {
                    Tag = preset.Key,
                    ToolTipText = preset.Description
                });
        }

        trePresets.ExpandAll();
        trePresets.EndUpdate();

        SelectRequestedPreset();
    }

    /// <summary>
    /// Selects the node the caller asked to open on, if it is there.
    /// </summary>
    /// <remarks>
    /// Selecting the node raises AfterSelect, so the presenter loads
    /// that report's slicers exactly as it would had the operator
    /// clicked it. A key that matches nothing leaves the tree alone
    /// rather than throwing: a stale menu entry should still open the
    /// report screen.
    /// </remarks>
    private void SelectRequestedPreset()
    {
        if (string.IsNullOrWhiteSpace(OpenOnPreset)) return;

        if (FindPreset(OpenOnPreset) is not TreeNode node) return;

        trePresets.SelectedNode = node;
        node.EnsureVisible();
    }

    private TreeNode? FindPreset(string presetKey)
    {
        foreach (TreeNode group in trePresets.Nodes)
        {
            foreach (TreeNode node in group.Nodes)
            {
                if (node.Tag is string key
                    && string.Equals(key, presetKey, StringComparison.Ordinal))
                    return node;
            }
        }

        return null;
    }

    public string AskText => txtAsk.Text;

    public void ShowAsk(bool available) => pnlAsk.Visible = available;

    public void ShowAskResult(string message) => lblAskResult.Text = message;

    public void SelectPreset(string presetKey)
    {
        if (FindPreset(presetKey) is not TreeNode node) return;

        _selectingQuietly = true;

        try
        {
            trePresets.SelectedNode = node;
        }
        finally
        {
            _selectingQuietly = false;
        }

        node.EnsureVisible();
    }

    /// <summary>
    /// The pickers set to whole days. For Sales Summary, which runs by
    /// the minute, the last day runs to 23:59.
    /// </summary>
    public void SetPeriod(DateOnly from, DateOnly to)
    {
        dtpFrom.Value = from.ToDateTime(TimeOnly.MinValue);
        dtpTo.Value = UsesClock
            ? to.ToDateTime(new TimeOnly(23, 59))
            : to.ToDateTime(TimeOnly.MinValue);
    }

    public void ApplySlicerValues(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        foreach (var (key, value) in values)
        {
            if (!_slicerInputs.TryGetValue(key, out var input)) continue;

            switch (input)
            {
                case CheckBox check:
                    check.Checked = value == "true";
                    break;

                case ComboBox combo:
                    var choice = combo.Items.OfType<Choice>().FirstOrDefault(c =>
                        string.Equals(c.Value, value, StringComparison.OrdinalIgnoreCase));
                    if (choice is not null) combo.SelectedItem = choice;
                    break;

                default:
                    input.Text = value;
                    break;
            }
        }
    }

    /// <summary>The margin every slicer row starts at.</summary>
    private const int SlicerMargin = 8;

    /// <summary>One caption and its input, plus the gap to the next.</summary>
    private const int SlicerRowHeight = 30;

    /// <summary>
    /// Past this the panel scrolls rather than eating the grid. Four rows
    /// and the margins, which is more slicers than any preset declares.
    /// </summary>
    private const int SlicerPanelMaximum = 140;

    public void ShowSlicers(IReadOnlyList<SlicerDto> slicers)
    {
        ArgumentNullException.ThrowIfNull(slicers);

        pnlSlicers.SuspendLayout();
        pnlSlicers.Controls.Clear();
        _slicerInputs.Clear();

        var gutter = InputColumn(slicers);
        var y = SlicerMargin;

        foreach (var slicer in slicers)
        {
            pnlSlicers.Controls.Add(new Label
            {
                AutoSize = true,
                Location = new Point(SlicerMargin, y + 4),
                Text = slicer.Label
            });

            Control input = slicer.Kind switch
            {
                "flag" => new CheckBox
                {
                    Location = new Point(gutter, y),
                    Size = new Size(20, 22)
                },

                // Staff are a list like any other: the report is sent the
                // person's id, which nobody could type. A user slicer was
                // a text box, so a name typed into it filtered nothing.
                "lookup" or "user" => BuildCombo(slicer, gutter, y),

                _ => new TextBox
                {
                    Location = new Point(gutter, y),
                    PlaceholderText = slicer.Kind == "number"
                        ? "e.g. 90"
                        : "Words to look for, or blank for all",
                    Size = new Size(200, 23)
                }
            };

            pnlSlicers.Controls.Add(input);
            _slicerInputs[slicer.Key] = input;
            y += SlicerRowHeight;
        }

        // As tall as what is in it. The height was fixed at 140, so a
        // preset with one slicer showed it stranded above a hand's depth
        // of empty grey, and a preset with none reserved the band anyway.
        pnlSlicers.Height = slicers.Count == 0
            ? 0
            : Math.Min(y + SlicerMargin, SlicerPanelMaximum);

        pnlSlicers.ResumeLayout();
    }

    /// <summary>
    /// Where the inputs line up: after the longest caption, not at a
    /// figure somebody typed once.
    /// </summary>
    /// <remarks>
    /// The captions come from the report catalogue, so this screen cannot
    /// know how wide they are - and the fixed 150 it used was narrower
    /// than several of them. "Expiring within (days)" reached 131 pixels
    /// from a margin of 8 and so ran right up against its own text box;
    /// "Below reorder level only" reached 149 and ran underneath it.
    /// </remarks>
    private int InputColumn(IReadOnlyList<SlicerDto> slicers)
    {
        var widest = slicers
            .Select(s => TextRenderer.MeasureText(s.Label, Font).Width)
            .DefaultIfEmpty(0)
            .Max();

        // Never narrower than the column this screen has always had, so
        // a report of short captions looks exactly as it did, and never
        // so wide that the input is pushed off the edge.
        return Math.Clamp(SlicerMargin + widest + 16, 150, 340);
    }

    /// <summary>
    /// What the operator reads against what the report is sent.
    /// </summary>
    /// <remarks>
    /// The two are not the same thing, and the combo used to add the
    /// value as the item - so it showed "MedicalAid" where it meant
    /// "Medical aid", and would have shown a bare GUID for a department
    /// or a till session. ToString is what a ComboBox draws, so putting
    /// the label there keeps the display and the filter value apart
    /// without a custom draw.
    /// </remarks>
    private sealed class Choice
    {
        public Choice(string value, string label)
        {
            Value = value;
            Label = label;
        }

        public string Value { get; }
        public string Label { get; }

        public override string ToString() => Label;
    }

    private static ComboBox BuildCombo(SlicerDto slicer, int x, int y)
    {
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(x, y),

            // Wider than a code needs, because a till session reads
            // "Till 1, 20/09/2026 08:14" and a department name is
            // whatever the pharmacy called it.
            Size = new Size(260, 23)
        };

        // A blank first entry, so "any" is reachable after a choice.
        combo.Items.Add(new Choice("", ""));

        // Nothing on file yet - no departments, no sessions. Said, and
        // greyed out, rather than a box to type into that could only
        // ever filter on something that does not exist.
        if (slicer.Options.Count == 0)
        {
            combo.Items[0] = new Choice("", "None on file");
            combo.Enabled = false;
        }

        foreach (var option in slicer.Options)
            combo.Items.Add(new Choice(option.Value, option.Label));

        combo.SelectedIndex = 0;

        // A department or a session found by any word of its name.
        if (slicer.Options.Count > 0) WordFilter.Attach(combo);
        return combo;
    }

    public IReadOnlyDictionary<string, string?> SlicerValues =>
        _slicerInputs.ToDictionary(
            pair => pair.Key,
            pair => pair.Value switch
            {
                CheckBox check => check.Checked ? "true" : null,
                ComboBox combo => Blank((combo.SelectedItem as Choice)?.Value),
                _ => Blank(pair.Value.Text)
            },
            StringComparer.Ordinal);

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public void ShowPreview(ReportResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        grdPreview.Columns.Clear();
        grdPreview.Rows.Clear();

        foreach (var column in result.Columns)
            grdPreview.AddColumn(column.Key, column.Header,
                format: column.Format, rightAlign: column.RightAlign);

        // A page at a time: a year of sale lines is forty thousand rows,
        // and the export runs the report again for all of them. A new run
        // starts at its first page.
        var pager = GridPager.Attach(grdPreview);
        pager.Restart();
        pager.ShowRows(result.Rows
            .Select(row => result.Columns
                .Select(c => row.TryGetValue(c.Key, out var v) ? v : null)
                .ToArray())
            .ToList());

        FitColumns();

        // RPT-012 on screen as well as on paper: what was asked, by whom,
        // when, and how current the data is.
        lblProvenance.Text = string.Format(CultureInfo.CurrentCulture,
            "{0}  |  {1:N0} rows  |  Run by {2} at {3:dd/MM/yyyy HH:mm}  |  {4}",
            result.Provenance.ParameterSummary, result.RowCount,
            result.Provenance.RunByUsername, result.Provenance.RunAtLocal,
            result.Provenance.DataCurrency);
    }

    /// <summary>
    /// Each column as wide as what it holds, and the window's width shared
    /// out only when everything already fits.
    /// </summary>
    /// <remarks>
    /// Shared out equally, a report of fifteen columns cut every value to
    /// three letters and an ellipsis. It scrolls sideways now instead, and
    /// a report of four columns still fills the window.
    /// </remarks>
    private void FitColumns()
    {
        if (grdPreview.Columns.Count == 0) return;

        grdPreview.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        grdPreview.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.DisplayedCells);

        var needed = 0;

        foreach (DataGridViewColumn column in grdPreview.Columns)
        {
            column.Width = Math.Min(column.Width + 8, 420);
            needed += column.Width;
        }

        var room = grdPreview.ClientSize.Width
                   - SystemInformation.VerticalScrollBarWidth - 2;

        if (needed > room) return;

        foreach (DataGridViewColumn column in grdPreview.Columns)
            column.FillWeight = Math.Max(1, column.Width);

        grdPreview.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
    }

    public bool IncludeGraph => chkGraph.Checked;

    public void ShowChart(byte[]? png)
    {
        var old = picGraph.Image;
        picGraph.Image = png is null ? null : Image.FromStream(new MemoryStream(png));
        old?.Dispose();

        // A third of the width beside the rows at first. Once somebody
        // has dragged the divider, their width stays for the next report.
        if (!_graphWidened)
            pnlGraph.Width = Math.Clamp(splMain.Panel2.Width / 3, 240, 420);

        ShowGraphPanel();
    }

    private bool _graphWidened;

    private void ShowGraphPanel()
    {
        var shown = chkGraph.Checked && picGraph.Image is not null;
        pnlGraph.Visible = shown;
        splGraph.Visible = shown;
    }

    public void ShowGraphWindow(string title, byte[] png)
    {
        using var window = new frmGraph(title, png);
        window.ShowDialog(this);
    }

    public void ShowFindings(IReadOnlyList<string> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);

        txtFindings.Text = string.Join(Environment.NewLine, findings.Select(f => "• " + f));
        txtFindings.Visible = findings.Count > 0;
    }

    public void ClearPreview(string caption)
    {
        ShowFindings(Array.Empty<string>());
        ShowChart(null);
        GridPager.Attach(grdPreview).ShowRows(Array.Empty<object?[]>());
        grdPreview.Columns.Clear();
        lblProvenance.Text = caption;
    }

    public string? PromptExportPath(string suggestedName, bool pdf)
    {
        using var save = new SaveFileDialog
        {
            DefaultExt = pdf ? "pdf" : "xlsx",
            FileName = suggestedName,
            Filter = pdf ? "PDF document (*.pdf)|*.pdf" : "Excel workbook (*.xlsx)|*.xlsx",
            OverwritePrompt = true,
            Title = "Export report",
            InitialDirectory = GridExport.Downloads
        };

        return save.ShowDialog(this) == DialogResult.OK ? save.FileName : null;
    }

    public event EventHandler? PresetChanged;
    public event EventHandler? PreviewRequested;
    public event EventHandler? ExportRequested;
    public event EventHandler? ExportPdfRequested;
    public event EventHandler? EnlargeGraphRequested;
    public event EventHandler? CloseRequested;
    public event EventHandler? AskStarted;
    public event EventHandler? AskRequested;

    /// <summary>
    /// Sets the split the designer asked for, once the window is its
    /// real size.
    /// </summary>
    /// <remarks>
    /// A SplitContainer clamps SplitterDistance to the width it has at
    /// the time, and in a hand-written designer that is the default
    /// 150 pixels. The clamped value then scales up with the window and
    /// lands nowhere near what was asked for - here it left the report
    /// tree filling four fifths of the screen and the parameters
    /// squeezed off the edge.
    /// </remarks>
    private void SeatSplitter() =>
        splMain.SplitterDistance = Math.Min(300,
            Math.Max(60, (splMain.Orientation == Orientation.Vertical
                ? splMain.Width : splMain.Height) - 120));
}
