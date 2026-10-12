using System.Globalization;
using PharmaX.Application.Abstractions;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Primitives;

namespace PharmaX.Application.Inventory;

/// <summary>
/// The pharmacy's pricing rules: a default markup on cost for an item
/// with no price yet, the margin kept when a delivery costs more, and
/// every price worked out rounded up to a step (System setup, Prices).
/// </summary>
public static class Pricing
{
    public static decimal RoundUp(decimal price, decimal step) =>
        step <= 0m ? Math.Round(price, 2) : Math.Ceiling(price / step) * step;

    /// The retail a delivery at newCost calls for, or null to leave it.
    /// Prices only go up here; lowering one is a person's decision.
    public static decimal? NewRetail(decimal retail, decimal oldCost,
        decimal newCost, decimal markupPercent, decimal step)
    {
        decimal target;

        if (retail > 0m && oldCost > 0m)
            target = newCost > oldCost ? retail * newCost / oldCost : 0m;
        else
            target = markupPercent > 0m ? newCost * (1m + markupPercent / 100m) : 0m;

        var price = RoundUp(target, step);
        return price > retail ? price : null;
    }

    /// A new retail price, with its audit line - which the Price Changes
    /// report reads - and the message the other tills sync from.
    public static void ChangeRetail(Product product, decimal price,
        IAuditWriter audit, IOutbox outbox, string? why = null)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(outbox);

        audit.Applied("inv.price.change", "Product", product.Id,
            $"{product.RetailPrice} to {Money.Of(price)}{why}");

        product.ChangeRetailPrice(Money.Of(price));

        outbox.Append("ProductPrice", product.Id,
            $"{{\"productId\":\"{product.Id}\",\"retail\":{price.ToString(CultureInfo.InvariantCulture)}}}");
    }

    /// Markup percent and rounding step as set, zero where not set.
    public static async Task<(decimal Markup, decimal Step)> ReadAsync(
        IPharmacySettings settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        static decimal Parse(string? value) =>
            decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m;

        return (Parse(await settings.GetAsync(PharmacySettingKey.Markup, ct).ConfigureAwait(false)),
                Parse(await settings.GetAsync(PharmacySettingKey.Rounding, ct).ConfigureAwait(false)));
    }
}
