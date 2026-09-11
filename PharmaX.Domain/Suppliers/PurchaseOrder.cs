using PharmaX.Domain.Common;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Primitives;

namespace PharmaX.Domain.Suppliers;

public enum PurchaseOrderStatus
{
    Draft, Sent, PartiallyReceived, Received, Cancelled
}

public sealed class PurchaseOrder : AuditableEntity
{
    private readonly List<PurchaseOrderLine> _lines = new();

    public string OrderNumber { get; private set; } = "";
    public Guid SupplierId { get; private set; }
    public DateOnly OrderedOn { get; private set; }
    public DateOnly? ExpectedOn { get; private set; }
    public PurchaseOrderStatus Status { get; private set; } =
        PurchaseOrderStatus.Draft;

    public IReadOnlyList<PurchaseOrderLine> Lines => _lines;

    public Money Total => _lines.Aggregate(
        Money.Zero(), (sum, l) => sum + l.LineTotal);

    public bool IsFullyReceived =>
        _lines.Count > 0 && _lines.TrueForAll(l => l.IsFullyReceived);

    private PurchaseOrder() { }

    public static PurchaseOrder Raise(string orderNumber, Guid supplierId,
        DateOnly orderedOn, DateOnly? expectedOn = null) =>
        new()
        {
            OrderNumber = orderNumber,
            SupplierId = supplierId,
            OrderedOn = orderedOn,
            ExpectedOn = expectedOn
        };

    public PurchaseOrderLine AddLine(Guid productId, string description,
                                     decimal quantity, Money unitCost)
    {
        if (Status != PurchaseOrderStatus.Draft)
            throw new DomainException(
                "Only a draft order can be changed. Raise a new order.");

        var line = PurchaseOrderLine.Create(productId, description, quantity,
                                            unitCost);
        line.AssignTo(Id, _lines.Count + 1);
        _lines.Add(line);
        return line;
    }

    public void Send()
    {
        if (_lines.Count == 0)
            throw new DomainException("An empty order cannot be sent.");

        Status = PurchaseOrderStatus.Sent;
    }

    /// <summary>
    /// Records what a goods receipt actually brought in. Short deliveries
    /// are the norm rather than the exception, so the order moves to
    /// PartiallyReceived and stays open rather than being forced closed.
    /// </summary>
    public void RecordReceipt(int lineNo, decimal quantity)
    {
        var line = _lines.SingleOrDefault(l => l.LineNo == lineNo)
                   ?? throw new DomainException($"Line {lineNo} not found.");

        line.Receive(quantity);

        Status = IsFullyReceived
            ? PurchaseOrderStatus.Received
            : PurchaseOrderStatus.PartiallyReceived;
    }

    public void Cancel(string reason)
    {
        if (Status == PurchaseOrderStatus.Received)
            throw new DomainException("A received order cannot be cancelled.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("Cancelling an order requires a reason.");

        Status = PurchaseOrderStatus.Cancelled;
    }
}

public sealed class PurchaseOrderLine : Entity
{
    public Guid PurchaseOrderId { get; private set; }
    public int LineNo { get; private set; }
    public Guid ProductId { get; private set; }
    public string Description { get; private set; } = "";
    public decimal QuantityOrdered { get; private set; }
    public decimal QuantityReceived { get; private set; }
    public Money UnitCost { get; private set; }

    public Money LineTotal => UnitCost * QuantityOrdered;
    public decimal Outstanding => QuantityOrdered - QuantityReceived;
    public bool IsFullyReceived => Outstanding <= 0m;

    private PurchaseOrderLine() { }

    internal static PurchaseOrderLine Create(Guid productId,
        string description, decimal quantity, Money unitCost)
    {
        if (quantity <= 0m)
            throw new DomainException("Order quantity must be positive.");

        return new PurchaseOrderLine
        {
            ProductId = productId,
            Description = description,
            QuantityOrdered = quantity,
            UnitCost = unitCost
        };
    }

    internal void AssignTo(Guid purchaseOrderId, int lineNo)
    {
        PurchaseOrderId = purchaseOrderId;
        LineNo = lineNo;
    }

    internal void Receive(decimal quantity)
    {
        if (quantity <= 0m)
            throw new DomainException("A receipt must be greater than zero.");

        QuantityReceived += quantity;
    }
}
