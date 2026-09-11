using System.Globalization;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Reporting;
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

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.ReportOperational;

    /// Designer only. Never called at runtime.
    public frmReports()
    {
        InitializeComponent();

        trePresets.AfterSelect += (_, _) =>
            PresetChanged?.Invoke(this, EventArgs.Empty);

        btnPreview.Click += (_, _) =>
            PreviewRequested?.Invoke(this, EventArgs.Empty);
        btnExport.Click += (_, _) =>
            ExportRequested?.Invoke(this, EventArgs.Empty);
        btnClose.Click += (_, _) =>
            CloseRequested?.Invoke(this, EventArgs.Empty);
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

    public string? SelectedPresetKey =>
        trePresets.SelectedNode?.Tag as string;

    public void ShowPresets(IReadOnlyList<ReportPresetDto> presets)
    {
        ArgumentNullException.ThrowIfNull(presets);

        trePresets.BeginUpdate();
        trePresets.Nodes.Clear();

        foreach (var group in presets.GroupBy(p => p.Group)
                                     .OrderBy(g => g.Key))
        {
            var parent = trePresets.Nodes.Add(group.Key);

            foreach (var preset in group.OrderBy(p => p.DisplayName))
                parent.Nodes.Add(new TreeNode(preset.DisplayName)
                {
                    Tag = preset.Key
                });
        }

        trePresets.ExpandAll();
        trePresets.EndUpdate();
    }

    public void ShowSlicers(IReadOnlyList<SlicerDto> slicers)
    {
        ArgumentNullException.ThrowIfNull(slicers);

        pnlSlicers.SuspendLayout();
        pnlSlicers.Controls.Clear();
        _slicerInputs.Clear();

        var y = 8;

        foreach (var slicer in slicers)
        {
            pnlSlicers.Controls.Add(new Label
            {
                AutoSize = true,
                Location = new Point(8, y + 4),
                Text = slicer.Label
            });

            Control input = slicer.Kind switch
            {
                "flag" => new CheckBox
                {
                    Location = new Point(150, y),
                    Size = new Size(20, 22)
                },

                // A lookup with no options is one whose values load at run
                // time; it falls back to free text rather than an empty
                // dropdown the operator cannot get past.
                "lookup" when slicer.Options.Count > 0 => BuildCombo(slicer, y),

                _ => new TextBox
                {
                    Location = new Point(150, y),
                    Size = new Size(200, 23)
                }
            };

            pnlSlicers.Controls.Add(input);
            _slicerInputs[slicer.Key] = input;
            y += 30;
        }

        pnlSlicers.ResumeLayout();
    }

    private static ComboBox BuildCombo(SlicerDto slicer, int y)
    {
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(150, y),
            Size = new Size(200, 23)
        };

        // A blank first entry, so "any" is reachable after a choice.
        combo.Items.Add("");

        foreach (var option in slicer.Options)
            combo.Items.Add(option.Value);

        combo.SelectedIndex = 0;
        return combo;
    }

    public IReadOnlyDictionary<string, string?> SlicerValues =>
        _slicerInputs.ToDictionary(
            pair => pair.Key,
            pair => pair.Value switch
            {
                CheckBox check => check.Checked ? "true" : null,
                ComboBox combo => Blank(combo.SelectedItem?.ToString()),
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

        foreach (var row in result.Rows)
        {
            var values = result.Columns
                .Select(c => row.TryGetValue(c.Key, out var v) ? v : null)
                .ToArray();

            grdPreview.Rows.Add(values);
        }

        // RPT-012 on screen as well as on paper: what was asked, by whom,
        // when, and how current the data is.
        lblProvenance.Text = string.Format(CultureInfo.CurrentCulture,
            "{0}  |  {1:N0} rows  |  Run by {2} at {3:dd/MM/yyyy HH:mm}  |  {4}",
            result.Provenance.ParameterSummary, result.RowCount,
            result.Provenance.RunByUsername, result.Provenance.RunAtLocal,
            result.Provenance.DataCurrency);
    }

    public event EventHandler? PresetChanged;
    public event EventHandler? PreviewRequested;
    public event EventHandler? ExportRequested;
    public event EventHandler? CloseRequested;

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
