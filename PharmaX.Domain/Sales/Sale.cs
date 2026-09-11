using PharmaX.Domain.Common;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Primitives;

namespace PharmaX.Domain.Sales;

public enum SaleStatus { Open, Finalised, Voided }

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
    public SaleStatus Status { get; private set; } = SaleStatus.Open;
    public bool IsRated => _lines.Exists(l => l.IsRated);

    public IReadOnlyList<SaleLine> Lines => _lines;
    public IReadOnlyList<SalePayment> Payments => _payments;

    public Money Subtotal => _lines.Aggregate(
        Money.Zero(), (sum, l) => sum + l.GrossBeforeDiscount);

    public Money DiscountTotal => _lines.Aggregate(
        Money.Zero(), (sum, l) => sum + l.DiscountAmount);

    public Money Total => _lines.Aggregate(
        Money.Zero(), (sum, l) => sum + l.LineTotal);

    public Money Tendered => _payments.Aggregate(
        Money.Zero(), (sum, p) => sum + p.Amount);

    public Money Change
    {
        get
        {
            var over = Tendered - Total;
            return over.IsNegative ? Money.Zero() : over;
        }
    }

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

        if (Tendered != Total)
            throw new DomainException(
                $"Tendered {Tendered} does not equal total {Total} " +
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
        EnsureOpen();

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
