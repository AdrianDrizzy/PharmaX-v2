using System.Globalization;
using PharmaX.Application.Abstractions;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Primitives;

namespace PharmaX.Application.Inventory;

/// <summary>
/// The pharmacy's pricing rules: every delivery prices an item at its cost
/// plus the markup - 50% unless System setup says otherwise, so a 10.00
/// cost sells at 15.00 - rounded up to a step (System setup, Prices).
/// </summary>
public static class Pricing
{
    private const decimal DefaultMarkup = 50m;

    public static decimal RoundUp(decimal price, decimal step) =>
        step <= 0m ? Math.Round(price, 2) : Math.Ceiling(price / step) * step;

    /// The retail a delivery at cost calls for, up or down, or null to leave it.
    public static decimal? NewRetail(decimal retail, decimal cost,
        decimal markupPercent, decimal step)
    {
        // A free line, or a markup set to nothing, says nothing about the price.
        if (cost <= 0m || markupPercent <= 0m) return null;

        var price = RoundUp(cost * (1m + markupPercent / 100m), step);
        return price != retail ? price : null;
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

    /// Markup percent and rounding step as set: a markup of 50 and a step of
    /// zero where not set.
    public static async Task<(decimal Markup, decimal Step)> ReadAsync(
        IPharmacySettings settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        static decimal Parse(string? value) =>
            decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m;

        var markup = await settings.GetAsync(PharmacySettingKey.Markup, ct).ConfigureAwait(false);

        return (string.IsNullOrWhiteSpace(markup) ? DefaultMarkup : Parse(markup),
                Parse(await settings.GetAsync(PharmacySettingKey.Rounding, ct).ConfigureAwait(false)));
    }
}
