using PharmaX.Domain.Primitives;
using PharmaX.Domain.Sales;

namespace PharmaX.Domain.Pricing;

/// <summary>
/// One candidate price and where it came from. A product may have several
/// at once: a medical aid tariff, an account price, a promotion and the
/// shelf price.
/// </summary>
public sealed record PriceQuote(Money Amount, PriceSource Source);

/// <summary>
/// Resolves the several candidate prices down to one.
/// </summary>
public static class PricingPolicy
{
    /// <summary>
    /// Precedence is the numeric order of PriceSource (specification
    /// Section 6.4), so this is a Min rather than a chain of if statements
    /// that drifts apart from the specification over time.
    /// </summary>
    public static PriceQuote Resolve(IEnumerable<PriceQuote> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var best = candidates
            .OrderBy(c => (int)c.Source)
            .FirstOrDefault();

        return best ?? new PriceQuote(Money.Zero(), PriceSource.StandardRetail);
    }
}
