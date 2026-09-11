using PharmaX.Domain.Common;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Primitives;

namespace PharmaX.Domain.Reference;

/// <summary>
/// What one medical aid society pays for one product, for a period.
/// </summary>
/// <remarks>
/// This is the rule behind PriceSource.MedicalAidTariff: a dispensing
/// line priced from a tariff is rated, and rated lines are what the
/// rated reports count. Held as its own aggregate rather than as a
/// price on the product because the same product has a different price
/// per society, and each of those prices starts and stops on its own
/// dates.
/// </remarks>
public sealed class Tariff : AggregateRoot
{
    public Guid SchemeId { get; private set; }
    public Guid ProductId { get; private set; }
    public Money Price { get; private set; } = Money.Zero();

    /// <summary>
    /// A dispensing fee the society allows on top of the product price.
    /// Separate because it is negotiated separately and reported on
    /// separately.
    /// </summary>
    public Money DispensingFee { get; private set; } = Money.Zero();

    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
    public bool IsActive { get; private set; } = true;

    public Money Total => Price + DispensingFee;

    private Tariff() { }

    public static Tariff Create(Guid schemeId, Guid productId, Money price,
        Money dispensingFee, DateOnly effectiveFrom,
        DateOnly? effectiveTo = null)
    {
        if (schemeId == Guid.Empty)
            throw new DomainException("A tariff requires a society.");

        if (productId == Guid.Empty)
            throw new DomainException("A tariff requires a product.");

        if (price.IsNegative)
            throw new DomainException("A tariff price cannot be negative.");

        if (dispensingFee.IsNegative)
            throw new DomainException("A dispensing fee cannot be negative.");

        if (effectiveTo is DateOnly end && end < effectiveFrom)
            throw new DomainException(
                "A tariff cannot stop before it starts.");

        return new Tariff
        {
            SchemeId = schemeId,
            ProductId = productId,
            Price = price,
            DispensingFee = dispensingFee,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = effectiveTo
        };
    }

    public void Reprice(Money price, Money dispensingFee)
    {
        if (price.IsNegative)
            throw new DomainException("A tariff price cannot be negative.");

        if (dispensingFee.IsNegative)
            throw new DomainException("A dispensing fee cannot be negative.");

        Price = price;
        DispensingFee = dispensingFee;
    }

    public void Reschedule(DateOnly effectiveFrom, DateOnly? effectiveTo)
    {
        if (effectiveTo is DateOnly end && end < effectiveFrom)
            throw new DomainException(
                "A tariff cannot stop before it starts.");

        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
    }

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;

    /// <summary>
    /// Whether this tariff is the one to price by on a given day.
    /// </summary>
    public bool IsEffectiveOn(DateOnly day) =>
        IsActive
        && EffectiveFrom <= day
        && (EffectiveTo is null || EffectiveTo >= day);
}
