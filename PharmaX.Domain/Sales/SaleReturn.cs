using PharmaX.Domain.Common;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Primitives;

namespace PharmaX.Domain.Sales;

public enum ReturnKind
{
    /// Goods came back over the counter and may go on the shelf again.
    GoodsReturn = 1,

    /// Value credited without goods returning: a pricing error, or
    /// goods too damaged to be worth taking back.
    CreditNote = 2
}

public enum ReturnStatus { Open, Finalised }

/// <summary>
/// A reversal of part or all of a finalised sale. Its own document
/// rather than an edit of the sale, because a finalised sale is a
/// printed record someone may already be holding (BR-POS-07).
/// </summary>
public sealed class SaleReturn : AuditableEntity
{
    private readonly List<SaleReturnLine> _lines = new();
    private readonly List<SalePayment> _refunds = new();

    public string DocumentNumber { get; private set; } = "";
    public ReturnKind Kind { get; private set; }
    public ReturnStatus Status { get; private set; } = ReturnStatus.Open;

    public Guid OriginalSaleId { get; private set; }
    public string OriginalDocumentNumber { get; private set; } = "";

    public Guid BranchId { get; private set; }
    public Guid WorkstationId { get; private set; }
    public Guid TillSessionId { get; private set; }
    public Guid OperatorId { get; private set; }

    /// The supervisor who authorised it. A refund is money leaving the
    /// drawer, so it is never the operator's decision alone (BR-POS-09).
    public Guid AuthorisedBy { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }
    public string Reason { get; private set; } = "";

    public IReadOnlyList<SaleReturnLine> Lines => _lines;
    public IReadOnlyList<SalePayment> Refunds => _refunds;

    public Money Total => _lines.Aggregate(
        Money.Zero(), (sum, l) => sum + l.LineTotal);

    public Money Refunded => _refunds.Aggregate(
        Money.Zero(), (sum, r) => sum + r.Amount);

    /// Lines whose goods go back on the shelf. The rest are written off.
    public IReadOnlyList<SaleReturnLine> Restockable =>
        _lines.Where(l => l.RestockToShelf).ToList();

    public Money WriteOffValue => _lines
        .Where(l => !l.RestockToShelf)
        .Aggregate(Money.Zero(), (sum, l) => sum + l.LineTotal);

    private SaleReturn() { }

    public static SaleReturn Against(Sale sale, ReturnKind kind,
        Guid tillSessionId, Guid operatorId, Guid authorisedBy,
        string reason, DateTime occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(sale);

        if (sale.Status != SaleStatus.Finalised)
            throw new DomainException(
                "Only a finalised sale can be returned against. An open "
                + "sale is changed or abandoned instead.");

        if (tillSessionId == Guid.Empty)
            throw new DomainException(
                "A refund requires an open till session: the money comes "
                + "out of a drawer (POS-013).");

        if (authorisedBy == Guid.Empty)
            throw new DomainException(
                "A return must be authorised by a supervisor (BR-POS-09).");

        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException(
                "A return requires a reason. Without one the pattern of "
                + "returns tells a manager nothing.");

        return new SaleReturn
        {
            Kind = kind,
            OriginalSaleId = sale.Id,
            OriginalDocumentNumber = sale.DocumentNumber,
            BranchId = sale.BranchId,
            WorkstationId = sale.WorkstationId,
            TillSessionId = tillSessionId,
            OperatorId = operatorId,
            AuthorisedBy = authorisedBy,
            Reason = reason.Trim(),
            OccurredAtUtc = occurredAtUtc
        };
    }

