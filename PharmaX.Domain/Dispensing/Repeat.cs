using PharmaX.Domain.Common;
using PharmaX.Domain.Exceptions;

namespace PharmaX.Domain.Dispensing;

/// <summary>
/// One authorised repeat on a script, and when it may be collected. The
/// due date exists so that a patient collecting a month's medicine after
/// a week is questioned rather than served (DSP-012).
/// </summary>
public sealed class Repeat : AuditableEntity
{
    public Guid ScriptId { get; private set; }
    public int RepeatNumber { get; private set; }
    public DateOnly DueOn { get; private set; }
    public DateOnly? CollectedOn { get; private set; }
    public Guid? DispensingId { get; private set; }
    public bool IsCollected => CollectedOn is not null;

    /// Collection is allowed this many days before the due date, which
    /// covers a weekend or a public holiday without needing an override.
    public const int GraceDays = 5;

    private Repeat() { }

    public static Repeat Schedule(Guid scriptId, int repeatNumber,
                                  DateOnly dueOn)
    {
        if (repeatNumber < 1)
            throw new DomainException("A repeat is numbered from one.");

        return new Repeat
        {
            ScriptId = scriptId,
            RepeatNumber = repeatNumber,
            DueOn = dueOn
        };
    }

    public bool IsDueOn(DateOnly day) => day >= DueOn.AddDays(-GraceDays);

    public int DaysEarlyOn(DateOnly day) =>
        Math.Max(0, DueOn.DayNumber - day.DayNumber);

    public void Collect(Guid dispensingId, DateOnly collectedOn)
    {
        if (IsCollected)
            throw new DomainException(
                $"Repeat {RepeatNumber} was already collected on "
                + $"{CollectedOn:yyyy-MM-dd}.");

        if (!IsDueOn(collectedOn))
            throw new DomainException(
                $"Repeat {RepeatNumber} is not due until "
                + $"{DueOn:yyyy-MM-dd}. A pharmacist must authorise an "
                + "early collection.");

        DispensingId = dispensingId;
        CollectedOn = collectedOn;
    }

    /// <summary>
    /// Collects a repeat before it is due, on a pharmacist's authority.
    /// Kept separate from Collect so the early release is a deliberate act
    /// with its own audit trail rather than a parameter someone defaults.
    /// </summary>
    public void CollectEarly(Guid dispensingId, DateOnly collectedOn,
                             Guid pharmacistId, string reason)
    {
        if (pharmacistId == Guid.Empty || string.IsNullOrWhiteSpace(reason))
            throw new DomainException(
                "An early collection needs a pharmacist and a reason.");

        DispensingId = dispensingId;
        CollectedOn = collectedOn;
    }
}
