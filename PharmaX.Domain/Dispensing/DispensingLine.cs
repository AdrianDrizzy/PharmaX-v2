using PharmaX.Domain.Common;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Primitives;

namespace PharmaX.Domain.Dispensing;

/// <summary>
/// One medicine actually handed over, with the batch it came from and the
/// directions that will be printed on its label.
/// </summary>
public sealed class DispensingLine : Entity
{
    private readonly List<DispensingLineBatch> _batches = new();

    public Guid DispensingId { get; private set; }
    public int LineNo { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid? ScriptItemId { get; private set; }
    public Guid? BatchId { get; private set; }
    public string? BatchNumber { get; private set; }
    public DateOnly? BatchExpiry { get; private set; }
    public string Description { get; private set; } = "";
    public decimal Quantity { get; private set; }
    public string Directions { get; private set; } = "";

    /// What the shelf was short of: charged and claimed with the rest,
    /// handed over when the stock comes in. Null when nothing is owed.
    public decimal? Owed { get; private set; }
    public DateTime? OwedGivenAtUtc { get; private set; }
    public bool IsSubstitution { get; private set; }
    public Guid? SubstitutedForProductId { get; private set; }

    public Money TariffAmount { get; private set; } = Money.Zero();
    public Money SchemeContribution { get; private set; } = Money.Zero();

    /// <summary>
    /// Where the quantity comes from, batch by batch, earliest expiry
    /// first.
    /// </summary>
    /// <remarks>
    /// A line used to carry one batch. Twenty-one capsules against a
    /// first batch holding four was accepted when it was added and then
    /// refused at issue - "batch holds 4, cannot issue 21" - so the
    /// pharmacist had to cancel and dispense a smaller quantity than was
    /// prescribed. The quantity is now spread across as many batches as
    /// it takes, and BatchId, BatchNumber and BatchExpiry above describe
    /// the first of them: the earliest expiry, which is the one the label
    /// has to show.
    /// </remarks>
    public IReadOnlyList<DispensingLineBatch> Batches => _batches;

    /// <summary>
    /// The batches as the label prints them: "AMOX50-A x4, AMOX50-B x17".
    /// </summary>
    public string? BatchSummary => _batches.Count switch
    {
        0 => BatchNumber,
        1 => _batches[0].BatchNumber,
        // Earliest expiry first, however the rows came back from the
        // database: a reprint must read the same as the first label.
        _ => string.Join(", ", _batches.OrderBy(b => b.ExpiryDate)
            .ThenBy(b => b.BatchNumber, StringComparer.Ordinal).Select(b =>
            string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"{b.BatchNumber} x{b.Quantity:0.###}")))
    };

    /// What the patient pays. Never negative: a scheme paying more than
    /// the tariff does not put money in the patient's pocket.
    public Money CoPayment
    {
        get
        {
            var difference = TariffAmount - SchemeContribution;
            return difference.IsNegative ? Money.Zero() : difference;
        }
    }

    private DispensingLine() { }

    public static DispensingLine Create(Guid productId, string description,
        decimal quantity, string directions, Money tariffAmount,
        Guid? scriptItemId = null, Guid? batchId = null,
        string? batchNumber = null, DateOnly? batchExpiry = null)
    {
        if (quantity <= 0m)
            throw new DomainException("Quantity must be greater than zero.");

        if (string.IsNullOrWhiteSpace(directions))
            throw new DomainException(
                "A dispensing line cannot be labelled without directions "
                + "(DSP-004).");

        return new DispensingLine
        {
            ProductId = productId,
            Description = description,
            Quantity = quantity,
            Directions = directions,
            TariffAmount = tariffAmount,
            ScriptItemId = scriptItemId,
            BatchId = batchId,
            BatchNumber = batchNumber,
            BatchExpiry = batchExpiry
        };
    }

    internal void AssignTo(Guid dispensingId, int lineNo)
    {
        DispensingId = dispensingId;
        LineNo = lineNo;
    }

