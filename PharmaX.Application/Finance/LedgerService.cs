using PharmaX.Application.Abstractions;
using PharmaX.Application.Repositories;
using PharmaX.Application.Results;
using PharmaX.Application.Security;
using PharmaX.Domain.Finance;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Finance;

public sealed record LedgerAccountDto(Guid Id, string Code, string Name,
    LedgerAccountType AccountType, bool IsActive);

/// <summary>
/// One account's line on the trial balance.
/// </summary>
/// <param name="Debit">
/// The balance where it falls on the debit side, otherwise zero. A
/// trial balance shows a net figure in one column, not both sides
/// gross: an account that has been debited a thousand times and
/// credited nine hundred owes a hundred, and that is what is printed.
/// </param>
public sealed record TrialBalanceLineDto(string Code, string Name,
    LedgerAccountType AccountType, decimal Debit, decimal Credit);

public sealed record TrialBalanceDto(DateOnly AsAt,
    IReadOnlyList<TrialBalanceLineDto> Lines, decimal TotalDebits,
    decimal TotalCredits)
{
    /// <summary>
    /// Whether the books balance.
    /// </summary>
    /// <remarks>
    /// It should be impossible for this to be false - every batch is
    /// refused at posting unless it balances - which is exactly why it
    /// is worth printing. A trial balance that does not balance means
    /// something reached the tables without going through JournalBatch.
    /// </remarks>
    public bool IsBalanced => TotalDebits == TotalCredits;
}

/// One line of a journal as it is captured or read back.
public sealed record JournalLineDto(string AccountCode, decimal Amount,
                                    bool IsDebit, string? Narration);

public sealed record JournalBatchDto(Guid Id, string BatchNumber,
    DateOnly PostingDate, string Source, string? Narration, bool IsPosted,
    decimal TotalDebits, decimal TotalCredits,
    IReadOnlyList<JournalLineDto> Lines);

/// <summary>
/// The chart, the journals and the trial balance - the parts of the
/// books a person touches, as opposed to the ones that write themselves.
/// </summary>
public interface ILedgerService
{
    Task<Result<IReadOnlyList<LedgerAccountDto>>> AccountsAsync(
        bool includeInactive = false, CancellationToken ct = default);

    Task<Result<LedgerAccountDto>> AddAccountAsync(string code, string name,
        LedgerAccountType accountType, CancellationToken ct = default);

    Task<Result> RenameAccountAsync(Guid id, string name,
                                    CancellationToken ct = default);

    /// <summary>
    /// Takes an account out of use without deleting it.
    /// </summary>
    /// <remarks>
    /// Never deleted: posted journals refer to it by code, and an
    /// account that vanished would leave history pointing at nothing.
    /// </remarks>
    Task<Result> DeactivateAccountAsync(Guid id,
                                        CancellationToken ct = default);

    /// <summary>
    /// Captures a hand-written journal and posts it in one step.
    /// </summary>
    /// <remarks>
    /// One step because a draft that can be left half-finished is a
    /// draft somebody will leave half-finished. The balance check is the
    /// domain's, so an unbalanced set of lines is refused here rather
    /// than saved and found later.
    /// </remarks>
    Task<Result<JournalBatchDto>> PostJournalAsync(DateOnly postingDate,
        string? narration, IReadOnlyList<JournalLineDto> lines,
        CancellationToken ct = default);

    Task<Result<IReadOnlyList<JournalBatchDto>>> JournalsAsync(DateOnly from,
        DateOnly until, CancellationToken ct = default);

    Task<Result<TrialBalanceDto>> TrialBalanceAsync(DateOnly asAt,
        CancellationToken ct = default);

    /// The postings behind one figure on the trial balance.
    Task<Result<IReadOnlyList<LedgerEntry>>> EntriesAsync(string accountCode,
        DateOnly from, DateOnly until, CancellationToken ct = default);
}

