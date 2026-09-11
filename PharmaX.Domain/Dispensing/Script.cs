using PharmaX.Domain.Common;
using PharmaX.Domain.Exceptions;

namespace PharmaX.Domain.Dispensing;

public enum ScriptStatus { Captured, PartiallyDispensed, Completed, Cancelled }

/// <summary>
/// The prescription as written by the prescriber. Distinct from a
/// Dispensing, which is one act of handing medicine over against it: one
/// script with five repeats produces six dispensings over six months.
/// </summary>
public sealed class Script : AuditableEntity
{
    private readonly List<ScriptItem> _items = new();

    public string ScriptNumber { get; private set; } = "";
    public Guid PatientId { get; private set; }
    public string PrescriberName { get; private set; } = "";
    public string? PrescriberNumber { get; private set; }
    public DateOnly PrescribedOn { get; private set; }
    public DateOnly? ValidUntil { get; private set; }
    public int RepeatsAuthorised { get; private set; }
    public int RepeatsUsed { get; private set; }
    public ScriptStatus Status { get; private set; } = ScriptStatus.Captured;

    public IReadOnlyList<ScriptItem> Items => _items;

    public int RepeatsRemaining => RepeatsAuthorised - RepeatsUsed;

    private Script() { }

    public static Script Capture(string scriptNumber, Guid patientId,
        string prescriberName, DateOnly prescribedOn,
        int repeatsAuthorised = 0, DateOnly? validUntil = null)
    {
        if (patientId == Guid.Empty)
            throw new DomainException("A script must belong to a patient.");

        if (string.IsNullOrWhiteSpace(prescriberName))
            throw new DomainException("A script requires a prescriber.");

        if (repeatsAuthorised < 0)
            throw new DomainException("Repeats authorised cannot be negative.");

        return new Script
        {
            ScriptNumber = scriptNumber,
            PatientId = patientId,
            PrescriberName = prescriberName,
            PrescribedOn = prescribedOn,
            RepeatsAuthorised = repeatsAuthorised,

            // Six months is the default validity where the prescriber has
            // not written one (specification Section 9.2).
            ValidUntil = validUntil ?? prescribedOn.AddMonths(6)
        };
    }

    public ScriptItem AddItem(ScriptItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (Status is ScriptStatus.Completed or ScriptStatus.Cancelled)
            throw new DomainException(
                "A completed or cancelled script cannot take new items.");

        item.AssignTo(Id, _items.Count + 1);
        _items.Add(item);
        return item;
    }

    public bool IsValidOn(DateOnly day) =>
        Status != ScriptStatus.Cancelled
        && (ValidUntil is null || ValidUntil >= day);

    /// <summary>
    /// Records that one dispensing has been made against this script.
    /// Refuses past the authorised repeat count, which is the rule that
    /// stops a chronic script running indefinitely (DSP-011).
    /// </summary>
    public void RecordDispensing(DateOnly today)
    {
        if (!IsValidOn(today))
            throw new DomainException(
                $"Script {ScriptNumber} expired on {ValidUntil:yyyy-MM-dd}.");

        if (RepeatsUsed > RepeatsAuthorised)
            throw new DomainException(
                $"Script {ScriptNumber} has no repeats left.");

        RepeatsUsed++;

        Status = RepeatsUsed > RepeatsAuthorised
            ? ScriptStatus.Completed
            : ScriptStatus.PartiallyDispensed;
    }

    public void Cancel(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("Cancelling a script requires a reason.");

        Status = ScriptStatus.Cancelled;
    }
}

/// <summary>
/// One medicine on the script, as prescribed. What was actually handed
/// over is a DispensingLine, which may differ: a generic substitution
/// changes the product but not the prescription.
/// </summary>
public sealed class ScriptItem : Entity
{
    public Guid ScriptId { get; private set; }
    public int LineNo { get; private set; }
    public Guid ProductId { get; private set; }
    public string Description { get; private set; } = "";
    public decimal Quantity { get; private set; }
    public string Directions { get; private set; } = "";
    public bool AllowSubstitution { get; private set; } = true;

    private ScriptItem() { }

    public static ScriptItem Create(Guid productId, string description,
        decimal quantity, string directions, bool allowSubstitution = true)
    {
        if (quantity <= 0m)
            throw new DomainException("Quantity must be greater than zero.");

        if (string.IsNullOrWhiteSpace(directions))
            throw new DomainException(
                "Directions are mandatory: a label without them cannot be "
                + "dispensed (DSP-004).");

        return new ScriptItem
        {
            ProductId = productId,
            Description = description,
            Quantity = quantity,
            Directions = directions,
            AllowSubstitution = allowSubstitution
        };
    }

    internal void AssignTo(Guid scriptId, int lineNo)
    {
        ScriptId = scriptId;
        LineNo = lineNo;
    }
}
