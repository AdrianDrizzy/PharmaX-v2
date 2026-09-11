using PharmaX.Domain.Common;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Primitives;

namespace PharmaX.Domain.Finance;

/// <summary>
/// One side of one posting. Amount is always positive and direction is a
/// flag, because a signed amount plus a direction gives two ways to say
/// the same thing and eventually they disagree.
/// </summary>
public sealed class JournalLine : Entity
{
    public Guid JournalBatchId { get; private set; }
    public int LineNo { get; private set; }
    public string AccountCode { get; private set; } = "";
    public Money Amount { get; private set; }
    public bool IsDebit { get; private set; }
    public string? Narration { get; private set; }

    private JournalLine() { }

    internal static JournalLine Create(string accountCode, Money amount,
                                       bool isDebit, string? narration)
    {
        if (string.IsNullOrWhiteSpace(accountCode))
            throw new DomainException("A journal line needs an account code.");

        if (amount.IsNegative)
            throw new DomainException(
                "A journal amount is always positive; direction is the "
                + "debit flag.");

        if (amount.IsZero)
            throw new DomainException("A journal line of zero posts nothing.");

        return new JournalLine
        {
            AccountCode = accountCode,
            Amount = amount,
            IsDebit = isDebit,
            Narration = narration
        };
    }

    internal void AssignTo(Guid journalBatchId, int lineNo)
    {
        JournalBatchId = journalBatchId;
        LineNo = lineNo;
    }
}