public sealed class LedgerService : ILedgerService
{
    private readonly ILedgerRepository _ledger;
    private readonly IDocumentNumbering _numbering;
    private readonly IPermissionService _permissions;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public LedgerService(ILedgerRepository ledger,
        IDocumentNumbering numbering, IPermissionService permissions,
        IAuditWriter audit, IUnitOfWork uow, IClock clock)
    {
        _ledger = ledger;
        _numbering = numbering;
        _permissions = permissions;
        _audit = audit;
        _uow = uow;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<LedgerAccountDto>>> AccountsAsync(
        bool includeInactive = false, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.LedgerRead);

        var accounts = await _ledger.AccountsAsync(includeInactive, ct);

        IReadOnlyList<LedgerAccountDto> described = accounts
            .Select(Describe)
            .ToList();

        return Result<IReadOnlyList<LedgerAccountDto>>.Ok(described);
    }

    public async Task<Result<LedgerAccountDto>> AddAccountAsync(string code,
        string name, LedgerAccountType accountType,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.LedgerPost);

        if (string.IsNullOrWhiteSpace(code))
            return Result<LedgerAccountDto>.Fail("FIN-001",
                "An account needs a code.");

        if (string.IsNullOrWhiteSpace(name))
            return Result<LedgerAccountDto>.Fail("FIN-002",
                "An account needs a name.");

        var trimmed = code.Trim();

        if (await _ledger.AccountByCodeAsync(trimmed, ct) is not null)
            return Result<LedgerAccountDto>.Fail("FIN-003",
                $"Account {trimmed} already exists.");

        var account = LedgerAccount.Create(trimmed, name, accountType);

        await _ledger.AddAccountAsync(account, ct);
        _audit.Applied("fin.account.add", "LedgerAccount", account.Id,
                       $"{trimmed} {name}");

        await _uow.SaveChangesAsync(ct);