    /// <summary>
    /// Records which batches the quantity is taken from, replacing any
    /// earlier allocation.
    /// </summary>
    /// <remarks>
    /// Called when the item is added, so the pharmacist sees the batches
    /// on the screen, and again inside the issue transaction against the
    /// stock as it is at that moment - another till may have sold from
    /// the same batch in between, and the issue must move what is really
    /// there.
    /// </remarks>
    public void AllocateBatches(IEnumerable<BatchAllocation> allocations)
    {
        ArgumentNullException.ThrowIfNull(allocations);

        var ordered = allocations
            .OrderBy(a => a.Expiry)
            .ThenBy(a => a.BatchNumber, StringComparer.Ordinal)
            .ToList();

        if (ordered.Count == 0)
            throw new DomainException(
                $"{Description}: no batch to dispense from.");

        if (ordered.Exists(a => a.Quantity <= 0m))
            throw new DomainException(
                "A batch allocation must be greater than zero.");

        var total = ordered.Sum(a => a.Quantity);

        if (total != Quantity - (Owed ?? 0m))
            throw new DomainException(
                $"{Description}: the batches supply {total:0.###} but "
                + $"{Quantity - (Owed ?? 0m):0.###} is being dispensed.");

        _batches.Clear();

        foreach (var allocation in ordered)
            _batches.Add(DispensingLineBatch.Create(Id, allocation));

        BatchId = ordered[0].BatchId;
        BatchNumber = ordered[0].BatchNumber;
        BatchExpiry = ordered[0].Expiry;
    }

    /// <summary>
    /// Records that a generic was handed over in place of what was
    /// prescribed. The original product is kept, because the substitution
    /// itself is a fact the pharmacist may have to account for.
    /// </summary>
    internal void Owe(decimal owed)
    {
        if (owed < 0m || owed > Quantity)
            throw new DomainException(
                $"{Description}: between 0 and {Quantity:0.###} can be owed.");

        Owed = owed == 0m ? null : owed;
    }

    /// The owed quantity handed over, with the batches it came off.
    public void GiveOwed(IEnumerable<BatchAllocation>? allocations,
                         DateTime nowUtc)
    {
        if (Owed is null || OwedGivenAtUtc is not null)
            throw new DomainException($"{Description}: nothing is owed.");

        foreach (var allocation in allocations ?? Enumerable.Empty<BatchAllocation>())
            _batches.Add(DispensingLineBatch.Create(Id, allocation));

        OwedGivenAtUtc = nowUtc;
    }

    /// <summary>
    /// Corrects how much was handed over and what it was charged at.
    /// </summary>
    /// <remarks>
    /// A scheme cannot pay more than the item comes to, so its share comes
    /// down with an amount brought below it.
    /// </remarks>
    internal void Amend(decimal quantity, Money tariff)
    {
        if (quantity <= 0m)
            throw new DomainException($"{Description}: the quantity must be more than zero.");

        if (tariff.IsNegative)
            throw new DomainException($"{Description}: the amount cannot be negative.");

        Quantity = quantity;
        TariffAmount = tariff;

        if (SchemeContribution.Amount > tariff.Amount) SchemeContribution = tariff;
    }

    /// <summary>
    /// More of the item handed over after the issue, from the batches it
    /// came off.
    /// </summary>
    public void TakeMore(IEnumerable<BatchAllocation> allocations)
    {
        ArgumentNullException.ThrowIfNull(allocations);

        foreach (var allocation in allocations)
        {
            if (_batches.Find(b => b.BatchId == allocation.BatchId) is { } same)
                same.Add(allocation.Quantity);
            else
                _batches.Add(DispensingLineBatch.Create(Id, allocation));
        }

        if (BatchId is null && _batches.Count > 0)
        {
            var first = _batches.OrderBy(b => b.ExpiryDate).First();
            BatchId = first.BatchId;
            BatchNumber = first.BatchNumber;
            BatchExpiry = first.ExpiryDate;
        }
    }

