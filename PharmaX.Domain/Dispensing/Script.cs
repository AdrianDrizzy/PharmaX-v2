using PharmaX.Domain.Common;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Primitives;

namespace PharmaX.Domain.Dispensing;

/// <remarks>
/// Deleted is an administrator taking the script off every list. The row
/// stays, so the dispensings, sales and claims made against it keep the
/// script they name; stored as text, so it needs no migration.
/// </remarks>
public enum ScriptStatus { Captured, PartiallyDispensed, Completed, Cancelled, Deleted }

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
        int repeatsAuthorised = 0, DateOnly? validUntil = null,
        string? prescriberNumber = null)
    {
        if (patientId == Guid.Empty)
            throw new DomainException("A script must belong to a patient.");

        if (Refuse(prescriberName, prescribedOn, repeatsAuthorised,
                validUntil) is string problem)
            throw new DomainException(problem);

        return new Script
        {
            ScriptNumber = scriptNumber,
            PatientId = patientId,
            PrescriberName = prescriberName.Trim(),

            // Taken on the capture screen and then dropped: the number
            // never reached the record it was typed for.
            PrescriberNumber = Blank(prescriberNumber),
            PrescribedOn = prescribedOn,
            RepeatsAuthorised = repeatsAuthorised,

            // Six months is the default validity where the prescriber has
            // not written one (specification Section 9.2).
            ValidUntil = validUntil ?? prescribedOn.AddMonths(6)
        };
    }

    /// <summary>
    /// Why these particulars cannot make a script, or null where they
    /// can. Public so a service can ask before it spends a script number
    /// on an attempt that will be refused (ADM-DEF-13).
    /// </summary>
    public static string? Refuse(string? prescriberName, DateOnly prescribedOn,
        int repeatsAuthorised, DateOnly? validUntil) =>
        string.IsNullOrWhiteSpace(prescriberName)
            ? "A script requires a prescriber."
            : repeatsAuthorised < 0
                ? "Repeats authorised cannot be negative."
                : validUntil < prescribedOn
                    ? "A script cannot run out before it was written."
                    : null;

    /// <summary>
    /// Whether the script may still be corrected: captured, and nothing
    /// yet dispensed against it.
    /// </summary>
    public bool IsEditable =>
        Status == ScriptStatus.Captured && RepeatsUsed == 0;

    /// <summary>
    /// Corrects what was typed when the script was captured.
    /// </summary>
    /// <remarks>
    /// Only before anything is dispensed. After that the script is the
    /// record of what the patient was given against, and a typo is
    /// corrected by cancelling it and capturing it again. Before it, a
    /// wrong quantity meant exactly that anyway, under a new number
    /// (ADM-OBS-13).
    /// </remarks>
    public void Amend(string prescriberName, string? prescriberNumber,
        DateOnly prescribedOn, int repeatsAuthorised, DateOnly? validUntil,
        IEnumerable<ScriptItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (!IsEditable)
            throw new DomainException(
                $"Script {ScriptNumber} has been dispensed against or "
                + "cancelled, so it can no longer be changed.");

        if (Refuse(prescriberName, prescribedOn, repeatsAuthorised,
                validUntil) is string problem)
            throw new DomainException(problem);

        var replacement = items.ToList();

        if (replacement.Count == 0)
            throw new DomainException("A script with no items is not a script.");

        PrescriberName = prescriberName.Trim();
        PrescriberNumber = Blank(prescriberNumber);
        PrescribedOn = prescribedOn;
        RepeatsAuthorised = repeatsAuthorised;
        ValidUntil = validUntil ?? prescribedOn.AddMonths(6);

        _items.Clear();

        foreach (var item in replacement)
        {
            item.AssignTo(Id, _items.Count + 1);
            _items.Add(item);
        }
    }

    /// <summary>
    /// Gives back the repeat a reversed dispensing used.
    /// </summary>
    public void UndoDispensing()
    {
        if (RepeatsUsed == 0)
            throw new DomainException(
                $"Script {ScriptNumber} has nothing dispensed to undo.");

        RepeatsUsed--;

        if (Status is not (ScriptStatus.Cancelled or ScriptStatus.Deleted))
            Status = RepeatsUsed == 0
                ? ScriptStatus.Captured
                : ScriptStatus.PartiallyDispensed;
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public ScriptItem AddItem(ScriptItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (Status is ScriptStatus.Completed or ScriptStatus.Cancelled
            or ScriptStatus.Deleted)
            throw new DomainException(
                "A completed or cancelled script cannot take new items.");

        item.AssignTo(Id, _items.Count + 1);
        _items.Add(item);
        return item;
    }

    public bool IsValidOn(DateOnly day) =>
        Status is not (ScriptStatus.Cancelled or ScriptStatus.Deleted)
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

    /// <summary>
    /// Moves this script to another patient file.
    /// </summary>
    /// <remarks>
    /// Only for merging two files that turned out to be the same person.
    /// A script is evidence of what was prescribed to whom, so this is
    /// not a correction tool: the merge service is the one caller, and
    /// it writes an audit entry naming both files.
    /// </remarks>
    public void ReassignTo(Guid patientId)
    {
        if (patientId == Guid.Empty)
            throw new DomainException(
                "A script cannot be moved to no patient at all.");

        PatientId = patientId;
    }

    public void Cancel(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("Cancelling a script requires a reason.");

        if (Status == ScriptStatus.Deleted)
            throw new DomainException($"Script {ScriptNumber} has been deleted.");

        Status = ScriptStatus.Cancelled;
    }

    /// <summary>
    /// Takes the script off every list, in whatever state it is in.
    /// </summary>
    public void Delete(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("Deleting a script requires a reason.");

        Status = ScriptStatus.Deleted;
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

    /// <summary>
    /// The pack this line is priced by - how many units it holds - where
    /// it was set for this script rather than taken from the product.
    /// Null is the product's own.
    /// </summary>
    public decimal? PackUnits { get; private set; }

    /// <summary>
    /// The price of that pack for this script, where it was set rather
    /// than taken from the shelf. Null is the shelf price. The patient
    /// pays this price for every PackUnits handed over; the catalogue
    /// is not changed.
    /// </summary>
    public Money? PackPrice { get; private set; }

    private ScriptItem() { }

    public static ScriptItem Create(Guid productId, string description,
        decimal quantity, string directions, bool allowSubstitution = true,
        decimal? packUnits = null, Money? packPrice = null)
    {
        if (quantity <= 0m)
            throw new DomainException("Quantity must be greater than zero.");

        if (string.IsNullOrWhiteSpace(directions))
            throw new DomainException(
                "Directions are mandatory: a label without them cannot be "
                + "dispensed (DSP-004).");

        if (packUnits is decimal units && units <= 0m)
            throw new DomainException(
                "The pack must hold more than nothing, or be left blank.");

        if (packPrice is Money price && price.Amount < 0m)
            throw new DomainException("A price cannot be less than nothing.");

        return new ScriptItem
        {
            ProductId = productId,
            Description = description,
            Quantity = quantity,
            Directions = directions,
            AllowSubstitution = allowSubstitution,
            PackUnits = packUnits,
            PackPrice = packPrice
        };
    }

    internal void AssignTo(Guid scriptId, int lineNo)
    {
        ScriptId = scriptId;
        LineNo = lineNo;
    }
}
