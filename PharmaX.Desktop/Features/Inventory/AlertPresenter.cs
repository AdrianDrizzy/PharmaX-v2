using System.Globalization;
using PharmaX.Application.Alerts;
using PharmaX.Application.Inventory;
using PharmaX.Application.Security;
using PharmaX.Desktop.Mvp;
using PharmaX.Domain.Security;

namespace PharmaX.Desktop.Features.Inventory;

public sealed class AlertPresenter : PresenterBase<IAlertView>
{
    private readonly IStockAlertService _alerts;
    private readonly IStockAdjustmentService _adjustments;
    private readonly IPermissionService _permissions;

    private IReadOnlyList<StockAlertDto> _all = Array.Empty<StockAlertDto>();

    public AlertPresenter(IStockAlertService alerts,
                          IStockAdjustmentService adjustments,
                          IPermissionService permissions)
    {
        _alerts = alerts;
        _adjustments = adjustments;
        _permissions = permissions;
    }

    protected override void Subscribe()
    {
        View.RefreshRequested += (_, _) => Enqueue(LoadAsync);
        View.FilterChanged += (_, _) => Show();
        View.OpenProductRequested += (_, _) => OnOpenProduct();
        View.DeleteRequested += (_, _) => Enqueue(DeleteAsync);
    }

    public override Task InitialiseAsync()
    {
        View.AllowDelete(_permissions.Has(PermissionCode.StockRemove));
        return LoadAsync();
    }

    /// <summary>
    /// Deletes the chosen expired and out-of-stock lines: the expired
    /// batches are written off and the out-of-stock products discontinued,
    /// which is what takes them off this list.
    /// </summary>
    private Task DeleteAsync() => GuardAsync(async () =>
    {
        var keys = View.SelectedKeys.ToHashSet(StringComparer.Ordinal);
        var deletable = Shown().Where(IsDeletable).ToList();
        var chosen = deletable.Where(a => keys.Contains(a.Key)).ToList();

        // Ctrl+A chooses the page in view, a hundred lines. Clearing six
        // hundred dead lines should not take six rounds of it.
        if (View.WholePageChosen && deletable.Count > chosen.Count
            && View.Confirm(string.Format(CultureInfo.CurrentCulture,
                "Every line on this page is chosen. Delete all {0:N0} expired "
                + "and out-of-stock lines shown, on every page?\n\n"
                + "No deletes only the {1:N0} on this page.",
                deletable.Count, chosen.Count)))
            chosen = deletable;

        var expired = chosen
            .Where(a => a.Kind == AlertKind.Expired && a.BatchId is not null)
            .Select(a => a.BatchId!.Value)
            .ToList();

        var outOfStock = chosen
            .Where(a => a.Kind is AlertKind.OutOfStock or AlertKind.NothingOnHand)
            .Select(a => a.ProductId)
            .ToList();

        if (expired.Count + outOfStock.Count == 0)
        {
            View.ShowInfo("Choose the expired or out-of-stock lines to delete. "
                          + "Show Expired or Out of stock above, and Ctrl+A "
                          + "chooses every line shown.");
            return;
        }

        var what = string.Join(" and ", new[]
        {
            expired.Count > 0
                ? string.Format(CultureInfo.CurrentCulture,
                    "{0} expired batch(es)", expired.Count)
                : null,
            outOfStock.Count > 0
                ? string.Format(CultureInfo.CurrentCulture,
                    "{0} out-of-stock product(s)", outOfStock.Count)
                : null
        }.OfType<string>());

        if (!View.Confirm($"Delete {what}?\n\nExpired stock is written off "
                          + "and out-of-stock products are discontinued. Their "
                          + "history stays on the reports."))
            return;

        var result = await _adjustments.ClearAsync(expired, outOfStock);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.ShowInfo(result.Value);
        await LoadAsync();
    });

    /// What Delete can take off the list.
    internal static bool IsDeletable(StockAlertDto alert) =>
        alert.Kind is AlertKind.Expired or AlertKind.OutOfStock
                   or AlertKind.NothingOnHand;

    /// Re-reads without the busy cursor, for the screen's own timer.
    public async Task RefreshQuietlyAsync()
    {
        var result = await ReadAsync();
        if (result.IsFailure) return;

        _all = result.Value;
        Show();
    }

    private Task LoadAsync() => GuardAsync(async () =>
    {
        var result = await ReadAsync();

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        _all = result.Value;
        Show();
    });

    /// The alerts, then the products with nothing on hand that only "Out
    /// of stock" shows.
    private async Task<Application.Results.Result<IReadOnlyList<StockAlertDto>>> ReadAsync()
    {
        var current = await _alerts.CurrentAsync();
        if (current.IsFailure) return current;

        var empty = await _alerts.NothingOnHandAsync();
        if (empty.IsFailure) return empty;

        return Application.Results.Result<IReadOnlyList<StockAlertDto>>.Ok(
            current.Value.Concat(empty.Value).ToList());
    }

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
        var shown = Shown();

        View.ShowAlerts(shown);
        View.ShowSummary(Summarise(shown));
    }

    /// <summary>
    /// What the filter shows. Everything leaves out the products with
    /// nothing on hand and no reorder level, which on a price list loaded
    /// without stock is most of the catalogue; "Out of stock" adds them,
    /// so the dead ones can be deleted.
    /// </summary>
    private List<StockAlertDto> Shown() => View.Filter switch
    {
        null => _all.Where(a => a.Kind != AlertKind.NothingOnHand).ToList(),
        AlertKind.OutOfStock => _all.Where(a =>
            a.Kind is AlertKind.OutOfStock or AlertKind.NothingOnHand).ToList(),
        AlertKind kind => _all.Where(a => a.Kind == kind).ToList()
    };

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
        AlertKind.NothingOnHand => "No stock",
        _ => ""
    };
}