    /// <summary>
    /// Part of what was handed over coming back, off the batches it came
    /// from - the latest expiry first, so the patient keeps the stock that
    /// runs out soonest. Returns what each batch takes back; what no batch
    /// recorded comes back with no batch.
    /// </summary>
    public IReadOnlyList<(Guid? BatchId, decimal Units)> GiveBack(decimal units)
    {
        if (units <= 0m)
            throw new DomainException("Give back more than nothing.");

        var back = new List<(Guid?, decimal)>();
        var remaining = units;

        foreach (var batch in _batches.OrderByDescending(b => b.ExpiryDate).ToList())
        {
            if (remaining <= 0m) break;

            var take = Math.Min(batch.Quantity, remaining);
            batch.Add(-take);
            remaining -= take;
            back.Add((batch.BatchId, take));

            if (batch.Quantity <= 0m) _batches.Remove(batch);
        }

        if (remaining > 0m) back.Add((null, remaining));
        return back;
    }

    public void MarkSubstitution(Guid substitutedForProductId)
    {
        IsSubstitution = true;
        SubstitutedForProductId = substitutedForProductId;
    }

    public void SetSchemeContribution(Money contribution)
    {
        if (contribution.IsNegative)
            throw new DomainException(
                "A scheme contribution cannot be negative.");

        SchemeContribution = contribution;
    }
}

/// <summary>
/// So much of one batch, as the allocator chose it.
/// </summary>
public sealed record BatchAllocation(Guid BatchId, string BatchNumber,
                                     DateOnly Expiry, decimal Quantity);

/// <summary>
/// One batch a dispensing line draws on, and how much it takes from it.
/// </summary>
public sealed class DispensingLineBatch : Entity
{
    public Guid DispensingLineId { get; private set; }
    public Guid BatchId { get; private set; }
    public string BatchNumber { get; private set; } = "";
    public DateOnly ExpiryDate { get; private set; }
    public decimal Quantity { get; private set; }

    private DispensingLineBatch() { }

    internal static DispensingLineBatch Create(Guid lineId,
                                               BatchAllocation allocation) =>
        new()
        {
            DispensingLineId = lineId,
            BatchId = allocation.BatchId,
            BatchNumber = allocation.BatchNumber,
            ExpiryDate = allocation.Expiry,
            Quantity = allocation.Quantity
        };

    /// More taken off this batch, or (negative) given back to it.
    internal void Add(decimal quantity) => Quantity += quantity;
}

public enum WarningSeverity { Information = 1, Caution = 2, HardStop = 3 }

/// <summary>
/// A clinical reason to stop and think, raised by the service from the
/// patient's profile and the interaction table. A hard stop needs a
/// written reason before it can be passed; anything softer needs only an
/// acknowledgement, but both are recorded (DSP-007).
/// </summary>
public sealed class ClinicalWarning : Entity
{
    public Guid DispensingId { get; private set; }
    public Guid? DispensingLineId { get; private set; }

    /// Allergy, Interaction, Duplication, Dose, Pregnancy, Paediatric.
    public string Kind { get; private set; } = "";

    public WarningSeverity Severity { get; private set; }
    public string Message { get; private set; } = "";
    public bool IsAcknowledged { get; private set; }
    public Guid? AcknowledgedBy { get; private set; }
    public string? OverrideReason { get; private set; }

    /// <summary>
    /// The pharmacist whose own credentials released a hard stop, where
    /// the person working the screen could not release it themselves.
    /// Null where the operator held the authority, or for a softer
    /// warning that needs no release.
    /// </summary>
    public Guid? AuthorisedBy { get; private set; }

    public bool IsHardStop => Severity == WarningSeverity.HardStop;

    private ClinicalWarning() { }

    public static ClinicalWarning Raise(string kind, WarningSeverity severity,
        string message, Guid? dispensingLineId = null) =>
        new()
        {
            Kind = kind,
            Severity = severity,
            Message = message,
            DispensingLineId = dispensingLineId
        };

    internal void AssignTo(Guid dispensingId) => DispensingId = dispensingId;

    public void Acknowledge(Guid userId, string? reason,
                            Guid? authorisedBy = null)
    {
        if (IsHardStop && string.IsNullOrWhiteSpace(reason))
            throw new DomainException(
                "Overriding a hard-stop warning requires a written reason "
                + "(DSP-007).");

        IsAcknowledged = true;
        AcknowledgedBy = userId;
        OverrideReason = string.IsNullOrWhiteSpace(reason)
            ? null
            : reason.Trim();
        AuthorisedBy = authorisedBy;
    }
}
