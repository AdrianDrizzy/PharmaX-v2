using System.Globalization;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Repositories;
using PharmaX.Application.Results;
using PharmaX.Application.Security;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Alerts;

/// <summary>
/// What is wrong with the stock, in the order a person would deal with it.
/// </summary>
public enum AlertSeverity
{
    /// Worth knowing about on a quiet afternoon.
    Notice = 1,

    /// Wants doing this week.
    Warning = 2,

    /// Wants doing today - money is being lost, or something
    /// unsellable is sitting where it can be sold.
    Critical = 3
}

public enum AlertKind
{
    /// Past its expiry date and still on the shelf.
    Expired = 1,

    /// Still sellable, but not for much longer.
    ExpiringSoon = 2,

    /// Nothing left, on a product the pharmacy means to keep.
    OutOfStock = 3,

    /// At or under the level somebody set as "time to reorder".
    LowStock = 4
}

/// <summary>
/// One thing that needs attention, already written as a sentence.
/// </summary>
/// <param name="Key">
/// Stable between refreshes, so a screen redrawing every few seconds
/// does not lose the row the user had selected.
/// </param>
public sealed record StockAlertDto(string Key, AlertKind Kind,
    AlertSeverity Severity, Guid ProductId, string ProductCode,
    string ProductName, string? BatchNumber, DateOnly? Expiry,
    int? DaysLeft, decimal Quantity, decimal ReorderLevel, string Summary);

/// <summary>
/// The pharmacy's standing list of stock problems.
/// </summary>
/// <remarks>
/// Deliberately stateless: there is nothing to acknowledge and nothing to
/// dismiss, because an alert here is a fact about the shelf rather than a
/// message about it. An expiring batch stops being listed when it is
/// sold, returned or written off, and a product below its reorder level
/// stops when it is reordered - which is the same moment the alert was
/// asking for. A dismiss button would let the list be silenced without
/// any of that happening, and the one alert somebody silenced in March is
/// the one that matters in June.
///
/// Every workstation reads the same shared database, so this is the same
/// list at the dispensary and at both tills.
/// </remarks>
public interface IStockAlertService
{
    /// Everything wrong right now, worst first.
    Task<Result<IReadOnlyList<StockAlertDto>>> CurrentAsync(
        CancellationToken ct = default);
}

public sealed class StockAlertService : IStockAlertService
{
    private readonly IProductRepository _products;
    private readonly IBatchRepository _batches;
    private readonly IPermissionService _permissions;
    private readonly PharmaXOptions _options;
    private readonly IClock _clock;

