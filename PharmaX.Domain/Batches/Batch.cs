using PharmaX.Domain.Common;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Primitives;

namespace PharmaX.Domain.Batches;

public sealed class Batch : AuditableEntity
{
    public Guid ProductId { get; private set; }
    public string BatchNumber { get; private set; } = "";
    public DateOnly ExpiryDate { get; private set; }
    public DateTime ReceivedAtUtc { get; private set; }
    public decimal QuantityOnHand { get; private set; }
    public Money CostPrice { get; private set; }
    public Guid? SupplierId { get; private set; }
    public BatchStatus Status { get; private set; } = BatchStatus.Available;
    public string? StatusReason { get; private set; }

    private Batch() { }

    public static Batch Receive(Guid productId, string batchNumber,
        DateOnly expiryDate, decimal quantity, Money costPrice,
        DateTime receivedAtUtc, Guid? supplierId = null)
    {
        if (string.IsNullOrWhiteSpace(batchNumber))
            throw new DomainException("A batch requires a batch number.");

        if (quantity <= 0m)
            throw new DomainException(
                "A receipt must bring in more than zero units.");

        return new Batch
        {
            ProductId = productId,
            BatchNumber = batchNumber,
            ExpiryDate = expiryDate,
            QuantityOnHand = quantity,
            CostPrice = costPrice,
            ReceivedAtUtc = receivedAtUtc,
            SupplierId = supplierId
        };
    }

    public bool IsExpiredOn(DateOnly today) => ExpiryDate < today;

    public bool IsAvailableOn(DateOnly today) =>
        Status == BatchStatus.Available
        && QuantityOnHand > 0m
        && !IsExpiredOn(today);

    /// <summary>
    /// Takes stock out of the batch. Refuses to go negative: a till that
    /// can sell stock the shelf does not have produces a stock take nobody
    /// can reconcile.
    /// </summary>
    public void Issue(decimal quantity)
    {
        if (quantity <= 0m)
            throw new DomainException("An issue must be greater than zero.");

        if (quantity > QuantityOnHand)
            throw new DomainException(
                $"Batch {BatchNumber} holds {QuantityOnHand}, " +
                $"cannot issue {quantity}.");

        QuantityOnHand -= quantity;
    }

    public void Return(decimal quantity)
    {
        if (quantity <= 0m)
            throw new DomainException("A return must be greater than zero.");

        QuantityOnHand += quantity;
    }

    /// <summary>
    /// Applies a signed correction from a physical count. A delta, not
    /// an absolute: the count records what the shelf held when it was
    /// looked at, and stock can have moved legitimately between then and
    /// the moment the count is posted. Setting the counted figure
    /// outright would silently reverse those movements.
    /// </summary>
    public void AdjustBy(decimal delta)
    {
        var corrected = QuantityOnHand + delta;

        if (corrected < 0m)
            throw new DomainException(
                $"Correcting batch {BatchNumber} by {delta} would take it "
                + $"below zero from {QuantityOnHand}.");

        QuantityOnHand = corrected;
    }

    public void Quarantine(string reason) =>
        ChangeStatus(BatchStatus.Quarantined, reason);

    public void Recall(string reason) =>
        ChangeStatus(BatchStatus.Recalled, reason);

    public void Release(string reason) =>
        ChangeStatus(BatchStatus.Available, reason);

    /// <summary>
    /// Written off. Quantity goes to zero because the stock has physically
    /// left, and a disposed batch holding units would overstate the shelf.
    /// </summary>
    public void Dispose(string reason)
    {
        ChangeStatus(BatchStatus.Disposed, reason);
        QuantityOnHand = 0m;
    }

    public void MarkExpired() =>
        ChangeStatus(BatchStatus.Expired, "Expiry date passed.");

    private void ChangeStatus(BatchStatus status, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException(
                "Changing a batch status requires a reason (BR-INV-04).");

        Status = status;
        StatusReason = reason;
    }
}
