using PharmaX.Domain.Common;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Primitives;

namespace PharmaX.Domain.Sales;

/// <summary>
/// Where a sale is in its life. Held is an open basket parked for later:
/// the customer went back for something they forgot, and the till served
/// the next person in the meantime.
/// </summary>
public enum SaleStatus { Open, Finalised, Voided, Held }

public sealed class Sale : AuditableEntity
{
    private readonly List<SaleLine> _lines = new();
    private readonly List<SalePayment> _payments = new();

    private string? _documentNumber;

    public Guid BranchId { get; private set; }
    public Guid WorkstationId { get; private set; }
    public Guid TillSessionId { get; private set; }
    public Guid OperatorId { get; private set; }
    public Guid? PatientId { get; private set; }
    public Guid? AccountId { get; private set; }
    /// <summary>
    /// The receipt number, once the sale has been finalised. Empty
    /// until then, because an abandoned draft must not consume a number
    /// out of the sequence an auditor will later read.
    /// </summary>
    /// <remarks>
    /// Backed by a nullable field on purpose. The database holds NULL
    /// rather than an empty string for a draft, so that the unique
    /// index over workstation and document number constrains issued
    /// receipts only. Over empty strings it constrained the drafts too,
    /// and the second unfinished sale a till ever opened was refused.
    /// </remarks>
    public string DocumentNumber
    {
        get => _documentNumber ?? "";
        private set => _documentNumber =
            string.IsNullOrEmpty(value) ? null : value;
    }

    public DateTime OccurredAtUtc { get; private set; }

    /// <summary>
    /// The dispensing this sale is collecting payment for, where it is
    /// one rather than an ordinary over-the-counter sale.
    /// </summary>
    /// <remarks>
    /// Load-bearing, not decoration. Stock left the shelf when the
    /// pharmacist issued the dispensing, so a collection must not take
    /// it again - StockService reads this and leaves the shelf alone.
    /// It is also what ties the receipt back to the script when a
    /// patient asks a month later what they paid for.
    /// </remarks>
    public Guid? DispensingId { get; private set; }

    /// <summary>
    /// The quotation this sale was converted from, where it was. Set when
    /// the quotation is recalled at the till; the quotation is marked
    /// accepted only when this sale is finalised, so an abandoned basket
    /// does not use the quotation up.
    /// </summary>
    public Guid? QuotationId { get; private set; }

    /// <summary>
    /// The change handed back from a cash tender, once the sale is paid.
    /// </summary>
    /// <remarks>
    /// Stored because it cannot be derived afterwards: the payments are
    /// kept at the amounts that actually paid for the sale - which is
    /// what the drawer, the cash-up and the journal must add up to - so
    /// "the customer gave 100 for an 80 sale" survives only here and on
    /// the receipt.
    /// </remarks>
    public Money ChangeGiven { get; private set; } = Money.Zero();

    /// True where this sale is collecting a dispensing's co-payment.
    public bool IsCollection => DispensingId is not null;
    public SaleStatus Status { get; private set; } = SaleStatus.Open;
    public bool IsRated => _lines.Exists(l => l.IsRated);

    public IReadOnlyList<SaleLine> Lines => _lines;
    public IReadOnlyList<SalePayment> Payments => _payments;

    /// <summary>
    /// Before discount, over the lines still on the sale.
    /// </summary>
    /// <remarks>
    /// A voided line counted here and in the discount while the total
    /// left it out, so a discounted sale with a voided line printed a
    /// subtotal and discount that did not come to its own total (DEF-0016).
    /// </remarks>
    public Money Subtotal => _lines.Where(l => !l.IsVoided).Aggregate(
        Money.Zero(), (sum, l) => sum + l.GrossBeforeDiscount);

    public Money DiscountTotal => _lines.Where(l => !l.IsVoided).Aggregate(
        Money.Zero(), (sum, l) => sum + l.DiscountAmount);

    public Money Total => _lines.Aggregate(
        Money.Zero(), (sum, l) => sum + l.LineTotal);

    /// What the payments put towards the sale, change already given back.
    public Money AmountPaid => _payments.Aggregate(
        Money.Zero(), (sum, p) => sum + p.Amount);

    /// What the customer handed over.
    public Money Tendered => AmountPaid + ChangeGiven;

    public Money Change => ChangeGiven;

    private Sale() { }

    public static Sale Open(Guid branchId, Guid workstationId,
                            Guid tillSessionId, Guid operatorId,
                            DateTime occurredAtUtc)
    {
        if (tillSessionId == Guid.Empty)
            throw new DomainException(
                "A sale requires an open till session (POS-013).");

        return new Sale
        {
            BranchId = branchId,
            WorkstationId = workstationId,
            TillSessionId = tillSessionId,
            OperatorId = operatorId,
            OccurredAtUtc = occurredAtUtc
        };
    }

    /// <summary>
    /// Marks this sale as collecting a dispensing's co-payment.
    /// </summary>
    public void CollectFor(Guid dispensingId)
    {
        if (Status != SaleStatus.Open)
            throw new DomainException(
                "Only an open sale can be tied to a dispensing.");

        if (_lines.Count > 0)
            throw new DomainException(
                "A collection cannot be mixed with over-the-counter "
                + "lines: the stock behind them is accounted for "
                + "differently.");

        if (dispensingId == Guid.Empty)
            throw new DomainException("That is not a dispensing.");

        DispensingId = dispensingId;
    }

