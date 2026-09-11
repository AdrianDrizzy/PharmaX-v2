using PharmaX.Domain.Common;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Primitives;

namespace PharmaX.Domain.Suppliers;

/// <summary>
/// What physically arrived, against what was ordered. A receipt is where
/// batch numbers and expiry dates enter the system, which is why it
/// refuses a batch-tracked line without them.
/// </summary>
public sealed class GoodsReceipt : AuditableEntity
{
    private readonly List<GoodsReceiptLine> _lines = new();

    public string ReceiptNumber { get; private set; } = "";
    public Guid SupplierId { get; private set; }
    public Guid? PurchaseOrderId { get; private set; }
    public string? SupplierInvoiceNumber { get; private set; }
    public DateOnly ReceivedOn { get; private set; }
    public Guid ReceivedBy { get; private set; }
    public bool IsPosted { get; private set; }

    public IReadOnlyList<GoodsReceiptLine> Lines => _lines;

    public Money Total => _lines.Aggregate(
        Money.Zero(), (sum, l) => sum + l.LineTotal);

    /// <summary>
    /// What the supplier's invoice says, where it has been captured.
    /// Kept separate from Total so that a mismatch is visible rather
    /// than reconciled away by whoever typed the lines.
    /// </summary>
    public Money? InvoiceTotal { get; private set; }

    public Money? InvoiceVariance =>
        InvoiceTotal is null ? null : InvoiceTotal.Value - Total;

    private GoodsReceipt() { }

    public static GoodsReceipt Create(string receiptNumber, Guid supplierId,
        DateOnly receivedOn, Guid receivedBy, Guid? purchaseOrderId = null,
        string? supplierInvoiceNumber = null) =>
        new()
        {
            ReceiptNumber = receiptNumber,
            SupplierId = supplierId,
            ReceivedOn = receivedOn,
            ReceivedBy = receivedBy,
            PurchaseOrderId = purchaseOrderId,
            SupplierInvoiceNumber = supplierInvoiceNumber
        };

    public GoodsReceiptLine AddLine(Guid productId, string description,
        decimal quantity, Money unitCost, bool isBatchTracked,
        string? batchNumber = null, DateOnly? expiryDate = null,
        int? purchaseOrderLineNo = null)
    {
        EnsureNotPosted();

        if (isBatchTracked && (string.IsNullOrWhiteSpace(batchNumber)
                               || expiryDate is null))
            throw new DomainException(
                $"{description} is batch tracked: a batch number and an "
                + "expiry date are required at receipt (BR-INV-02).");

        var line = GoodsReceiptLine.Create(productId, description, quantity,
            unitCost, batchNumber, expiryDate, purchaseOrderLineNo);

        line.AssignTo(Id, _lines.Count + 1);
        _lines.Add(line);
        return line;
    }

    public void RecordInvoiceTotal(Money total)
    {
        EnsureNotPosted();
        InvoiceTotal = total;
    }

    public void RemoveLine(int lineNo)
    {
        EnsureNotPosted();

        var line = _lines.SingleOrDefault(l => l.LineNo == lineNo)
                   ?? throw new DomainException($"Line {lineNo} not found.");

        _lines.Remove(line);

        // Renumbered so the printed receipt has no gaps, which is what
        // the supplier's query will refer to.
        for (var i = 0; i < _lines.Count; i++)
            _lines[i].AssignTo(Id, i + 1);
    }

    /// <summary>
    /// Posts the receipt, which is the point at which stock moves. Posting
    /// is one-way: a mistake is corrected by an adjustment, never by
    /// unposting and editing.
    /// </summary>
    public void Post()
    {
        EnsureNotPosted();

        if (_lines.Count == 0)
            throw new DomainException("An empty receipt cannot be posted.");

        IsPosted = true;
    }

    private void EnsureNotPosted()
    {
        if (IsPosted)
            throw new DomainException(
                "A posted receipt cannot be changed. Raise an adjustment.");
    }
}

public sealed class GoodsReceiptLine : Entity
{
    public Guid GoodsReceiptId { get; private set; }
    public int LineNo { get; private set; }
    public int? PurchaseOrderLineNo { get; private set; }
    public Guid ProductId { get; private set; }
    public string Description { get; private set; } = "";
    public decimal Quantity { get; private set; }
    public Money UnitCost { get; private set; }
    public string? BatchNumber { get; private set; }
    public DateOnly? ExpiryDate { get; private set; }

    public Money LineTotal => UnitCost * Quantity;

    private GoodsReceiptLine() { }

    internal static GoodsReceiptLine Create(Guid productId,
        string description, decimal quantity, Money unitCost,
        string? batchNumber, DateOnly? expiryDate, int? purchaseOrderLineNo)
    {
        if (quantity <= 0m)
            throw new DomainException("A receipt line must be positive.");

        return new GoodsReceiptLine
        {
            ProductId = productId,
            Description = description,
            Quantity = quantity,
            UnitCost = unitCost,
            BatchNumber = batchNumber,
            ExpiryDate = expiryDate,
            PurchaseOrderLineNo = purchaseOrderLineNo
        };
    }

    internal void AssignTo(Guid goodsReceiptId, int lineNo)
    {
        GoodsReceiptId = goodsReceiptId;
        LineNo = lineNo;
    }
}
