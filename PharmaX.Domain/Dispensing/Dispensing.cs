using PharmaX.Domain.Common;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Primitives;

namespace PharmaX.Domain.Dispensing;

public enum DispensingStatus { InProgress, Issued, Reversed }

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
        line.AssignTo(Id, _lines.Count + 1);
        _lines.Add(line);
        return line;
    }

    public ClinicalWarning RaiseWarning(ClinicalWarning warning)
    {
        ArgumentNullException.ThrowIfNull(warning);

        EnsureInProgress();
        warning.AssignTo(Id);
        _warnings.Add(warning);
        return warning;
    }

    public void AcknowledgeWarning(Guid warningId, Guid userId, string? reason)
    {
        var warning = _warnings.SingleOrDefault(w => w.Id == warningId)
                      ?? throw new DomainException("Warning not found.");

        warning.Acknowledge(userId, reason);
    }

    /// <summary>
    /// Issues the dispensing. Every precondition is checked here rather
    /// than in the presenter, so that a screen cannot issue by skipping a
    /// step it did not know about.
    /// </summary>
    public void Issue(Guid pharmacistId, string documentNumber,
                      DateTime nowUtc)
    {
        EnsureInProgress();

        if (_lines.Count == 0)
            throw new DomainException("Nothing to issue.");

        if (pharmacistId == Guid.Empty)
            throw new DomainException(
                "A pharmacist must authorise the issue (DSP-006).");

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
    /// Reverses an issued dispensing. The record stays: a reversal is a
    /// new fact about the dispensing, not the removal of an old one.
    /// </summary>
    public void Reverse(string reason)
    {
        if (Status != DispensingStatus.Issued)
            throw new DomainException("Only an issued dispensing can be reversed.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("A reversal requires a reason.");

        Status = DispensingStatus.Reversed;
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