        return Result<LedgerAccountDto>.Ok(Describe(account));
    }

    public async Task<Result> RenameAccountAsync(Guid id, string name,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.LedgerPost);

        if (string.IsNullOrWhiteSpace(name))
            return Result.Fail("FIN-002", "An account needs a name.");

        var account = await _ledger.AccountAsync(id, ct);

        if (account is null)
            return Result.Fail("FIN-004", "Account not found.");

        account.Rename(name);
        _audit.Applied("fin.account.rename", "LedgerAccount", id, name);

        await _uow.SaveChangesAsync(ct);

        return Result.Ok();
    }

    public async Task<Result> DeactivateAccountAsync(Guid id,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.LedgerPost);

        var account = await _ledger.AccountAsync(id, ct);

        if (account is null)
            return Result.Fail("FIN-004", "Account not found.");

        // The standard accounts are where PharmaX posts by itself. Take
        // one out of use and the next sale of the day fails at the till,
        // which is a long way from where the mistake was made.
        if (LedgerCode.Standard.Any(a => string.Equals(a.Code, account.Code,
                StringComparison.Ordinal)))
            return Result.Fail("FIN-005",
                $"{account.Code} {account.Name} is one of the accounts "
                + "PharmaX posts to automatically and cannot be taken out "
                + "of use. Rename it if it is called something else here.");

        account.Deactivate();
        _audit.Applied("fin.account.deactivate", "LedgerAccount", id,
                       account.Code);

        await _uow.SaveChangesAsync(ct);

        return Result.Ok();
    }

    public async Task<Result<JournalBatchDto>> PostJournalAsync(
        DateOnly postingDate, string? narration,
        IReadOnlyList<JournalLineDto> lines, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lines);

        _permissions.Demand(PermissionCode.LedgerPost);

        if (lines.Count < 2)
            return Result<JournalBatchDto>.Fail("FIN-010",
                "A journal needs at least one debit and one credit.");

        // Checked before anything is built, so a typed account code that
        // does not exist is reported as itself rather than as a foreign
        // key failure three layers down.
        foreach (var code in lines.Select(l => l.AccountCode).Distinct(
                     StringComparer.Ordinal))
        {
            var account = await _ledger.AccountByCodeAsync(code, ct);

            if (account is null)
                return Result<JournalBatchDto>.Fail("FIN-011",
                    $"There is no account {code}.");

            if (!account.IsActive)
                return Result<JournalBatchDto>.Fail("FIN-012",
                    $"Account {code} {account.Name} is no longer in use.");
        }

        var number = await _numbering.NextAsync(DocumentSeries.Journal, ct);

        var batch = JournalBatch.Create(number, postingDate, "MANUAL",
                                        narration);

        try
        {
            foreach (var line in lines)
                batch.AddLine(line.AccountCode, Money.Of(line.Amount),
                              line.IsDebit, line.Narration);

            // Throws where debits and credits disagree, which is the
            // one rule this whole screen exists to enforce.
            batch.Post(_clock.UtcNow);
        }
        catch (Domain.Exceptions.DomainException ex)
        {
            return Result<JournalBatchDto>.Fail("FIN-013", ex.Message);
        }

        await _ledger.AddBatchAsync(batch, ct);
        _audit.Applied("fin.journal.post", "JournalBatch", batch.Id,
                       $"{number}, {batch.TotalDebits}");

        await _uow.SaveChangesAsync(ct);

        return Result<JournalBatchDto>.Ok(Describe(batch));
    }

    public async Task<Result<IReadOnlyList<JournalBatchDto>>> JournalsAsync(
        DateOnly from, DateOnly until, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.LedgerRead);

        var batches = await _ledger.BatchesAsync(from, until, ct);

        IReadOnlyList<JournalBatchDto> described = batches
            .Select(Describe)
            .ToList();

        return Result<IReadOnlyList<JournalBatchDto>>.Ok(described);
    }

    public async Task<Result<TrialBalanceDto>> TrialBalanceAsync(
        DateOnly asAt, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.LedgerRead);

        var totals = await _ledger.TotalsAsync(asAt, ct);

        // Every account, including the ones nothing has been posted to:
        // an empty line says "nothing here" where a missing line makes
        // somebody wonder whether the account exists at all.
        var accounts = await _ledger.AccountsAsync(includeInactive: true, ct);

        var byCode = totals.ToDictionary(t => t.AccountCode,
                                         StringComparer.Ordinal);

        var lines = new List<TrialBalanceLineDto>();

        foreach (var account in accounts)
        {
            var total = byCode.GetValueOrDefault(account.Code);
            var debits = total?.Debits ?? 0m;
            var credits = total?.Credits ?? 0m;

            // A dormant account with no history is noise on a page that
            // is read down a column.
            if (debits == 0m && credits == 0m && !account.IsActive) continue;

            var net = debits - credits;

            lines.Add(new TrialBalanceLineDto(account.Code, account.Name,
                account.AccountType,
                net > 0m ? net : 0m,
                net < 0m ? -net : 0m));
        }

        var balance = new TrialBalanceDto(asAt, lines,
            lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));

        return Result<TrialBalanceDto>.Ok(balance);
    }

    public async Task<Result<IReadOnlyList<LedgerEntry>>> EntriesAsync(
        string accountCode, DateOnly from, DateOnly until,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.LedgerRead);

        var entries = await _ledger.EntriesAsync(accountCode, from, until, ct);

        return Result<IReadOnlyList<LedgerEntry>>.Ok(entries);
    }

    private static LedgerAccountDto Describe(LedgerAccount a) =>
        new(a.Id, a.Code, a.Name, a.AccountType, a.IsActive);

    private static JournalBatchDto Describe(JournalBatch b) =>
        new(b.Id, b.BatchNumber, b.PostingDate, b.Source, b.Narration,
            b.IsPosted, b.TotalDebits.Amount, b.TotalCredits.Amount,
            b.Lines.Select(l => new JournalLineDto(l.AccountCode,
                l.Amount.Amount, l.IsDebit, l.Narration)).ToList());
}
