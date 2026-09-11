using PharmaX.Domain.Common;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Primitives;

namespace PharmaX.Domain.Sales;

public enum QuotationStatus { Open, Issued, Expired, Accepted }

/// <summary>
/// A priced list of items given to a customer who has not bought them.
/// </summary>
/// <remarks>
/// Deliberately not a Sale with a flag. A sale moves stock, consumes a
/// document number from the sequence an auditor reads, and lands in the
/// VAT return; a quotation does none of those and must not be capable of
/// them by accident. Accepting one produces a sale through the ordinary
/// till path rather than turning into one.
/// </remarks>
public sealed class Quotation : AggregateRoot
{
    private readonly List<QuotationLine> _lines = new();

    public string QuotationNumber { get; private set; } = "";
    public string CustomerName { get; private set; } = "";
    public Guid? PatientId { get; private set; }
    public Guid OperatorId { get; private set; }
    public DateOnly QuotedOn { get; private set; }

    /// <summary>
    /// After this the prices on it are no longer the pharmacy's offer.
    /// Held on the quotation rather than computed, because the period a
    /// pharmacy is prepared to hold a price for is its own decision.
    /// </summary>
    public DateOnly ValidUntil { get; private set; }

    public QuotationStatus Status { get; private set; } = QuotationStatus.Open;
    public string? Notes { get; private set; }

    public IReadOnlyList<QuotationLine> Lines => _lines;

    public Money Total => _lines.Aggregate(Money.Zero(),
        (sum, l) => sum + l.LineTotal);

    public static Quotation Open(string quotationNumber, string customerName,
        Guid operatorId, DateOnly quotedOn, int validForDays,
        Guid? patientId = null)
    {
        if (string.IsNullOrWhiteSpace(customerName))
            throw new DomainException("A quotation requires a customer name.");

        if (validForDays < 1)
            throw new DomainException(
                "A quotation must be valid for at least a day.");

        return new Quotation
        {
            QuotationNumber = quotationNumber,
            CustomerName = customerName.Trim(),
            OperatorId = operatorId,
            QuotedOn = quotedOn,
            ValidUntil = quotedOn.AddDays(validForDays),
            PatientId = patientId
        };
    }

    public QuotationLine AddLine(Guid productId, string description,
        decimal quantity, Money unitPrice, string taxCode)
    {
        EnsureOpen();

        if (quantity <= 0m)
            throw new DomainException(
                "A quotation line needs a quantity above zero.");

        var line = QuotationLine.Create(_lines.Count + 1, productId,
            description, quantity, unitPrice, taxCode);

        _lines.Add(line);
        return line;
    }

    public void RemoveLine(int lineNo)
    {
        EnsureOpen();

        var line = _lines.Find(l => l.LineNo == lineNo)
            ?? throw new DomainException($"There is no line {lineNo}.");

        _lines.Remove(line);

        // Renumbered so the printed quotation reads 1, 2, 3 rather than
        // explaining to a customer why it skips from 1 to 3.
        for (var i = 0; i < _lines.Count; i++) _lines[i].Renumber(i + 1);
    }

    public void Note(string? notes) =>
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

    /// <summary>
    /// Hands it to the customer. An issued quotation is fixed: the
    /// prices on the paper they are holding are the offer.
    /// </summary>
    public void Issue()
    {
        EnsureOpen();

        if (_lines.Count == 0)
            throw new DomainException(
                "A quotation with nothing on it cannot be issued.");

        Status = QuotationStatus.Issued;
    }

    public void Accept(DateOnly today)
    {
        if (Status != QuotationStatus.Issued)
            throw new DomainException(
                "Only an issued quotation can be accepted.");

        if (HasExpiredOn(today))
            throw new DomainException(
                $"This quotation lapsed on {ValidUntil:dd/MM/yyyy}. "
                + "Quote again at today's prices.");

        Status = QuotationStatus.Accepted;
    }

    public void MarkExpired() => Status = QuotationStatus.Expired;

    public bool HasExpiredOn(DateOnly today) => today > ValidUntil;

    private void EnsureOpen()
    {
        if (Status != QuotationStatus.Open)
            throw new DomainException(
                "This quotation has been issued and cannot be changed. "
                + "Start a new one.");
    }
}

public sealed class QuotationLine : Entity
{
    public int LineNo { get; private set; }
    public Guid ProductId { get; private set; }
    public string Description { get; private set; } = "";
    public decimal Quantity { get; private set; }
    public Money UnitPrice { get; private set; } = Money.Zero();
    public string TaxCode { get; private set; } = "A";

    public Money LineTotal => UnitPrice * Quantity;

    private QuotationLine() { }

    internal static QuotationLine Create(int lineNo, Guid productId,
        string description, decimal quantity, Money unitPrice,
        string taxCode) =>
        new()
        {
            LineNo = lineNo,
            ProductId = productId,
            Description = description,
            Quantity = quantity,
            UnitPrice = unitPrice,
            TaxCode = taxCode
        };

    internal void Renumber(int lineNo) => LineNo = lineNo;
}
