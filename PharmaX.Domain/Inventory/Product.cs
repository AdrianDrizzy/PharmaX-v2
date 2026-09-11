using PharmaX.Domain.Common;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Pricing;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Sales;

namespace PharmaX.Domain.Inventory;

/// <summary>
/// A sellable or dispensable item. Schedule drives whether dispensing needs
/// a pharmacist (DSP-006); batch tracking drives whether the till has to
/// pick a batch by FEFO before it can add a line.
/// </summary>
public sealed class Product : AuditableEntity
{
    private readonly List<string> _barcodes = new();
    private readonly List<ProductPrice> _prices = new();

    public string Code { get; private set; } = "";
    public string Description { get; private set; } = "";
    public string? GenericName { get; private set; }
    public int Schedule { get; private set; }
    public bool IsBatchTracked { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string TaxCode { get; private set; } = "A";
    public decimal VatRatePercent { get; private set; } = 15m;
    public Money RetailPrice { get; private set; } = Money.Zero();
    public Money CostPrice { get; private set; } = Money.Zero();
    public decimal ReorderLevel { get; private set; }
    public decimal QuantityOnHand { get; private set; }
    public string? PackSize { get; private set; }
    public Guid? DefaultSupplierId { get; private set; }

    public IReadOnlyList<string> Barcodes => _barcodes;
    public IReadOnlyList<ProductPrice> Prices => _prices;

    /// Schedule 0 to 2 sell over the counter; 3 and above need dispensing.
    public bool RequiresPharmacist => Schedule >= 3;
    public bool IsBelowReorderLevel => QuantityOnHand <= ReorderLevel;

    private Product() { }

    public static Product Create(string code, string description,
        Money retailPrice, decimal vatRatePercent = 15m, string taxCode = "A",
        int schedule = 0, bool isBatchTracked = false)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("A product requires a code.");

        if (string.IsNullOrWhiteSpace(description))
            throw new DomainException("A product requires a description.");

        if (schedule is < 0 or > 8)
            throw new DomainException("Schedule must be between 0 and 8.");

        return new Product
        {
            Code = code.Trim().ToUpperInvariant(),
            Description = description.Trim(),
            RetailPrice = retailPrice,
            VatRatePercent = vatRatePercent,
            TaxCode = taxCode,
            Schedule = schedule,
            IsBatchTracked = isBatchTracked
        };
    }

    public void Rename(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainException("A product requires a description.");

        Description = description.Trim();
    }

    public void AddBarcode(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
            throw new DomainException("A barcode cannot be blank.");

        var normalised = barcode.Trim();

        if (!_barcodes.Contains(normalised, StringComparer.OrdinalIgnoreCase))
            _barcodes.Add(normalised);
    }

    public bool MatchesCode(string scannedCode) =>
        string.Equals(Code, scannedCode?.Trim(), StringComparison.OrdinalIgnoreCase)
        || _barcodes.Contains(scannedCode?.Trim() ?? "",
                              StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A price change is a permissioned act (inv.price.change) and the
    /// service demands that permission before calling this.
    /// </summary>
    public void ChangeRetailPrice(Money price)
    {
        if (price.IsNegative)
            throw new DomainException("A retail price cannot be negative.");

        RetailPrice = price;
    }

    public void ChangeCostPrice(Money price) => CostPrice = price;

    public void SetReorderLevel(decimal level)
    {
        if (level < 0m)
            throw new DomainException("A reorder level cannot be negative.");

        ReorderLevel = level;
    }

    public void Deactivate() => IsActive = false;
    public void Reactivate() => IsActive = true;

    public void SetQuantityOnHand(decimal quantity) => QuantityOnHand = quantity;

    public void AddPrice(ProductPrice price)
    {
        ArgumentNullException.ThrowIfNull(price);
        _prices.Add(price);
    }

    /// <summary>
    /// Picks one price from all the candidates that apply. Precedence is the
    /// numeric order of PriceSource (specification Section 6.4), so adding a
    /// new kind of price is an enum value rather than an edit here.
    /// </summary>
    public PriceQuote ResolvePrice(Guid? patientId)
    {
        var candidates = new List<PriceQuote>
        {
            new(RetailPrice, PriceSource.StandardRetail)
        };

        foreach (var price in _prices.Where(p => p.AppliesTo(patientId)))
            candidates.Add(new PriceQuote(price.Amount, price.Source));

        return PricingPolicy.Resolve(candidates);
    }
}

/// <summary>
/// One alternative price for a product: a medical aid tariff, an agreed
/// account price or a promotion. A null PatientId means it applies to
/// everyone.
/// </summary>
public sealed class ProductPrice : Entity
{
    public Guid ProductId { get; private set; }
    public Money Amount { get; private set; }
    public PriceSource Source { get; private set; }
    public Guid? PatientId { get; private set; }
    public DateOnly? EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }

    private ProductPrice() { }

    public static ProductPrice Create(Guid productId, Money amount,
        PriceSource source, Guid? patientId = null,
        DateOnly? effectiveFrom = null, DateOnly? effectiveTo = null) =>
        new()
        {
            ProductId = productId,
            Amount = amount,
            Source = source,
            PatientId = patientId,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = effectiveTo
        };

    public bool AppliesTo(Guid? patientId) =>
        PatientId is null || PatientId == patientId;

    public bool IsEffectiveOn(DateOnly day) =>
        (EffectiveFrom is null || EffectiveFrom <= day)
        && (EffectiveTo is null || EffectiveTo >= day);
}
