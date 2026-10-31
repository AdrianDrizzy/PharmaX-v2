using PharmaX.Application.Sales;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Sales;

/// <summary>
/// The till's half of the dispensing hand-off: scripts the pharmacist has
/// issued and the patient has not yet paid for.
/// </summary>
/// <remarks>
/// Every till in the pharmacy shows the same queue, because they read the
/// same database. Whoever takes the payment first takes it off every
/// other screen within a few seconds, and a second cashier who was a
/// moment behind is told so rather than charging the patient twice.
/// </remarks>
public partial class frmCollect : PhxFormBase, ICollectView
{
    private readonly CollectPresenter? _presenter;
    private readonly ViewFactory? _views;

    /// <summary>
    /// Re-reads the queue while the screen is open.
    /// </summary>
    /// <remarks>
    /// Five seconds: fast enough that a patient walking from the
    /// dispensary counter to the till finds themselves already on the
    /// screen, slow enough to be nothing against a handful of indexed
    /// rows. Stopped whenever the till is taking a payment.
    /// </remarks>
    private readonly System.Windows.Forms.Timer _poll =
        new() { Interval = 5000 };

    private readonly System.Windows.Forms.Timer _debounce =
        new() { Interval = 200 };

    /// The matches, offered under the search box as it is typed into.
    private readonly SearchSuggestions _suggestions;

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.SaleCreate;

    /// Designer only. Never called at runtime.
    public frmCollect()
    {
        InitializeComponent();

        _suggestions = new SearchSuggestions(txtFind);
        _suggestions.Chosen += (_, chosen) => Suggest.SelectRow(grdWaiting,
            item => item is WaitingRow w && Equals(w.DispensingId, chosen.Value));

        txtFind.TextChanged += (_, _) => Restart();
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();

            // A new search starts at the first page; the five-second
            // refresh stays on the one being read.
            GridPager.Of(grdWaiting)?.Restart();
            FilterChanged?.Invoke(this, EventArgs.Empty);
        };

        _poll.Tick += (_, _) => Operations.EnqueueIfIdle(PollAsync);

        btnCollect.Click += (_, _) =>
            CollectRequested?.Invoke(this, EventArgs.Empty);

        btnRefresh.Click += (_, _) =>
            RefreshRequested?.Invoke(this, EventArgs.Empty);

        btnClose.Click += (_, _) => CloseView(false);

        grdWaiting.DoubleClick += (_, _) =>
            CollectRequested?.Invoke(this, EventArgs.Empty);

        btnDelete.Click += (_, _) =>
            DeleteRequested?.Invoke(this, EventArgs.Empty);

        grdWaiting.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Delete) return;
            e.Handled = true;
            DeleteRequested?.Invoke(this, EventArgs.Empty);
        };
    }

    /// Runtime. Resolved by the container.
    public frmCollect(CollectPresenter presenter, ViewFactory views) : this()
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
        txtFind.Focus();
    }

    private async Task PollAsync()
    {
        if (_presenter is null) return;

        // A cashier part-way through typing a name is not interrupted:
        // the rows would shuffle under the selection they were about to
        // act on. The next tick catches up.
        if (grdWaiting.IsCurrentCellInEditMode) return;

        await _presenter.RefreshQuietlyAsync();
    }

    private void Restart()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    public string FilterText => txtFind.Text.Trim();

    public Guid? SelectedDispensingId =>
        grdWaiting.CurrentRow?.DataBoundItem is WaitingRow row
            ? row.DispensingId
            : null;

    public IReadOnlyList<Guid> SelectedDispensingIds =>
        grdWaiting.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.DataBoundItem).OfType<WaitingRow>()
            .Select(r => r.DispensingId).ToList();

    public void ShowWaiting(IReadOnlyList<CollectionDto> waiting)
    {
        ArgumentNullException.ThrowIfNull(waiting);

        // The selection survives a refresh, so a cashier who has picked
        // a patient - or several to delete - does not lose them every
        // five seconds.
        var chosen = SelectedDispensingId;
        var picked = SelectedDispensingIds.ToHashSet();

        GridPager.Attach(grdWaiting).Show(waiting.Select(WaitingRow.From));

        foreach (DataGridViewRow row in grdWaiting.Rows)
        {
            if (row.DataBoundItem is not WaitingRow waited) continue;

            if (waited.DispensingId == chosen)
                grdWaiting.CurrentCell = row.Cells[0];

            // Ten minutes at a counter is long enough that somebody
            // should be looking for this patient.
            if (waited.Minutes >= 10)
                PhxTheme.Flag(row, PhxTheme.Attention);
        }

        // After the current cell, which clears every other selected row.
        if (picked.Count > 1)
            foreach (DataGridViewRow row in grdWaiting.Rows)
                if (row.DataBoundItem is WaitingRow w && picked.Contains(w.DispensingId))
                    row.Selected = true;

        btnCollect.Enabled = waiting.Count > 0;
        btnDelete.Enabled = waiting.Count > 0;

        _suggestions.Offer(waiting.Select(w => new Suggestion(w.DispensingId,
            w.PatientName, Suggest.Details(w.ScriptNumber,
                Format.Money(w.CoPayment)))));
    }

    public void ShowSummary(string summary) => lblSummary.Text = summary;

    public bool TakePayment(Guid dispensingId, string patientName)
    {
        if (_views is null) return false;

        // Nothing may move underneath the till while it is open on one
        // script.
        _poll.Stop();

        try
        {
            using var till = _views.Create<frmPOS>();
            till.CollectFor(dispensingId, patientName);
            return till.ShowDialog(this) == DialogResult.OK;
        }
        finally
        {
            _poll.Start();
        }
    }

    /// <summary>
    /// One waiting script as the grid shows it.
    /// </summary>
    /// <remarks>
    /// A row rather than the DTO itself: the grid needs the waiting time
    /// and the money already written the way a person reads them, and
    /// formatting is the screen's job rather than the service's.
    /// </remarks>
    private sealed record WaitingRow(Guid DispensingId, string Waited,
        string PatientName, string ScriptNumber, string DocumentNumber,
        int Items, string Owing, string PharmacistName, int Minutes)
    {
        public static WaitingRow From(CollectionDto dto)
        {
            var waited = DateTime.UtcNow - dto.IssuedAtUtc;

            return new WaitingRow(
                dto.DispensingId,
                CollectPresenter.Describe(waited),
                dto.PatientName,
                dto.ScriptNumber,
                dto.DocumentNumber,
                dto.Items,
                Format.Money(dto.CoPayment),
                dto.PharmacistName,
                Math.Max(0, (int)waited.TotalMinutes));
        }
    }

    public event EventHandler? RefreshRequested;
    public event EventHandler? FilterChanged;
    public event EventHandler? CollectRequested;
    public event EventHandler? DeleteRequested;
}
