using PharmaX.Domain.Common;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Primitives;

namespace PharmaX.Domain.Sales;

public sealed class SalePayment : Entity
{
    /// <summary>
    /// The sale this paid for, or null where it is a refund on a return.
    /// A tender is the same shape whichever way the money moved, so the
    /// two share this type and one of the two owners is always null.
    /// </summary>
    public Guid? SaleId { get; private set; }
    public TenderType Tender { get; private set; }
    public Money Amount { get; private set; }

    /// Card authorisation code, voucher serial or account reference.
    public string? Reference { get; private set; }

    private SalePayment() { }

    public static SalePayment Create(TenderType tender, Money amount,
                                     string? reference = null)
    {
        if (amount.IsNegative)
            throw new DomainException("A payment cannot be negative.");

        if (tender is TenderType.Card or TenderType.Eft
            && string.IsNullOrWhiteSpace(reference))
            throw new DomainException(
                "A card or EFT payment requires an authorisation reference.");

        return new SalePayment
        {
            Tender = tender,
            Amount = amount,
            Reference = reference
        };
    }

    /// <summary>
    /// Overload taking the tender as text, because the payment DTO crosses
    /// the boundary as a string. Parsing lives here rather than in the
    /// service, so an unknown tender is refused in one place.
    /// </summary>
    public static SalePayment Create(string tenderType, Money amount,
                                     string? reference = null)
    {
        if (!Enum.TryParse<TenderType>(tenderType, ignoreCase: true,
                                       out var tender))
            throw new DomainException($"Unknown tender type '{tenderType}'.");

        return Create(tender, amount, reference);
    }

    internal void AssignTo(Guid saleId) => SaleId = saleId;

    public bool IsRefund => SaleId is null;
}
