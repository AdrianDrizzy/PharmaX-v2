using PharmaX.Application.Alerts;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Inventory;

/// <summary>
/// Stock that has expired, is about to, or has run out.
/// </summary>
/// <remarks>
/// The same list on every machine in the pharmacy, because they all read
/// the same database: the pharmacist sees a short-dated batch without
/// waiting for a stock controller to run a report, and a cashier sees
/// that a line is out before promising a customer it is on the shelf.
/// </remarks>
public partial class frmAlerts : PhxFormBase, IAlertView
{
    private readonly AlertPresenter? _presenter;
    private readonly ViewFactory? _views;

    /// <summary>
    /// Re-reads the list while the screen is open.
    /// </summary>
    /// <remarks>
    /// A minute, not the collection queue's five seconds. Nothing here
    /// changes between one breath and the next: a batch does not expire
    /// while somebody is reading about it, and the answer to a low line
    /// is a purchase order rather than a faster refresh.
    /// </remarks>
    private readonly System.Windows.Forms.Timer _poll =
        new() { Interval = 60000 };

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.ProductRead;

    /// Designer only. Never called at runtime.
    public frmAlerts()
    {
        InitializeComponent();

        cboFilter.SelectedIndexChanged += (_, _) =>
            FilterChanged?.Invoke(this, EventArgs.Empty);

        btnProduct.Click += (_, _) =>
            OpenProductRequested?.Invoke(this, EventArgs.Empty);

        btnRefresh.Click += (_, _) =>
            RefreshRequested?.Invoke(this, EventArgs.Empty);

        btnClose.Click += (_, _) => CloseView(false);

        grdAlerts.DoubleClick += (_, _) =>
            OpenProductRequested?.Invoke(this, EventArgs.Empty);

        _poll.Tick += (_, _) => Operations.EnqueueIfIdle(PollAsync);
    }

    /// Runtime. Resolved by the container.
    public frmAlerts(AlertPresenter presenter, ViewFactory views) : this()
    {
        _presenter = presenter;
        _views = views;
        _presenter.Attach(this);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        if (_presenter is not null) await _presenter.InitialiseAsync();

        _poll.Start();
    }

    private async Task PollAsync()
    {
        if (_presenter is not null) await _presenter.RefreshQuietlyAsync();
    }

    public AlertKind? Filter =>
        cboFilter.SelectedItem is Choice choice ? choice.Kind : null;

    public Guid? SelectedProductId =>
        grdAlerts.CurrentRow?.DataBoundItem is AlertRow row
            ? row.ProductId
            : null;

    public void ShowAlerts(IReadOnlyList<StockAlertDto> alerts)
    {
        ArgumentNullException.ThrowIfNull(alerts);

        // Held across the redraw, so a list refreshing on its own timer
        // does not move the row somebody was reading.
        var chosen = grdAlerts.CurrentRow?.DataBoundItem is AlertRow current
            ? current.Key
            : null;

        grdAlerts.DataSource = alerts.Select(AlertRow.From).ToList();

        foreach (DataGridViewRow row in grdAlerts.Rows)
        {
            if (row.DataBoundItem is not AlertRow alert) continue;

            if (alert.Key == chosen) grdAlerts.CurrentCell = row.Cells[0];

            // Colour carries the severity, so the eye finds the urgent
            // rows before it has read a word.
            if (alert.Severity == AlertSeverity.Critical)
                PhxTheme.Flag(row, PhxTheme.Problem);
            else if (alert.Severity == AlertSeverity.Warning)
                PhxTheme.Flag(row, PhxTheme.Attention);
        }

        btnProduct.Enabled = alerts.Count > 0;
    }

    public void ShowSummary(string summary) => lblSummary.Text = summary;

    public void OpenProduct(Guid productId)
    {
        if (_views is null) return;

        // Nothing moves under the editor while it is open, and the list
        // is re-read afterwards: the usual reason to open a product from
        // here is to change the very thing being complained about.
        _poll.Stop();

        try
        {
            using var editor = _views.Create<frmProductEdit>();
            editor.LoadProduct(productId);
            editor.ShowDialog(this);

            RefreshRequested?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _poll.Start();
        }
    }

    /// <summary>
    /// One alert as the grid shows it: severity kept for the colouring,
    /// everything else already written for a person to read.
    /// </summary>
    private sealed record AlertRow(string Key, Guid ProductId,
        AlertSeverity Severity, string What, string ProductCode,
        string ProductName, string BatchNumber, string ExpiryText,
        string Summary)
    {
        public static AlertRow From(StockAlertDto dto) => new(
            dto.Key,
            dto.ProductId,
            dto.Severity,
            AlertPresenter.Describe(dto.Kind),
            dto.ProductCode,
            dto.ProductName,
            dto.BatchNumber ?? "",
            dto.Expiry is DateOnly expiry ? Format.Date(expiry) : "",
            dto.Summary);
    }

    public event EventHandler? RefreshRequested;
    public event EventHandler? FilterChanged;
    public event EventHandler? OpenProductRequested;
}
