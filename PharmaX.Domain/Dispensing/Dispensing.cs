using PharmaX.Domain.Common;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Primitives;

namespace PharmaX.Domain.Dispensing;

/// <remarks>
/// Abandoned is a dispensing begun and left without being issued: the
/// screen was closed on it. It used to stay InProgress for ever
/// (ADM-OBS-14).
/// </remarks>
public enum DispensingStatus { InProgress, Issued, Reversed, Abandoned }

/// <summary>
/// One act of handing medicine over against a script. Its central rule is
/// that nothing is issued until every clinical warning has been dealt with
/// and a pharmacist has authenticated at the moment of issue (DSP-006).
/// </summary>
public sealed class Dispensing : AuditableEntity
{
    private readonly List<DispensingLine> _lines = new();
    private readonly List<ClinicalWarning> _warnings = new();

    public Guid ScriptId { get; private set; }
    public Guid PatientId { get; private set; }
    public string DocumentNumber { get; private set; } = "";
    public int RepeatNumber { get; private set; }
    public Guid CapturedBy { get; private set; }
    public Guid? PharmacistId { get; private set; }
    public DateTime? IssuedAtUtc { get; private set; }

    /// <summary>
    /// The till sale that collected the patient's co-payment, once one
    /// has. Null while the medicine is dispensed but not yet paid for.
    /// </summary>
    /// <remarks>
    /// The join between the dispensary and the till. A pharmacist issues
    /// and the patient walks to a cashier, so for a few minutes the
    /// dispensing is real, the stock has gone, and no money has been
    /// taken. That interval is what the collection queue shows, and it
    /// is why this is nullable rather than a status: the dispensing is
    /// issued either way, and what changes is whether it has been paid.
    /// </remarks>
    public Guid? SaleId { get; private set; }

    public DateTime? CollectedAtUtc { get; private set; }

    /// Why an issued dispensing was taken back, and by whom and when.
    public string? ReversalReason { get; private set; }
    public Guid? ReversedBy { get; private set; }
    public DateTime? ReversedAtUtc { get; private set; }
    public DispensingStatus Status { get; private set; } =
        DispensingStatus.InProgress;

    public IReadOnlyList<DispensingLine> Lines => _lines;
    public IReadOnlyList<ClinicalWarning> Warnings => _warnings;

    public Money TariffTotal => _lines.Aggregate(
        Money.Zero(), (sum, l) => sum + l.TariffAmount);

    public Money ContributionTotal => _lines.Aggregate(
        Money.Zero(), (sum, l) => sum + l.SchemeContribution);

    /// What the patient pays at the till. The sale takes this figure.
    public Money CoPaymentTotal => _lines.Aggregate(
        Money.Zero(), (sum, l) => sum + l.CoPayment);

    /// <summary>
    /// Issued, and still waiting for a till to take the co-payment.
    /// </summary>
    /// <remarks>
    /// A dispensing with nothing to pay is not waiting for anything -
    /// a fully covered medical aid script is finished the moment it is
    /// issued, and putting it in a queue for a cashier to collect
    /// nothing would only teach the cashiers to ignore the queue.
    /// </remarks>
    public bool AwaitsCollection =>
        Status == DispensingStatus.Issued
        && SaleId is null
        && !CoPaymentTotal.IsZero;

    public IReadOnlyList<ClinicalWarning> PendingWarnings =>
        _warnings.Where(w => !w.IsAcknowledged).ToList();

    private Dispensing() { }

    public static Dispensing Begin(Guid scriptId, Guid patientId,
                                   Guid capturedBy, int repeatNumber)
    {
        if (scriptId == Guid.Empty)
            throw new DomainException("A dispensing needs a script.");

        return new Dispensing
        {
            ScriptId = scriptId,
            PatientId = patientId,
            CapturedBy = capturedBy,
            RepeatNumber = repeatNumber
        };
    }

    public DispensingLine AddLine(DispensingLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        EnsureInProgress();

        // After the highest number in use rather than the count, so an
        // item removed from the middle does not hand its successor's
        // number to the next one added.
        line.AssignTo(Id, _lines.Count == 0 ? 1 : _lines.Max(l => l.LineNo) + 1);
        _lines.Add(line);
        return line;
    }

    /// <summary>
    /// Takes an item back off before the issue, with whatever it raised.
    /// </summary>
    /// <remarks>
    /// The others keep their numbers. Renumbering rows under a unique
    /// index is a swap the database may refuse half-way through, and a
    /// gap in the item numbers of an unissued dispensing harms nobody.
    /// </remarks>
    public void RemoveLine(int lineNo)
    {
        EnsureInProgress();

        var line = _lines.SingleOrDefault(l => l.LineNo == lineNo)
                   ?? throw new DomainException($"Item {lineNo} not found.");

        _lines.Remove(line);
        _warnings.RemoveAll(w => w.DispensingLineId == line.Id);
    }

