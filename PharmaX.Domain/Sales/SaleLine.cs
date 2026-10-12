using PharmaX.Domain.Common;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Pricing;
using PharmaX.Domain.Primitives;

namespace PharmaX.Domain.Sales;

public sealed class SaleLine : Entity
{
    public Guid SaleId { get; private set; }
    public int LineNo { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid? BatchId { get; private set; }

    /// <summary>
    /// The batch number as it was issued, kept on the line rather than
    /// read back through BatchId.
    /// </summary>
    /// <remarks>
    /// A sold line is a record of what left the shelf. The batch row it
    /// came from can be renumbered, merged or archived later, and a
    /// recall needs the number that was on the pack the customer took
    /// home. DispensingLine already kept it; this one did not, which is
    /// why the batch column at the till was always blank.
    /// </remarks>
    public string? BatchNumber { get; private set; }
    public DateOnly? BatchExpiry { get; private set; }
    public string Description { get; private set; } = "";
    public decimal Quantity { get; private set; }
    public Money UnitPrice { get; private set; }
    public Money DiscountAmount { get; private set; }
    public decimal VatRatePercent { get; private set; }
    public string TaxCode { get; private set; } = "A";
    public PriceSource PriceSource { get; private set; }
    public Guid? OverrideAuthoriserId { get; private set; }
    public bool IsRated => PriceSource == PriceSource.MedicalAidTariff;
    public bool IsVoided { get; private set; }

    /// A collected script's co-payment: its stock moved at the issue, and
    /// only the pharmacist changes it. Anything else on the same sale is
    /// sold over the counter like any other line.
    public bool IsScript { get; private set; }
    public string? VoidReason { get; private set; }

    public Money GrossBeforeDiscount => UnitPrice * Quantity;
    public Money LineTotal => IsVoided
        ? Money.Zero()
        : GrossBeforeDiscount - DiscountAmount;

    public VatSplit Vat => VatCalculator.FromInclusive(LineTotal, VatRatePercent);

    private SaleLine() { }

    public static SaleLine Create(Guid productId, string description,
        decimal quantity, Money unitPrice, decimal vatRatePercent,
        string taxCode, PriceSource priceSource,
        Guid? batchId = null, DateOnly? batchExpiry = null,
        string? batchNumber = null, bool isScript = false)
    {
        if (quantity <= 0m)
            throw new DomainException("Quantity must be greater than zero.");

        return new SaleLine
        {
            IsScript = isScript,
            ProductId = productId,
            Description = description,
            Quantity = quantity,
            UnitPrice = unitPrice,
            VatRatePercent = vatRatePercent,
            TaxCode = taxCode,
            PriceSource = priceSource,
            BatchId = batchId,
            BatchNumber = batchNumber,
            BatchExpiry = batchExpiry,
            DiscountAmount = Money.Zero(unitPrice.Currency)
        };
    }

    internal void AssignTo(Guid saleId, int lineNo)
    {
        SaleId = saleId;
        LineNo = lineNo;
    }

    public void ApplyDiscount(Money amount, decimal operatorLimitPercent,
                              Guid? authoriserId)
    {
        var limit = GrossBeforeDiscount.Percentage(operatorLimitPercent);

        if (amount.Amount > limit.Amount && authoriserId is null)
            throw new DomainException(
                "A supervisor must authorise a discount above your limit " +
                "(BR-POS-05).");

        DiscountAmount = amount;
        OverrideAuthoriserId = authoriserId;
    }

    /// <summary>
    /// Changes the quantity on a line already on the sale, which is what a
    /// till operator does far more often than deleting and re-scanning.
    /// </summary>
    public void ChangeQuantity(decimal quantity)
    {
        if (quantity <= 0m)
            throw new DomainException("Quantity must be greater than zero.");

        if (IsVoided)
            throw new DomainException("A voided line cannot be changed.");

        Quantity = quantity;
    }

    /// <summary>
    /// Moves the line onto another batch, when a change of quantity means
    /// FEFO now starts it from a different one.
    /// </summary>
    public void ChangeBatch(Guid batchId, string batchNumber,
                            DateOnly batchExpiry)
    {
        if (IsVoided)
            throw new DomainException("A voided line cannot be changed.");

        BatchId = batchId;
        BatchNumber = batchNumber;
        BatchExpiry = batchExpiry;
    }

    public void Void(string reason, Guid operatorId)
    {
        IsVoided = true;
        VoidReason = reason;
        OverrideAuthoriserId = operatorId;
    }

    public void EnsureSaleable(DateTime nowUtc)
    {
        if (BatchExpiry is null) return;

        // Checked at finalisation, not only when the line was added
        // (BR-POS-03).
        if (BatchExpiry.Value < DateOnly.FromDateTime(nowUtc))
            throw new DomainException(
                $"{Description}: batch expired {BatchExpiry:yyyy-MM}. " +
                "This item cannot be sold.");
    }
}

public enum PriceSource
{
    MedicalAidTariff = 1,
    ManualOverride = 2,
    AgreedAccountPrice = 3,
    Promotion = 4,
    StandardRetail = 5
}
