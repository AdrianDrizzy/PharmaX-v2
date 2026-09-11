using System.Globalization;
using PharmaX.Application.Inventory.Dtos;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Inventory;

/// <summary>
/// Capture, variance and post in one screen. The guide's menu lists them
/// as three entries, but they are three views of the same count and
/// splitting them across three windows would mean re-finding the count
/// each time.
/// </summary>
public partial class frmStocktakeCapture : PhxFormBase,
    IStocktakeCaptureView
{
    private readonly StocktakeCapturePresenter? _presenter;

    private IReadOnlyList<StocktakeLineDto> _lines =
        Array.Empty<StocktakeLineDto>();

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.ProductRead;

    /// Designer only. Never called at runtime.
    public frmStocktakeCapture()
    {
        InitializeComponent();

        btnCount.Click += (_, _) =>
            CountRequested?.Invoke(this, EventArgs.Empty);
        btnClearCount.Click += (_, _) =>
            ClearCountRequested?.Invoke(this, EventArgs.Empty);
        btnPost.Click += (_, _) => PostRequested?.Invoke(this, EventArgs.Empty);
        btnClose.Click += (_, _) =>
            CloseRequested?.Invoke(this, EventArgs.Empty);

        chkVarianceOnly.CheckedChanged += (_, _) =>
            VarianceFilterToggled?.Invoke(this, EventArgs.Empty);

        // Enter and double-click both count the selected line: a person
        // working down a shelf has one hand on the keyboard.
        grdLines.DoubleClick += (_, _) =>
            CountRequested?.Invoke(this, EventArgs.Empty);
    }

    /// Runtime. Resolved by the container.
    public frmStocktakeCapture(StocktakeCapturePresenter presenter) : this()
    {
        _presenter = presenter;
        _presenter.Attach(this);
    }

    public Guid StocktakeId { get; private set; }

    public void LoadStocktake(Guid id) => StocktakeId = id;

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_presenter is not null) await _presenter.InitialiseAsync();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Enter && grdLines.Focused)
        {
            CountRequested?.Invoke(this, EventArgs.Empty);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    public int? SelectedLineNo =>
        grdLines.CurrentRow?.Cells["LineNo"].Value as int?;

    public bool VarianceOnly => chkVarianceOnly.Checked;

    public void ShowVarianceOnly(bool varianceOnly) =>
        chkVarianceOnly.Checked = varianceOnly;

    public void ShowStocktake(StocktakeDto stocktake)
    {
        ArgumentNullException.ThrowIfNull(stocktake);

        var culture = CultureInfo.CurrentCulture;
        _lines = stocktake.Lines;

        lblHeader.Text = string.Format(culture,
            "{0}  -  {1}  -  opened {2:dd/MM/yyyy HH:mm}  -  {3}",
            stocktake.StocktakeNumber, stocktake.Scope,
            stocktake.OpenedAtUtc.ToLocalTime(), stocktake.Status);

        var selected = SelectedLineNo;

        var shown = chkVarianceOnly.Checked
            ? _lines.Where(l => l.IsCounted && l.Variance != 0m).ToList()
            : _lines.ToList();

        grdLines.Rows.Clear();

        foreach (var line in shown)
        {
            var index = grdLines.Rows.Add(line.LineNo, line.Description,
                line.BatchNumber ?? "",
                line.BatchExpiry?.ToString("MM/yyyy", culture) ?? "",
                line.Expected,
                line.IsCounted ? line.Counted : null,
                line.IsCounted ? line.Variance : null,
                line.IsCounted ? line.VarianceValue : null);

            var row = grdLines.Rows[index];

            // Uncounted is the state that blocks posting, so it is the
            // one that has to be visible at a glance down the list.
            if (!line.IsCounted)
                row.DefaultCellStyle.ForeColor = SystemColors.GrayText;
            else if (line.Variance < 0m)
                PhxTheme.Flag(row, PhxTheme.Problem);
            else if (line.Variance > 0m)
                PhxTheme.Flag(row, PhxTheme.Information);
        }

        Reselect(selected);

        lblProgressValue.Text = string.Format(culture,
            "{0} of {1} counted, {2} to go", stocktake.CountedLines,
            stocktake.Lines.Count, stocktake.UncountedLines);

        lblNetVarianceValue.Text =
            Format.Money(stocktake.NetVarianceValue);

        lblNetVarianceValue.ForeColor = stocktake.NetVarianceValue < 0m
            ? Color.FromArgb(168, 32, 32)
            : SystemColors.ControlText;

        lblShrinkageValue.Text = Format.Money(stocktake.ShrinkageValue);

        lblReasonValue.Text = stocktake.VarianceReason ?? "";

        var open = stocktake.Status == "Counting";

        btnCount.Enabled = open;
        btnClearCount.Enabled = open;
        btnPost.Enabled = open && stocktake.UncountedLines == 0;

        btnPost.Text = stocktake.UncountedLines > 0
            ? $"Post ({stocktake.UncountedLines} uncounted)"
            : "&Post count";
    }

    private void Reselect(int? lineNo)
    {
        if (lineNo is null) return;

        foreach (DataGridViewRow row in grdLines.Rows)
        {
            if (row.Cells["LineNo"].Value is int value && value == lineNo)
            {
                row.Selected = true;
                grdLines.CurrentCell = row.Cells["Description"];
                return;
            }
        }
    }

    public decimal? PromptCount(string description, decimal expected,
                                decimal? current)
    {
        // The expected figure is deliberately not pre-filled: a counter
        // shown the answer tends to agree with it, and a stocktake that
        // agrees with the system by suggestion is worth nothing.
        return frmNumberPrompt.Ask(this, "Count",
            $"{description}\r\n\r\nHow many are on the shelf?",
            current ?? 0m, decimals: 3);
    }

    public string? PromptVarianceReason(decimal netVariance) =>
        frmTextPrompt.Ask(this, "Variance reason",
            string.Format(CultureInfo.CurrentCulture,
                "The net variance is {0:N2}. Why? This is kept on the "
                + "stocktake and appears on the variance report.",
                netVariance));

    public event EventHandler? CountRequested;
    public event EventHandler? ClearCountRequested;
    public event EventHandler? VarianceFilterToggled;
    public event EventHandler? PostRequested;
    public event EventHandler? CloseRequested;
}