    public StockAlertService(IProductRepository products,
        IBatchRepository batches, IPermissionService permissions,
        PharmaXOptions options, IClock clock)
    {
        _products = products;
        _batches = batches;
        _permissions = permissions;
        _options = options;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<StockAlertDto>>> CurrentAsync(
        CancellationToken ct = default)
    {
        // Reading stock, not changing it: the same permission the product
        // list needs. A cashier holds it, which is the point - the person
        // who notices a short-dated box is usually the one selling it.
        _permissions.Demand(PermissionCode.ProductRead);

        if (!_options.Alerts.Enabled)
            return Result<IReadOnlyList<StockAlertDto>>.Ok(
                Array.Empty<StockAlertDto>());

        var today = _clock.Today;
        var alerts = new List<StockAlertDto>();

        alerts.AddRange(await ExpiryAlertsAsync(today, ct));
        alerts.AddRange(await LevelAlertsAsync(ct));

        // A user who sees only the over-the-counter catalogue sees only
        // its alerts: an expiring dispensary batch is not a cashier's to
        // know about, any more than the batch itself is.
        if (_permissions.ProductScope() != ProductScope.All)
        {
            var visible = new List<StockAlertDto>();

            foreach (var alert in alerts)
            {
                var product = await _products.GetAsync(alert.ProductId, ct);
                if (product is { RequiresPharmacist: false }) visible.Add(alert);
            }

            alerts = visible;
        }

        // Worst first, then soonest, then by name, so the order is the
        // order somebody would work through them.
        var ordered = alerts
            .OrderByDescending(a => a.Severity)
            .ThenBy(a => a.DaysLeft ?? int.MaxValue)
            .ThenBy(a => a.ProductName, StringComparer.CurrentCulture)
            .ToList();

        return Result<IReadOnlyList<StockAlertDto>>.Ok(ordered);
    }

    /// <summary>
    /// Batches that have expired or are about to.
    /// </summary>
    private async Task<IEnumerable<StockAlertDto>> ExpiryAlertsAsync(
        DateOnly today, CancellationToken ct)
    {
        var horizon = today.AddDays(Math.Max(1,
            _options.Alerts.ExpiryWarningDays));

        var batches = await _batches.ExpiringBeforeAsync(horizon, ct);
        var alerts = new List<StockAlertDto>(batches.Count);

        // One lookup per product rather than per batch: a wholesaler
        // delivery can leave a dozen batches of the same line.
        var names = new Dictionary<Guid, (string Code, string Name)>();

        foreach (var batch in batches)
        {
            if (!names.TryGetValue(batch.ProductId, out var product))
            {
                var found = await _products.GetAsync(batch.ProductId, ct);

                product = found is null
                    ? ("", "(unknown product)")
                    : (found.Code, found.Description);

                names[batch.ProductId] = product;
            }

            var days = batch.ExpiryDate.DayNumber - today.DayNumber;

            var (kind, severity) = days < 0
                ? (AlertKind.Expired, AlertSeverity.Critical)
                : (AlertKind.ExpiringSoon,
                   days <= _options.Alerts.ExpiryCriticalDays
                       ? AlertSeverity.Critical
                       : AlertSeverity.Warning);

            alerts.Add(new StockAlertDto(
                Key: $"batch:{batch.Id}",
                Kind: kind,
                Severity: severity,
                ProductId: batch.ProductId,
                ProductCode: product.Code,
                ProductName: product.Name,
                BatchNumber: batch.BatchNumber,
                Expiry: batch.ExpiryDate,
                DaysLeft: days,
                Quantity: batch.QuantityOnHand,
                ReorderLevel: 0m,
                Summary: DescribeExpiry(batch.QuantityOnHand, days)));
        }

        return alerts;
    }

    /// <summary>
    /// Products that have run out, or are down to the level somebody set.
    /// </summary>
    private async Task<IEnumerable<StockAlertDto>> LevelAlertsAsync(
        CancellationToken ct)
    {
        var low = await _products.BelowReorderLevelAsync(ct);

        return low.Select(product => new StockAlertDto(
            Key: $"product:{product.Id}",
            Kind: product.QuantityOnHand <= 0m
                ? AlertKind.OutOfStock
                : AlertKind.LowStock,
            Severity: product.QuantityOnHand <= 0m
                ? AlertSeverity.Critical
                : AlertSeverity.Warning,
            ProductId: product.Id,
            ProductCode: product.Code,
            ProductName: product.Description,
            BatchNumber: null,
            Expiry: null,
            DaysLeft: null,
            Quantity: product.QuantityOnHand,
            ReorderLevel: product.ReorderLevel,
            Summary: DescribeLevel(product.QuantityOnHand,
                                   product.ReorderLevel)));
    }

    private static string DescribeExpiry(decimal quantity, int days)
    {
        var units = quantity.ToString("0.##", CultureInfo.CurrentCulture);

        if (days < 0)
            return string.Format(CultureInfo.CurrentCulture,
                "{0} on the shelf, expired {1} day(s) ago.",
                units, -days);

        if (days == 0)
            return string.Format(CultureInfo.CurrentCulture,
                "{0} expiring today.", units);

        return string.Format(CultureInfo.CurrentCulture,
            "{0} expiring in {1} day(s).", units, days);
    }

    private static string DescribeLevel(decimal onHand, decimal reorderLevel)
    {
        if (onHand <= 0m) return "Out of stock.";

        return string.Format(CultureInfo.CurrentCulture,
            "{0:0.##} left, reorder at {1:0.##}.", onHand, reorderLevel);
    }
}
