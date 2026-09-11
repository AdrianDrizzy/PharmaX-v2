using PharmaX.Domain.Common;
using PharmaX.Domain.Exceptions;

namespace PharmaX.Domain.Inventory;

/// <summary>
/// The immutable record of one change to stock on hand. Append-only: a
/// mistaken movement is corrected by a compensating movement, never by
/// editing history (specification Section 8).
/// </summary>
public sealed class StockMovement : AuditableEntity
{
    public Guid ProductId { get; private set; }
    public Guid? BatchId { get; private set; }
    public MovementType MovementType { get; private set; }

    /// Signed: negative for an issue, positive for a receipt.
    public decimal Quantity { get; private set; }

    public decimal BalanceAfter { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public string? ReasonCode { get; private set; }

    /// The sale, script or goods receipt that caused the movement.
    public string? SourceDocumentType { get; private set; }
    public Guid? SourceDocumentId { get; private set; }

    private StockMovement() { }

    public static StockMovement Record(Guid productId, Guid? batchId,
        MovementType movementType, decimal quantity, decimal balanceAfter,
        DateTime occurredAtUtc, string? reasonCode = null,
        string? sourceDocumentType = null, Guid? sourceDocumentId = null)
    {
        if (quantity == 0m)
            throw new DomainException(
                "A stock movement of zero units records nothing.");

        var signed = movementType.IsInbound()
            ? Math.Abs(quantity)
            : -Math.Abs(quantity);

        if (movementType == MovementType.Adjustment
            || movementType == MovementType.StockTake)
            signed = quantity;      // an adjustment carries its own sign

        return new StockMovement
        {
            ProductId = productId,
            BatchId = batchId,
            MovementType = movementType,
            Quantity = signed,
            BalanceAfter = balanceAfter,
            OccurredAtUtc = occurredAtUtc,
            ReasonCode = reasonCode,
            SourceDocumentType = sourceDocumentType,
            SourceDocumentId = sourceDocumentId
        };
    }
}
