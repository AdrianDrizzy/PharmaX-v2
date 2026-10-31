using System.Globalization;
using System.Text;
using PharmaX.Application.Repositories;
using PharmaX.Application.Results;
using PharmaX.Application.Security;
using PharmaX.Application.Suppliers;
using PharmaX.Application.Suppliers.Dtos;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Alerts;

/// <summary>
/// The stock alerts sorted into what a person deals with first, with the
/// sentence the sign-in pop-up shows.
/// </summary>
public sealed record AlertDigest(
    IReadOnlyList<StockAlertDto> OutOfStock,
    IReadOnlyList<StockAlertDto> BelowReorder,
    IReadOnlyList<StockAlertDto> Expired,
    IReadOnlyList<StockAlertDto> ExpiringSoon)
{
    /// How many names a list shows before "and 12 more".
    public const int Shown = 8;

    public bool IsEmpty => OutOfStock.Count + BelowReorder.Count
                           + Expired.Count + ExpiringSoon.Count == 0;

    /// <summary>
    /// The summary as text, worked out here and not by the model, which
    /// gets numbers wrong.
    /// </summary>
    public string Describe()
    {
        if (IsEmpty) return "Nothing needs attention: no stock is out, low or expiring.";

        var text = new StringBuilder();

        Section(text, "Out of stock", OutOfStock, a => a.ProductName);
        Section(text, "Below reorder level", BelowReorder, a =>
            string.Format(CultureInfo.CurrentCulture,
                "{0} ({1:0.##} left, reorder at {2:0.##})",
                a.ProductName, a.Quantity, a.ReorderLevel));
        Section(text, "Expired and still on the shelf", Expired, a =>
            string.Format(CultureInfo.CurrentCulture, "{0} batch {1}, {2:0.##} units",
                a.ProductName, a.BatchNumber, a.Quantity));
        Section(text, "Expiring soon", ExpiringSoon, a =>
            string.Format(CultureInfo.CurrentCulture, "{0} batch {1}, {2:0.##} units in {3} day(s)",
                a.ProductName, a.BatchNumber, a.Quantity, a.DaysLeft));

        return text.ToString().TrimEnd();
    }

    private static void Section(StringBuilder text, string heading,
        IReadOnlyList<StockAlertDto> alerts, Func<StockAlertDto, string> line)
    {
        if (alerts.Count == 0) return;

        text.Append(CultureInfo.CurrentCulture, $"{heading} ({alerts.Count}):").AppendLine();

        foreach (var alert in alerts.Take(Shown))
            text.Append("  - ").AppendLine(line(alert));

        if (alerts.Count > Shown)
            text.Append(CultureInfo.CurrentCulture, $"  ...and {alerts.Count - Shown} more.").AppendLine();

        text.AppendLine();
    }
}

/// <summary>
/// What the assistant drafted at sign-in.
/// </summary>
/// <param name="Orders">The drafts made this time, one per supplier.</param>
/// <param name="NoSupplier">
/// Lines that wanted ordering but whose product has no default supplier
/// (or one no longer trading), so there was nobody to draft them to.
/// </param>
public sealed record DraftedOrders(IReadOnlyList<PurchaseOrderDto> Orders,
    IReadOnlyList<PurchaseOrderLineDto> NoSupplier)
{
    public static readonly DraftedOrders None =
        new(Array.Empty<PurchaseOrderDto>(), Array.Empty<PurchaseOrderLineDto>());

    /// What could not be drafted, and why; empty where everything was.
    public string DescribeMissing()
    {
        if (NoSupplier.Count == 0) return "";

        var names = string.Join(", ", NoSupplier.Take(AlertDigest.Shown).Select(l => l.Description));
        if (NoSupplier.Count > AlertDigest.Shown)
            names += $" and {NoSupplier.Count - AlertDigest.Shown} more";

        return string.Format(CultureInfo.CurrentCulture,
            "{0} item(s) need ordering but have no default supplier, so they are on no order: {1}. "
            + "Set a supplier on the product, or order them by hand.", NoSupplier.Count, names);
    }
}

/// <summary>
/// What the status bar's assistant says about the drafts waiting to be sent.
/// </summary>
public static class DraftNotice
{
    /// The few words on the status bar button; empty where there are no drafts.
    public static string Caption(int drafts) => drafts switch
    {
        0 => "",
        1 => "AI: 1 draft order to finalise",
        _ => $"AI: {drafts} draft orders to finalise"
    };

