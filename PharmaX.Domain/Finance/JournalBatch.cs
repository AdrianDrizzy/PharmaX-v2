using PharmaX.Domain.Common;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Primitives;

namespace PharmaX.Domain.Finance;

public enum LedgerAccountType { Asset, Liability, Equity, Income, Expense }

/// <summary>
/// One account in the chart. Kept in PharmaX even though posting to an
/// accounting package is behind a feature flag in Release 1.0, because
/// the journals have to be correct from day one or the first period that
/// is switched on has nothing to hand over.
/// </summary>
public sealed class LedgerAccount : Entity
{
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public LedgerAccountType AccountType { get; private set; }
    public bool IsActive { get; private set; } = true;

    private LedgerAccount() { }

    public static LedgerAccount Create(string code, string name,
                                       LedgerAccountType accountType)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("A ledger account requires a code.");

        return new LedgerAccount
        {
            Code = code.Trim(),
            Name = name.Trim(),
            AccountType = accountType
        };
    }

    /// Debits increase an asset or an expense and decrease everything else.
    public bool IsDebitPositive =>
        AccountType is LedgerAccountType.Asset or LedgerAccountType.Expense;

    /// <summary>
    /// Changes what the account is called. The code never changes: it is
    /// what every posted journal line refers to.
    /// </summary>
    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("A ledger account requires a name.");

        Name = name.Trim();
    }

    public void Deactivate() => IsActive = false;
}

/// <summary>
/// A set of journal lines that balance and post together. The batch, not
/// the line, is the unit of posting, which is what makes "debits equal
/// credits" a rule the system can actually enforce.
/// </summary>
public sealed class JournalBatch : AuditableEntity
{
    private readonly List<JournalLine> _lines = new();

    public string BatchNumber { get; private set; } = "";
    public DateOnly PostingDate { get; private set; }
    public string Source { get; private set; } = "";
    public string? Narration { get; private set; }
    public bool IsPosted { get; private set; }
    public DateTime? PostedAtUtc { get; private set; }

    public IReadOnlyList<JournalLine> Lines => _lines;

    public Money TotalDebits => _lines
        .Where(l => l.IsDebit)
        .Aggregate(Money.Zero(), (sum, l) => sum + l.Amount);

    public Money TotalCredits => _lines
        .Where(l => !l.IsDebit)
        .Aggregate(Money.Zero(), (sum, l) => sum + l.Amount);

    public bool IsBalanced => TotalDebits == TotalCredits;

    private JournalBatch() { }

    public static JournalBatch Create(string batchNumber,
        DateOnly postingDate, string source, string? narration = null) =>
        new()
        {
            BatchNumber = batchNumber,
            PostingDate = postingDate,
            Source = source,
            Narration = narration
        };

    public JournalLine AddLine(string accountCode, Money amount,
                               bool isDebit, string? narration = null)
    {
        if (IsPosted)
            throw new DomainException("A posted batch cannot be changed.");

        var line = JournalLine.Create(accountCode, amount, isDebit, narration);
        line.AssignTo(Id, _lines.Count + 1);
        _lines.Add(line);
        return line;
    }

    public void Post(DateTime nowUtc)
    {
        if (IsPosted)
            throw new DomainException("This batch is already posted.");

        if (_lines.Count == 0)
            throw new DomainException("An empty batch cannot be posted.");

        if (!IsBalanced)
            throw new DomainException(
                $"Debits {TotalDebits} do not equal credits {TotalCredits}. "
                + "An unbalanced batch is never posted.");

        IsPosted = true;
        PostedAtUtc = nowUtc;
    }
}