    /// <summary>
    /// Adds a line, checking it against the sale it reverses. The
    /// already-returned quantity has to be passed in because previous
    /// returns are separate documents: a line returned twice for its
    /// full quantity would refund the customer twice.
    /// </summary>
    public SaleReturnLine AddLine(SaleLine original, decimal quantity,
        decimal alreadyReturned, bool restockToShelf, DateOnly today,
        string? lineReason = null)
    {
        ArgumentNullException.ThrowIfNull(original);

        EnsureOpen();

        if (original.IsVoided)
            throw new DomainException(
                $"{original.Description} was voided on the original sale, "
                + "so there is nothing to return.");

        if (quantity <= 0m)
            throw new DomainException("Return more than zero.");

        var returnable = original.Quantity - alreadyReturned;

        if (quantity > returnable)
            throw new DomainException(
                $"{original.Description}: {Quantity(original.Quantity)} "
                + $"were sold and {Quantity(alreadyReturned)} already "
                + $"returned, so only {Quantity(returnable)} can come "
                + "back.");

        if (_lines.Exists(l => l.OriginalLineNo == original.LineNo))
            throw new DomainException(
                $"{original.Description} is already on this return. "
                + "Change that line rather than adding a second one.");

        // A credit note credits value without goods coming back, so
        // nothing on one can go on a shelf.
        var restock = Kind == ReturnKind.GoodsReturn && restockToShelf;

        // And expired stock never goes back, whatever the operator
        // ticked: putting it back is how it reaches a patient.
        if (restock && original.BatchExpiry is DateOnly expiry
            && expiry < today)
            throw new DomainException(
                $"{original.Description}: batch expired {expiry:yyyy-MM}. "
                + "It can be credited but it cannot go back on the shelf.");

        var line = SaleReturnLine.Create(original, quantity, restock,
                                         lineReason);

        line.AssignTo(Id, _lines.Count + 1);
        _lines.Add(line);
        return line;
    }

    public void AddRefund(SalePayment refund)
    {
        EnsureOpen();
        _refunds.Add(refund);
    }

    public void Finalise(string documentNumber, DateTime nowUtc)
    {
        EnsureOpen();

        if (_lines.Count == 0)
            throw new DomainException("A return must have at least one line.");

        // The mirror of BR-POS-01: what is refunded has to equal what is
        // being credited, or the drawer will not balance at cash-up.
        if (Refunded != Total)
            throw new DomainException(
                $"Refunded {Refunded} does not equal the credit of {Total}.");

        DocumentNumber = documentNumber;
        Status = ReturnStatus.Finalised;
    }

    /// A quantity as a person reads it: three, not 3.000.
    private static string Quantity(decimal value) =>
        value.ToString("0.###", System.Globalization.CultureInfo.CurrentCulture);

    private void EnsureOpen()
    {
        if (Status != ReturnStatus.Open)
            throw new DomainException(
                "A finalised return cannot be changed. Raise another one.");
    }
}

public sealed class SaleReturnLine : Entity
{
    public Guid SaleReturnId { get; private set; }
    public int LineNo { get; private set; }

    /// The line on the original sale this reverses, so the two documents
    /// can be reconciled without matching on description and price.
    public int OriginalLineNo { get; private set; }

    public Guid OriginalSaleLineId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid? BatchId { get; private set; }
    public DateOnly? BatchExpiry { get; private set; }
    public string Description { get; private set; } = "";
    public decimal Quantity { get; private set; }

    /// Taken from the original line, not looked up again: a customer
    /// gets back what they paid, not what the item costs today.
    public Money UnitPrice { get; private set; }

    public decimal VatRatePercent { get; private set; }
    public string TaxCode { get; private set; } = "A";
    public bool RestockToShelf { get; private set; }
    public string? LineReason { get; private set; }

    public Money LineTotal => UnitPrice * Quantity;

    private SaleReturnLine() { }

    internal static SaleReturnLine Create(SaleLine original,
        decimal quantity, bool restockToShelf, string? lineReason) =>
        new()
        {
            OriginalLineNo = original.LineNo,
            OriginalSaleLineId = original.Id,
            ProductId = original.ProductId,
            BatchId = original.BatchId,
            BatchExpiry = original.BatchExpiry,
            Description = original.Description,
            Quantity = quantity,

            // The price actually charged, discount included, so a
            // discounted item is credited at what the customer paid.
            UnitPrice = original.Quantity == 0m
                ? original.UnitPrice
                : Money.Of(original.LineTotal.Amount / original.Quantity,
                           original.UnitPrice.Currency),

            VatRatePercent = original.VatRatePercent,
            TaxCode = original.TaxCode,
            RestockToShelf = restockToShelf,
            LineReason = lineReason
        };

    internal void AssignTo(Guid saleReturnId, int lineNo)
    {
        SaleReturnId = saleReturnId;
        LineNo = lineNo;
    }
}
