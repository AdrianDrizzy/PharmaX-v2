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

    /// <summary>
    /// How many dispensing units - tablets, capsules, millilitres - the
    /// priced pack holds. Null where the product is handed over in the
    /// packs it is priced as, so that each unit dispensed is one pack.
    /// </summary>
    /// <remarks>
    /// The shelf price and a society's tariff are both the price of a
    /// pack, while a script is dispensed in units. Without this four
    /// capsules and twenty-one were charged the same pack price, so a
    /// short course over-charged the patient and a long one under-charged
    /// the scheme (DEF-0021).
    /// </remarks>
    public decimal? UnitsPerPack { get; private set; }

    /// <summary>
    /// What part of the shop this belongs to, and who makes it. Both
    /// optional: a pharmacy that keeps no department list still sells
    /// things, and the sales and stock reports already slice by
    /// department where one is set.
    /// </summary>
    public Guid? DepartmentId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public Guid? ManufacturerId { get; private set; }

    public IReadOnlyList<string> Barcodes => _barcodes;
    public IReadOnlyList<ProductPrice> Prices => _prices;

    /// Schedule 0 to 2 sell over the counter; 3 and above need dispensing.
    public bool RequiresPharmacist => Schedule >= 3;

    /// <summary>
    /// Whether the cost is one somebody should look at before trusting a
    /// margin worked out from it: none at all, under a tenth of the price,
    /// or at or above it.
    /// </summary>
    /// <remarks>
    /// A wholesaler's list that gives the cost of one tablet against the
    /// price of a thousand reads as a 99.9% margin on every report and
    /// nobody notices: 121 lines of the pharmacy's own list did exactly
    /// that (OCT-OBS-07). A margin over ninety per cent is not impossible,
    /// so this asks for a check rather than refusing anything; a product
    /// with no price has no margin to be wrong about.
    /// </remarks>
    public bool CostNeedsChecking =>
        RetailPrice.Amount > 0m
        && (CostPrice.Amount <= 0m
            || CostPrice.Amount < RetailPrice.Amount * CostCheckFraction
            || CostPrice.Amount >= RetailPrice.Amount);

    /// The share of the price below which a cost is worth a second look.
    public const decimal CostCheckFraction = 0.10m;
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

    /// <summary>
    /// The active ingredient, as the pharmacist would name it: what the
    /// allergy screen matches a patient's allergies against, alongside
    /// the description.
    /// </summary>
    public void SetGenericName(string? genericName) =>
        GenericName = string.IsNullOrWhiteSpace(genericName)
            ? null
            : genericName.Trim();

    public void Rename(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainException("A product requires a description.");

        Description = description.Trim();
    }

    /// <summary>
    /// The code the shelf and the till know this product by.
    /// </summary>
    /// <remarks>
    /// A sale keeps the product's id, so renumbering does not rewrite a
    /// receipt that has already been printed. The code still has to be
    /// unique: two products answering to one code is a scan that rings
    /// up the wrong box.
    /// </remarks>
    public void ChangeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("A product requires a code.");

        Code = code.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Schedule 0 to 8. Three and above is dispensary stock, and the
    /// service is what demands that permission before calling this.
    /// </summary>
    public void SetSchedule(int schedule)
    {
        if (schedule is < 0 or > 8)
            throw new DomainException("Schedule must be between 0 and 8.");

        Schedule = schedule;
    }

    /// <summary>
    /// Whether the till has to pick a batch before it can sell this.
    /// </summary>
    /// <remarks>
    /// Turning it off while a batch still holds stock would leave units
    /// the till could no longer see. The service refuses that; this only
    /// records the answer.
    /// </remarks>
    public void SetBatchTracked(bool tracked) => IsBatchTracked = tracked;

    /// <summary>
    /// Files the product under a department, a category and a
    /// manufacturer, or under none of them.
    /// </summary>
    /// <remarks>
    /// Null clears the setting rather than being ignored, so a product
    /// filed in the wrong department can be taken out of it. All three
    /// are passed every time and none of them defaults: an overload that
    /// quietly cleared the one the caller did not mention is how a
    /// product loses its category the next time somebody edits its
    /// department.
    /// </remarks>
    public void Classify(Guid? departmentId, Guid? categoryId,
                         Guid? manufacturerId)
    {
        DepartmentId = departmentId == Guid.Empty ? null : departmentId;
        CategoryId = categoryId == Guid.Empty ? null : categoryId;
        ManufacturerId = manufacturerId == Guid.Empty ? null : manufacturerId;
    }

    public void AddBarcode(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
            throw new DomainException("A barcode cannot be blank.");

        var normalised = barcode.Trim();

        if (!_barcodes.Contains(normalised, StringComparer.OrdinalIgnoreCase))
            _barcodes.Add(normalised);
    }

    /// <summary>
    /// The barcodes the product scans by, as the product form lists them.
    /// </summary>
    /// <remarks>
    /// Replaced as a set, because the form shows them all and a code
    /// taken off it is one the pack no longer carries. Whether another
    /// product already answers to one is the service's to check: it
    /// needs the catalogue, which the product cannot see.
    /// </remarks>
    public void ReplaceBarcodes(IEnumerable<string> barcodes)
    {
        ArgumentNullException.ThrowIfNull(barcodes);

        var wanted = new List<string>();

        foreach (var raw in barcodes)
        {
            var barcode = raw?.Trim() ?? "";
            if (barcode.Length == 0) continue;

            if (barcode.Length is < 4 or > 48
                || !barcode.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
                throw new DomainException(
                    $"\"{barcode}\" is not a barcode: 4 to 48 letters or "
                    + "digits, as printed under the bars.");

            if (!wanted.Contains(barcode, StringComparer.OrdinalIgnoreCase))
                wanted.Add(barcode);
        }

        _barcodes.Clear();
        _barcodes.AddRange(wanted);
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

    /// <summary>
    /// The VAT the price includes, and the code the VAT return groups it
    /// under: 15 and "A" for standard-rated, 0 and "Z" for zero-rated.
    /// </summary>
    public void SetVat(decimal ratePercent, string taxCode)
    {
        if (ratePercent is < 0m or > 100m)
            throw new DomainException("A VAT rate must be between 0 and 100 per cent.");

        if (string.IsNullOrWhiteSpace(taxCode))
            throw new DomainException("A product requires a tax code.");

        VatRatePercent = ratePercent;
        TaxCode = taxCode.Trim().ToUpperInvariant();
    }

    /// The supplier PharmaX orders the product from by default.
    public void SetDefaultSupplier(Guid? supplierId) => DefaultSupplierId = supplierId;

    public void SetReorderLevel(decimal level)
    {
        if (level < 0m)
            throw new DomainException("A reorder level cannot be negative.");

        ReorderLevel = level;
    }

    public void Deactivate() => IsActive = false;
    public void Reactivate() => IsActive = true;

    public void SetQuantityOnHand(decimal quantity) => QuantityOnHand = quantity;

    public void SetUnitsPerPack(decimal? units)
    {
        if (units is decimal value && value <= 0m)
            throw new DomainException(
                "Units per pack must be more than zero, or left blank.");

        UnitsPerPack = units;
    }

    /// <summary>
    /// What dispensing so many units costs, where a pack costs
    /// <paramref name="packPrice"/>: pro rata to the pack, rounded once to
    /// the cent. A product with no units per pack is dispensed in whole
    /// packs, so two of them cost two packs - the flat per-line price this
    /// replaced charged one insulin refill for two.
    /// </summary>
    public Money PriceForUnits(Money packPrice, decimal quantity) =>
        Money.Of(packPrice.Amount * quantity / UnitsInPack,
                 packPrice.Currency);

    /// <summary>
    /// How many dispensing units one pack on the shelf holds: the units
    /// per pack where it is set, otherwise one - the pack is the unit.
    /// </summary>
    public decimal UnitsInPack =>
        UnitsPerPack is decimal units && units > 0m ? units : 1m;

    /// <summary>
    /// The packs a dispensed quantity takes off the shelf.
    /// </summary>
    /// <remarks>
    /// Stock is counted in packs and a script in tablets, capsules or
    /// millilitres. The dispensary compared the two directly, so sixty
    /// tablets of a sixty-tablet pack were refused against sixteen packs
    /// on hand, and issuing took sixty packs off the shelf for one
    /// (ADM-DEF-15). Rounded to the thousandth the stock is held to, so a
    /// broken pack reads as the fraction of it that is left.
    /// </remarks>
    public decimal PacksFor(decimal units) =>
        Math.Round(units / UnitsInPack, 3, MidpointRounding.AwayFromZero);

    /// <summary>
    /// How the product is packed, as it is written on the shelf label:
    /// "TAB 1000", "LIQ", "UNI".
    /// </summary>
    public void SetPackSize(string? packSize) =>
        PackSize = string.IsNullOrWhiteSpace(packSize)
            ? null
            : packSize.Trim();

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