    public SaleLine AddLine(SaleLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        EnsureOpen();
        line.AssignTo(Id, _lines.Count + 1);
        _lines.Add(line);
        return line;
    }

    public void VoidLine(int lineNo, string reason, Guid operatorId)
    {
        EnsureOpen();

        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("A voided line requires a reason.");

        var line = _lines.SingleOrDefault(l => l.LineNo == lineNo)
                   ?? throw new DomainException($"Line {lineNo} not found.");

        line.Void(reason, operatorId);
    }

    public void AddPayment(SalePayment payment)
    {
        EnsureOpen();
        _payments.Add(payment);
    }

    /// <summary>
    /// Takes the tenders the customer handed over, giving change from
    /// the cash where there is more than the sale comes to.
    /// </summary>
    /// <remarks>
    /// The till used to refuse anything over the exact amount, so it
    /// could never work out change: a 60.00 note for a 53.00 sale was
    /// "over-tendered" and the cashier had to key 53.00 and count the
    /// change in their head (DEF-0019).
    ///
    /// Only cash gives change. A card is charged what the sale comes to
    /// and an account is debited what was bought, so an excess on either
    /// is a keying error, not change. The cash payments are reduced by
    /// the change so that what is stored is what stays in the drawer.
    /// </remarks>
    public void TakePayments(IEnumerable<SalePayment> tendered)
    {
        ArgumentNullException.ThrowIfNull(tendered);
        EnsureOpen();

        var payments = tendered.ToList();
        var offered = payments.Aggregate(Money.Zero(),
            (sum, p) => sum + p.Amount) + AmountPaid;

        var over = offered - Total;

        if (!over.IsNegative && !over.IsZero)
        {
            var cash = payments
                .Where(p => p.Tender == TenderType.Cash)
                .Aggregate(Money.Zero(), (sum, p) => sum + p.Amount);

            if (over.Amount > cash.Amount)
                throw new DomainException(
                    $"{over} more than the sale was tendered, and only cash "
                    + "can give change. Reduce the card or account amount.");

            // Out of the last cash tender first, which is the note the
            // cashier is holding when they count the change.
            var remaining = over;

            foreach (var payment in payments
                         .Where(p => p.Tender == TenderType.Cash)
                         .Reverse())
            {
                if (remaining.IsZero) break;

                var take = payment.Amount.Amount < remaining.Amount
                    ? payment.Amount
                    : remaining;

                payment.Reduce(take);
                remaining -= take;
            }

            ChangeGiven = over;
        }

        foreach (var payment in payments.Where(p => !p.Amount.IsZero))
            _payments.Add(payment);
    }

    /// <summary>
    /// Parks an open sale so the till can serve someone else, with its
    /// lines, customer and discounts kept exactly as they are.
    /// </summary>
    public void Hold()
    {
        EnsureOpen();

        if (!_lines.Exists(l => !l.IsVoided))
            throw new DomainException(
                "There is nothing on this sale to hold.");

        if (IsCollection)
            throw new DomainException(
                "A script collection is not held: it stays in the "
                + "collection queue until it is paid.");

        Status = SaleStatus.Held;
    }

    /// <summary>
    /// Takes a held sale back up at the till, by whoever is serving now.
    /// </summary>
    public void Resume(Guid tillSessionId, Guid operatorId)
    {
        if (Status != SaleStatus.Held)
            throw new DomainException("Only a held sale can be recalled.");

        if (tillSessionId == Guid.Empty)
            throw new DomainException(
                "A sale requires an open till session (POS-013).");

        TillSessionId = tillSessionId;
        OperatorId = operatorId;
        Status = SaleStatus.Open;
    }

    /// <summary>
    /// Marks this sale as the conversion of a quotation.
    /// </summary>
    public void ConvertFrom(Guid quotationId)
    {
        EnsureOpen();

        if (quotationId == Guid.Empty)
            throw new DomainException("That is not a quotation.");

        if (IsCollection)
            throw new DomainException(
                "A script collection cannot take a quotation's lines.");

        QuotationId = quotationId;
    }

    /// <summary>
    /// Attaches the sale to a patient, which is what makes an account
    /// tender and a medical aid tariff available on subsequent lines.
    /// </summary>
    public void AssignPatient(Guid patientId, Guid? accountId)
    {
        EnsureOpen();
        PatientId = patientId;
        AccountId = accountId;
    }

    public void Finalise(string documentNumber, DateTime nowUtc)
    {
        EnsureOpen();

        if (_lines.Count == 0)
            throw new DomainException("A sale must have at least one line.");

        if (AmountPaid != Total)
            throw new DomainException(
                $"Tendered {AmountPaid} does not equal total {Total} " +
                "(BR-POS-01).");

        foreach (var line in _lines.Where(l => !l.IsVoided))
            line.EnsureSaleable(nowUtc);

        DocumentNumber = documentNumber;
        Status = SaleStatus.Finalised;
    }

    /// <summary>
    /// Abandons an open sale. A finalised sale is never voided this way:
    /// it is reversed by a return or a credit note (BR-POS-07).
    /// </summary>
    public void Abandon(string reason)
    {
        // A held basket the customer never came back for is abandoned the
        // same way as an open one.
        if (Status != SaleStatus.Held) EnsureOpen();

        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("An abandoned sale requires a reason.");

        Status = SaleStatus.Voided;
    }

    private void EnsureOpen()
    {
        if (Status != SaleStatus.Open)
            throw new DomainException(
                "A finalised sale cannot be changed. Use a return " +
                "or a credit note (BR-POS-07).");
    }
}
