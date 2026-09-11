using PharmaX.Application.Suppliers;
using PharmaX.Application.Suppliers.Dtos;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Suppliers;

public interface IPurchaseOrderListView : IView
{
    Guid? SupplierFilter { get; }
    Guid? SelectedId { get; }

    void ShowOrders(IReadOnlyList<PurchaseOrderDto> orders);
    void ShowSuggestions(IReadOnlyList<PurchaseOrderLineDto> suggestions);
    string? PromptCancelReason();

    event EventHandler? RefreshRequested;
    event EventHandler? SuggestRequested;
    event EventHandler? CancelOrderRequested;
    event EventHandler? ChooseRequested;
}

public sealed class PurchaseOrderListPresenter
    : PresenterBase<IPurchaseOrderListView>
{
    private readonly IPurchasingService _purchasing;

    public PurchaseOrderListPresenter(IPurchasingService purchasing) =>
        _purchasing = purchasing;

    protected override void Subscribe()
    {
        View.RefreshRequested += (_, _) => _ = LoadAsync();
        View.SuggestRequested += (_, _) => _ = SuggestAsync();
        View.CancelOrderRequested += (_, _) => _ = CancelAsync();

        View.ChooseRequested += (_, _) =>
        {
            if (View.SelectedId is not null) View.CloseView(true);
            else View.ShowInfo("Choose an order first.");
        };
    }

    public override Task InitialiseAsync() => LoadAsync();

    private Task LoadAsync() => GuardAsync(async () =>
    {
        var result = await _purchasing.OutstandingAsync(View.SupplierFilter);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.ShowOrders(result.Value);
    });

    /// <summary>
    /// What to order, from the reorder levels. The first thing a stock
    /// controller wants on a Monday.
    /// </summary>
    private Task SuggestAsync() => GuardAsync(async () =>
    {
        var result = await _purchasing.SuggestAsync();

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        if (result.Value.Count == 0)
        {
            View.ShowInfo("Nothing is below its reorder level.");
            return;
        }

        View.ShowSuggestions(result.Value);
    });

    private Task CancelAsync() => GuardAsync(async () =>
    {
        if (View.SelectedId is not Guid id)
        {
            View.ShowInfo("Choose an order first.");
            return;
        }

        var reason = View.PromptCancelReason();
        if (string.IsNullOrWhiteSpace(reason)) return;

        var result = await _purchasing.CancelAsync(id, reason);

        if (result.IsFailure) View.ShowError(result.Error.Message);
        await LoadAsync();
    });
}

public partial class frmPurchaseOrderList : PhxFormBase,
    IPurchaseOrderListView
{
    private readonly PurchaseOrderListPresenter? _presenter;

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.ProductRead;

    /// Designer only. Never called at runtime.
    public frmPurchaseOrderList()
    {
        InitializeComponent();

        btnRefresh.Click += (_, _) =>
            RefreshRequested?.Invoke(this, EventArgs.Empty);
        btnSuggest.Click += (_, _) =>
            SuggestRequested?.Invoke(this, EventArgs.Empty);
        btnCancelOrder.Click += (_, _) =>
            CancelOrderRequested?.Invoke(this, EventArgs.Empty);
        btnChoose.Click += (_, _) =>
            ChooseRequested?.Invoke(this, EventArgs.Empty);
        btnClose.Click += (_, _) => CloseView(false);

        grdOrders.SelectionChanged += (_, _) => ShowLinesOfSelected();

        grdOrders.DoubleClick += (_, _) =>
        {
            if (IsPickerMode) ChooseRequested?.Invoke(this, EventArgs.Empty);
        };
    }

    /// Runtime. Resolved by the container.
    public frmPurchaseOrderList(PurchaseOrderListPresenter presenter) : this()
    {
        _presenter = presenter;
        _presenter.Attach(this);
    }

    public bool IsPickerMode { get; private set; }
    public Guid? SupplierFilter { get; private set; }

    public void LoadForSupplier(Guid? supplierId, bool asPicker = false)
    {
        SupplierFilter = supplierId;
        IsPickerMode = asPicker;

        if (asPicker)
        {
            Text = "PharmaX - Choose order";
            btnChoose.Visible = true;
            AcceptButton = btnChoose;
            btnCancelOrder.Visible = false;
            btnSuggest.Visible = false;
        }
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        SeatSplitter();
        if (_presenter is not null) await _presenter.InitialiseAsync();
    }

    public Guid? SelectedId => grdOrders.CurrentRow?.DataBoundItem
        is PurchaseOrderDto dto ? dto.Id : null;

    public void ShowOrders(IReadOnlyList<PurchaseOrderDto> orders)
    {
        ArgumentNullException.ThrowIfNull(orders);

        grdOrders.DataSource = orders.ToList();
        lblCount.Text = $"{orders.Count} outstanding order(s)";

        ShowLinesOfSelected();
    }

    private void ShowLinesOfSelected()
    {
        grdLines.Rows.Clear();

        if (grdOrders.CurrentRow?.DataBoundItem is not PurchaseOrderDto order)
            return;

        foreach (var line in order.Lines)
            grdLines.Rows.Add(line.LineNo, line.Description,
                line.QuantityOrdered, line.QuantityReceived,
                line.Outstanding, line.UnitCost, line.LineTotal);
    }

    /// <summary>
    /// Shown in the lines grid rather than a separate window: the
    /// suggestion is a shopping list to read, and raising the order from
    /// it is a later step.
    /// </summary>
    public void ShowSuggestions(IReadOnlyList<PurchaseOrderLineDto> suggestions)
    {
        ArgumentNullException.ThrowIfNull(suggestions);

        grdOrders.DataSource = null;
        grdLines.Rows.Clear();

        foreach (var line in suggestions)
            grdLines.Rows.Add(line.LineNo, line.Description,
                line.QuantityOrdered, 0m, line.Outstanding, line.UnitCost,
                line.LineTotal);

        lblCount.Text = $"{suggestions.Count} item(s) below reorder level";
    }

    public string? PromptCancelReason() =>
        frmTextPrompt.Ask(this, "Cancel order",
            "Why is this order being cancelled? The order is kept and "
            + "marked cancelled; it is never deleted.");

    public event EventHandler? RefreshRequested;
    public event EventHandler? SuggestRequested;
    public event EventHandler? CancelOrderRequested;
    public event EventHandler? ChooseRequested;

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
        splMain.SplitterDistance = Math.Min(220,
            Math.Max(60, (splMain.Orientation == Orientation.Vertical
                ? splMain.Width : splMain.Height) - 120));
}
