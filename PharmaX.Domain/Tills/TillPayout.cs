using PharmaX.Domain.Common;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Primitives;

namespace PharmaX.Domain.Tills;

/// <summary>What money taken out of the drawer was for.</summary>
public enum PayoutKind
{
    /// The owner taking money out of the business.
    Drawings = 1,

    /// Lunch for whoever is working, paid from the drawer.
    Lunch = 2,

    /// Anything else paid from the drawer: bread, airtime, a delivery fee.
    Expense = 3
}

public static class PayoutKindExtensions
{
    /// As the till and the slip say it.
    public static string DisplayName(this PayoutKind kind) => kind switch
    {
        PayoutKind.Drawings => "Drawings",
        PayoutKind.Lunch => "Lunch",
        _ => "Other expense"
    };
}

/// <summary>
/// Cash taken out of a till's drawer during its shift - drawings, lunch,
/// another expense - with the amount and what it was for, so the cash-up
/// expects that much less in the drawer and the Z-read says where it went.
/// </summary>
/// <remarks>
/// Never deleted. One recorded by mistake is voided with a reason, while
/// the till is still open, and stays on the record as voided.
/// </remarks>
public sealed class TillPayout : AuditableEntity
{
    public const int NoteLength = 200;

    public Guid TillSessionId { get; private set; }
    public PayoutKind Kind { get; private set; }
    public Money Amount { get; private set; } = Money.Zero();
    public string Note { get; private set; } = "";
    public DateTime PaidAtUtc { get; private set; }
    public Guid PaidBy { get; private set; }

    public DateTime? VoidedAtUtc { get; private set; }
    public Guid? VoidedBy { get; private set; }
    public string? VoidReason { get; private set; }

    public bool IsVoided => VoidedAtUtc is not null;

    private TillPayout() { }

    public static TillPayout Record(Guid tillSessionId, PayoutKind kind,
        Money amount, string note, Guid paidBy, DateTime paidAtUtc)
    {
        if (tillSessionId == Guid.Empty)
            throw new DomainException("Money out of a drawer belongs to a till session.");

        if (!Enum.IsDefined(kind))
            throw new DomainException("Say what the money was taken out for.");

        if (amount.IsNegative || amount.IsZero)
            throw new DomainException("The amount taken out must be more than nothing.");

        var text = (note ?? "").Trim();

        if (text.Length == 0)
            throw new DomainException("Say what the money was for.");

        return new TillPayout
        {
            TillSessionId = tillSessionId,
            Kind = kind,
            Amount = amount,
            Note = text.Length <= NoteLength ? text : text[..NoteLength],
            PaidBy = paidBy,
            PaidAtUtc = paidAtUtc
        };
    }

    public void Void(Guid by, DateTime atUtc, string reason)
    {
        if (IsVoided)
            throw new DomainException("This has already been voided.");

        var text = (reason ?? "").Trim();

        if (text.Length == 0)
            throw new DomainException("Say why it is being voided.");

        VoidedAtUtc = atUtc;
        VoidedBy = by;
        VoidReason = text.Length <= NoteLength ? text : text[..NoteLength];
    }
}
