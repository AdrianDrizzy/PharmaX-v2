using PharmaX.Domain.Common;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Primitives;

namespace PharmaX.Domain.Sales;

public enum QuotationStatus { Open, Issued, Expired, Accepted }

/// <summary>
/// Where a quotation was made and what it can become: a sale at the
/// till, or a script at the dispensary.
/// </summary>
public enum QuotationKind { OverTheCounter, Dispensary }

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

    /// <summary>
    /// The sale the quotation became, once one has been paid for. What
    /// stops the same quotation being sold, and its stock taken, twice.
    /// </summary>
    public Guid? SaleId { get; private set; }
    public string? Notes { get; private set; }

    public QuotationKind Kind { get; private set; } = QuotationKind.OverTheCounter;

    /// <summary>
    /// The script a dispensary quote became, once the customer went
    /// ahead. What stops one quote being captured, and dispensed, twice.
    /// </summary>
    public Guid? ScriptId { get; private set; }

    public IReadOnlyList<QuotationLine> Lines => _lines;

    public Money Total => _lines.Aggregate(Money.Zero(),
        (sum, l) => sum + l.LineTotal);

    /// <param name="customerName">
    /// Who it is for, where they have said. Blank is allowed: a customer
    /// who asks what something costs has not given their name yet, and
    /// may never.
    /// </param>
    public static Quotation Open(string quotationNumber, string? customerName,
        Guid operatorId, DateOnly quotedOn, int validForDays,
        Guid? patientId = null,
        QuotationKind kind = QuotationKind.OverTheCounter)
    {
        if (validForDays < 1)
            throw new DomainException(
                "A quotation must be valid for at least a day.");

        return new Quotation
        {
            QuotationNumber = quotationNumber,
            CustomerName = (customerName ?? "").Trim(),
            OperatorId = operatorId,
            QuotedOn = quotedOn,
            ValidUntil = quotedOn.AddDays(validForDays),
            PatientId = patientId,
            Kind = kind
        };
    }

    /// <summary>
    /// Who the quote is for, given once the customer says.
    /// </summary>
    public void AddressTo(string? customerName)
    {
        if (Status == QuotationStatus.Accepted)
            throw new DomainException(
                "This quotation has already been taken up.");

        CustomerName = (customerName ?? "").Trim();
    }

    /// <param name="unitsInPack">
    /// How many of the quantity the priced pack holds. One at the till,
    /// where a pack is sold; the pack's tablets or millilitres at the
    /// dispensary, where the quantity is counted in them and the price is
    /// the pack's share, exactly as a dispensing prices it.
    /// </param>
    public QuotationLine AddLine(Guid productId, string description,
        decimal quantity, Money unitPrice, string taxCode,
        decimal unitsInPack = 1m)
    {
        EnsureOpen();

        if (quantity <= 0m)
            throw new DomainException(
                "A quotation line needs a quantity above zero.");

        var line = QuotationLine.Create(_lines.Count + 1, productId,
            description, quantity, unitPrice, taxCode,
            unitsInPack > 0m ? unitsInPack : 1m);

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

    /// <summary>
    /// Records the finalised sale the quotation became.
    /// </summary>
    public void AcceptInto(Guid saleId, DateOnly today)
    {
        if (SaleId is not null)
            throw new DomainException(
                $"Quotation {QuotationNumber} has already been sold.");

        if (saleId == Guid.Empty)
            throw new DomainException("That is not a sale.");

        Accept(today);
        SaleId = saleId;
    }

    /// <summary>
    /// Records the script a dispensary quote became: the customer went
    /// ahead, and the patient's details and the prescription were taken.
    /// </summary>
    /// <remarks>
    /// From an open quote as well as a saved one: a customer told the
    /// price who says yes on the spot has no reason to wait while it is
    /// saved first. A lapsed quote is refused, because its prices are no
    /// longer the pharmacy's offer.
    /// </remarks>
    public void ConvertToScript(Guid scriptId, Guid patientId, DateOnly today)
    {
        EnsureConvertibleToScript(today);

        ScriptId = scriptId;
        PatientId = patientId;
        Status = QuotationStatus.Accepted;
    }

    /// <summary>
    /// Refuses, with the reason, a quote that cannot become a script -
    /// asked before the script is written, so a refusal costs nothing.
    /// </summary>
    public void EnsureConvertibleToScript(DateOnly today)
    {
        if (Kind != QuotationKind.Dispensary)
            throw new DomainException(
                "Only a dispensary quote becomes a script.");

        if (ScriptId is not null || SaleId is not null)
            throw new DomainException(
                $"Quote {QuotationNumber} has already been taken up.");

        if (Status is not (QuotationStatus.Open or QuotationStatus.Issued))
            throw new DomainException(
                $"Quote {QuotationNumber} is {Status.ToString().ToLowerInvariant()}"
                + " and can no longer be taken up.");

        if (HasExpiredOn(today))
            throw new DomainException(
                $"This quote lapsed on {ValidUntil:dd/MM/yyyy}. "
                + "Quote again at today's prices.");

        if (_lines.Count == 0)
            throw new DomainException("A quote with nothing on it is not a script.");
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

    /// <summary>
    /// How many of the quantity the priced pack holds: one where the
    /// quantity is packs, the pack's units where it is tablets.
    /// </summary>
    public decimal UnitsInPack { get; private set; } = 1m;

    public Money LineTotal => UnitPrice * (Quantity / UnitsInPack);

    private QuotationLine() { }

    internal static QuotationLine Create(int lineNo, Guid productId,
        string description, decimal quantity, Money unitPrice,
        string taxCode, decimal unitsInPack = 1m) =>
        new()
        {
            LineNo = lineNo,
            ProductId = productId,
            Description = description,
            Quantity = quantity,
            UnitPrice = unitPrice,
            TaxCode = taxCode,
            UnitsInPack = unitsInPack
        };

    internal void Renumber(int lineNo) => LineNo = lineNo;
}
