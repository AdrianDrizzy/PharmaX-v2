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
    public bool IsSubstitution { get; private set; }
    public Guid? SubstitutedForProductId { get; private set; }

    public Money TariffAmount { get; private set; } = Money.Zero();
    public Money SchemeContribution { get; private set; } = Money.Zero();

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
    /// Records that a generic was handed over in place of what was
    /// prescribed. The original product is kept, because the substitution
    /// itself is a fact the pharmacist may have to account for.
    /// </summary>
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

    public void Acknowledge(Guid userId, string? reason)
    {
        if (IsHardStop && string.IsNullOrWhiteSpace(reason))
            throw new DomainException(
                "Overriding a hard-stop warning requires a written reason "
                + "(DSP-007).");

        IsAcknowledged = true;
        AcknowledgedBy = userId;
        OverrideReason = reason;
    }
}
