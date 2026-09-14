using System.Globalization;
using PharmaX.Application.Alerts;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Inventory;

public sealed class AlertPresenter : PresenterBase<IAlertView>
{
    private readonly IStockAlertService _alerts;

    private IReadOnlyList<StockAlertDto> _all = Array.Empty<StockAlertDto>();

    public AlertPresenter(IStockAlertService alerts) => _alerts = alerts;

    protected override void Subscribe()
    {
        View.RefreshRequested += (_, _) => _ = LoadAsync();
        View.FilterChanged += (_, _) => Show();
        View.OpenProductRequested += (_, _) => OnOpenProduct();
    }

    public override Task InitialiseAsync() => LoadAsync();

    /// Re-reads without the busy cursor, for the screen's own timer.
    public async Task RefreshQuietlyAsync()
    {
        var result = await _alerts.CurrentAsync();
        if (result.IsFailure) return;

        _all = result.Value;
        Show();
    }

    private Task LoadAsync() => GuardAsync(async () =>
    {
        var result = await _alerts.CurrentAsync();

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        _all = result.Value;
        Show();
    });

    private void OnOpenProduct()
    {
        if (View.SelectedProductId is not Guid id)
        {
            View.ShowInfo("Choose an alert first.");
            return;
        }

        View.OpenProduct(id);
    }

    private void Show()
    {
        var shown = View.Filter is AlertKind kind
            ? _all.Where(a => a.Kind == kind).ToList()
            : _all;

        View.ShowAlerts(shown);
        View.ShowSummary(Summarise(shown));
    }

    /// <summary>
    /// The line above the grid.
    /// </summary>
    /// <remarks>
    /// Leads with the critical count, because that is the number that
    /// decides whether anybody does anything today.
    /// </remarks>
    internal static string Summarise(IReadOnlyList<StockAlertDto> shown)
    {
        if (shown.Count == 0) return "Nothing needs attention.";

        var critical = shown.Count(a => a.Severity == AlertSeverity.Critical);

        if (critical == 0)
            return string.Format(CultureInfo.CurrentCulture,
                "{0} to look at", shown.Count);

        return string.Format(CultureInfo.CurrentCulture,
            "{0} to look at  -  {1} needing attention today",
            shown.Count, critical);
    }

    /// How the kind reads in a column rather than in an enum.
    internal static string Describe(AlertKind kind) => kind switch
    {
        AlertKind.Expired => "Expired",
        AlertKind.ExpiringSoon => "Expiring",
        AlertKind.OutOfStock => "Out of stock",
        AlertKind.LowStock => "Low stock",
        _ => ""
    };
}