    /// <param name="justMade">True straight after the assistant drafted them.</param>
    public static string Describe(IReadOnlyList<PurchaseOrderDto> drafts, bool justMade)
    {
        ArgumentNullException.ThrowIfNull(drafts);

        if (drafts.Count == 0) return "There are no draft purchase orders waiting.";

        var one = drafts.Count == 1;
        var text = new StringBuilder();

        text.AppendLine(justMade
            ? one
                ? "I've created the purchase order for these items that are running out:"
                : $"I've created {drafts.Count} purchase orders for the items that are running out:"
            : one
                ? "A draft purchase order is waiting to be finalised:"
                : $"{drafts.Count} draft purchase orders are waiting to be finalised:");

        foreach (var order in drafts)
            text.Append(CultureInfo.CurrentCulture,
                    $"  - {order.OrderNumber} to {order.SupplierName}: {order.Lines.Count} item(s), {order.Total:N2}")
                .AppendLine();

        text.AppendLine()
            .Append(one
                ? "Click here to view and finalise it for sending to the supplier."
                : "Click here to view and finalise them for sending to the suppliers.")
            .AppendLine()
            .Append("Nothing has been sent yet.");

        return text.ToString();
    }
}

/// <summary>
/// Reads the stock alerts at sign-in and drafts the orders they call for.
/// </summary>
/// <remarks>
/// Every figure is computed here: the summary from the alert service, the
/// quantities from PurchasingService.SuggestAsync - which counts what is
/// already on an open or draft order as coming (ADM-DEF-06), so signing in
/// twice does not draft the same stock twice. Nothing is sent: an order
/// is a draft until a person looks it over and sends it.
/// </remarks>
public interface IStockAlertAssistant
{
    Task<Result<AlertDigest>> DigestAsync(CancellationToken ct = default);

    /// <summary>
    /// Drafts an order per default supplier for what has run out or is
    /// below its reorder level. Nothing for a user who may not raise orders.
    /// </summary>
    Task<Result<DraftedOrders>> DraftOrdersAsync(CancellationToken ct = default);
}

public sealed class StockAlertAssistant : IStockAlertAssistant
{
    private readonly IStockAlertService _alerts;
    private readonly IPurchasingService _purchasing;
    private readonly IProductRepository _products;
    private readonly ISupplierRepository _suppliers;
    private readonly IPermissionService _permissions;

    public StockAlertAssistant(IStockAlertService alerts,
        IPurchasingService purchasing, IProductRepository products,
        ISupplierRepository suppliers, IPermissionService permissions)
    {
        _alerts = alerts;
        _purchasing = purchasing;
        _products = products;
        _suppliers = suppliers;
        _permissions = permissions;
    }

    public async Task<Result<AlertDigest>> DigestAsync(CancellationToken ct = default)
    {
        // Anybody signing in, but only as far as they may read stock.
        if (_permissions.Check(PermissionCode.ProductRead) != PermissionOutcome.Granted)
            return Result<AlertDigest>.Ok(new AlertDigest(
                Array.Empty<StockAlertDto>(), Array.Empty<StockAlertDto>(),
                Array.Empty<StockAlertDto>(), Array.Empty<StockAlertDto>()));

        var current = await _alerts.CurrentAsync(ct);
        if (current.IsFailure)
            return Result<AlertDigest>.Fail(current.Error.Code, current.Error.Message);

        List<StockAlertDto> Of(AlertKind kind) =>
            current.Value.Where(a => a.Kind == kind).ToList();

        return Result<AlertDigest>.Ok(new AlertDigest(
            Of(AlertKind.OutOfStock), Of(AlertKind.LowStock),
            Of(AlertKind.Expired), Of(AlertKind.ExpiringSoon)));
    }

    public async Task<Result<DraftedOrders>> DraftOrdersAsync(CancellationToken ct = default)
    {
        if (_permissions.Check(PermissionCode.PurchaseOrderRaise) != PermissionOutcome.Granted)
            return Result<DraftedOrders>.Ok(DraftedOrders.None);

        var suggested = await _purchasing.SuggestAsync(ct);
        if (suggested.IsFailure)
            return Result<DraftedOrders>.Fail(suggested.Error.Code, suggested.Error.Message);

        var bySupplier = new Dictionary<Guid, List<PurchaseOrderLineDto>>();
        var noSupplier = new List<PurchaseOrderLineDto>();

        foreach (var line in suggested.Value)
        {
            var product = await _products.GetAsync(line.ProductId, ct);

            if (product?.DefaultSupplierId is not Guid supplierId
                || await _suppliers.GetAsync(supplierId, ct) is not { IsActive: true })
            {
                noSupplier.Add(line);
                continue;
            }

            if (!bySupplier.TryGetValue(supplierId, out var lines))
                bySupplier[supplierId] = lines = new List<PurchaseOrderLineDto>();

            lines.Add(line);
        }

        var drafted = new List<PurchaseOrderDto>();

        foreach (var (supplierId, lines) in bySupplier)
        {
            var draft = await _purchasing.DraftAsync(new PurchaseOrderEditDto(
                supplierId, null,
                lines.Select(l => new PurchaseOrderLineEditDto(
                    l.ProductCode, l.QuantityOrdered, l.UnitCost)).ToList()), ct);

            if (draft.IsFailure)
                return Result<DraftedOrders>.Fail(draft.Error.Code, draft.Error.Message);

            drafted.Add(draft.Value);
        }

        return Result<DraftedOrders>.Ok(new DraftedOrders(
            drafted.OrderBy(o => o.SupplierName, StringComparer.CurrentCulture).ToList(),
            noSupplier));
    }
}