    /// Part of an item the shelf is short of, owed to the patient.
    public void Owe(int lineNo, decimal owed)
    {
        EnsureInProgress();

        (_lines.SingleOrDefault(l => l.LineNo == lineNo)
         ?? throw new DomainException($"Item {lineNo} not found.")).Owe(owed);
    }

    public ClinicalWarning RaiseWarning(ClinicalWarning warning)
    {
        ArgumentNullException.ThrowIfNull(warning);

        EnsureInProgress();
        warning.AssignTo(Id);
        _warnings.Add(warning);
        return warning;
    }

    public void AcknowledgeWarning(Guid warningId, Guid userId, string? reason,
                                   Guid? authorisedBy = null)
    {
        EnsureInProgress();

        var warning = _warnings.SingleOrDefault(w => w.Id == warningId)
                      ?? throw new DomainException("Warning not found.");

        warning.Acknowledge(userId, reason, authorisedBy);
    }

    /// <summary>
    /// Issues the dispensing. Every precondition is checked here rather
    /// than in the presenter, so that a screen cannot issue by skipping a
    /// step it did not know about.
    /// </summary>
    /// <param name="pharmacistId">
    /// Who is responsible for the issue: the operator, where they may
    /// issue on their own authority, or the pharmacist who authorised it.
    /// </param>
    public void Issue(Guid pharmacistId, string documentNumber,
                      DateTime nowUtc)
    {
        EnsureInProgress();

        if (_lines.Count == 0)
            throw new DomainException("Nothing to issue.");

        if (pharmacistId == Guid.Empty)
            throw new DomainException(
                "Somebody must be responsible for the issue (DSP-006).");

        var unacknowledged = _warnings.Where(w => !w.IsAcknowledged).ToList();

        if (unacknowledged.Count > 0)
            throw new DomainException(
                $"{unacknowledged.Count} clinical warning(s) have not been "
                + "acknowledged.");

        PharmacistId = pharmacistId;
        DocumentNumber = documentNumber;
        IssuedAtUtc = nowUtc;
        Status = DispensingStatus.Issued;
    }

    /// <summary>
    /// Records the till sale that collected the patient's co-payment.
    /// </summary>
    public void RecordCollection(Guid saleId, DateTime nowUtc)
    {
        if (Status != DispensingStatus.Issued)
            throw new DomainException(
                "Only an issued dispensing can be collected.");

        if (SaleId is not null)
            throw new DomainException(
                $"{DocumentNumber} has already been paid for.");

        if (saleId == Guid.Empty)
            throw new DomainException("A collection needs a sale.");

        SaleId = saleId;
        CollectedAtUtc = nowUtc;
    }

    /// <summary>
    /// Reverses an issued dispensing. The record stays: a reversal is a
    /// new fact about the dispensing, not the removal of an old one.
    /// </summary>
    public void Reverse(string reason) =>
        Reverse(reason, Guid.Empty, DateTime.UtcNow);

    public void Reverse(string reason, Guid reversedBy, DateTime nowUtc)
    {
        if (Status != DispensingStatus.Issued)
            throw new DomainException("Only an issued dispensing can be reversed.");

        // Money has changed hands. Reversing the dispensing alone would
        // put the stock back and leave the patient paid up for nothing;
        // the sale has to be refunded through returns first, which is
        // the path that actually gives the money back.
        if (SaleId is not null)
            throw new DomainException(
                $"{DocumentNumber} has been paid for. Refund the sale "
                + "through returns before reversing it.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("A reversal requires a reason.");

        Status = DispensingStatus.Reversed;
        ReversalReason = reason.Trim();
        ReversedBy = reversedBy == Guid.Empty ? null : reversedBy;
        ReversedAtUtc = nowUtc;
    }

    /// <summary>
    /// Sets aside a dispensing that was begun and never issued. Nothing
    /// left the shelf, so nothing is put back.
    /// </summary>
    public void Abandon()
    {
        EnsureInProgress();
        Status = DispensingStatus.Abandoned;
    }

    private void EnsureInProgress()
    {
        if (Status != DispensingStatus.InProgress)
            throw new DomainException(
                "This dispensing has been issued and cannot be changed.");
    }

    /// <summary>
    /// Moves this dispensing record to another patient file, as part of
    /// merging two files for the same person. See Script.ReassignTo.
    /// </summary>
    public void ReassignTo(Guid patientId)
    {
        if (patientId == Guid.Empty)
            throw new DomainException(
                "A dispensing cannot be moved to no patient at all.");

        PatientId = patientId;
    }
}
