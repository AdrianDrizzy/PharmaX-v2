using PharmaX.Application.Abstractions;
using PharmaX.Application.Results;
using PharmaX.Application.Security;
using PharmaX.Domain.Finance;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Sales;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Finance;

public sealed record DayEndJournalLineDto(int LineNo, string AccountCode,
    string AccountName, decimal Debit, decimal Credit, string? Narration);

public sealed record DayEndJournalBatchDto(Guid Id, string BatchNumber,
    DateOnly PostingDate, string Source, string? Narration, bool IsPosted,
    decimal TotalDebits, decimal TotalCredits, bool IsBalanced,
    IReadOnlyList<DayEndJournalLineDto> Lines);

/// <summary>
/// What a day's trading came to, before it is turned into journals.
/// </summary>
/// <param name="PostedAsTraded">
/// True where automatic posting is on, so every sale wrote its own
/// journal as it was made and there is nothing left for a day-end batch
/// to do.
/// </param>
public sealed record DaySummaryDto(DateOnly Day, int Sales, decimal Gross,
    decimal Vat, decimal Net, IReadOnlyDictionary<string, decimal> ByTender,
    bool AlreadyPosted, bool PostedAsTraded = false);

public interface IJournalRepository
{
    Task<JournalBatch?> GetAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<JournalBatch>> RecentAsync(int take = 100,
        CancellationToken ct = default);

    /// <summary>
    /// The batch already posted for a day from a given source, if there
    /// is one. Day-end posting asks this before it builds anything.
    /// </summary>
    Task<JournalBatch?> ForDayAsync(string source, DateOnly day,
        CancellationToken ct = default);

    Task<IReadOnlyList<Sale>> FinalisedOnAsync(DateOnly day,
        CancellationToken ct = default);

    /// Account names for the chart, so a journal reads in words.
    Task<IReadOnlyDictionary<string, string>> AccountNamesAsync(
        CancellationToken ct = default);

    Task AddAsync(JournalBatch batch, CancellationToken ct = default);
}

public interface IDayEndPostingService
{
    Task<Result<DaySummaryDto>> SummariseAsync(DateOnly day,
        CancellationToken ct = default);

    /// <summary>
    /// Builds the journal for a day's takings. Built and saved unposted,
    /// so it can be read before anyone commits to it.
    /// </summary>
    Task<Result<DayEndJournalBatchDto>> PrepareAsync(DateOnly day,
        CancellationToken ct = default);

    /// <summary>
    /// Commits a prepared batch. A posted batch is immutable, which is
    /// what makes it safe to hand to an accounting package.
    /// </summary>
    Task<Result<DayEndJournalBatchDto>> PostAsync(Guid batchId,
        CancellationToken ct = default);

    Task<Result<IReadOnlyList<DayEndJournalBatchDto>>> RecentAsync(
        CancellationToken ct = default);
}

/// <summary>
/// Turns a day's finalised sales into a balanced journal batch.
/// </summary>
/// <remarks>
/// The shape of the entry is the one every retail ledger uses: what came
/// in is debited to where it landed, and the sale is credited apart into
/// the part that is income and the part that was never the pharmacy's -
/// the VAT it collected on the state's behalf.
///
///   Debit  each tender (cash, card, account)   what was taken
///   Credit sales income                        the sale excluding VAT
///   Credit VAT output                          the VAT collected
///
/// Debits and credits are proved equal by the aggregate before it will
/// post, so a rounding error cannot reach an accountant.
/// </remarks>
public sealed class DayEndPostingService : IDayEndPostingService
{
    /// The source stamped on every batch this service creates, and what
    /// it asks by when checking whether a day is already done.
    public const string Source = "PointOfSale";

    private readonly IJournalRepository _journals;
    private readonly IDocumentNumbering _numbering;
    private readonly IPermissionService _permissions;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly PharmaXOptions _options;

    public DayEndPostingService(IJournalRepository journals,
        IDocumentNumbering numbering, IPermissionService permissions,
        IAuditWriter audit, IUnitOfWork uow, IClock clock,
        PharmaXOptions options)
    {
        _journals = journals;
        _numbering = numbering;
        _permissions = permissions;
        _audit = audit;
        _uow = uow;
        _clock = clock;
        _options = options;
    }

    public async Task<Result<DaySummaryDto>> SummariseAsync(DateOnly day,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ReportFinancial);

        var sales = await _journals.FinalisedOnAsync(day, ct);
        var existing = await _journals.ForDayAsync(Source, day, ct);

        var gross = sales.Aggregate(Money.Zero(), (s, x) => s + x.Total);
        var vat = sales.Aggregate(Money.Zero(),
            (s, x) => s + x.Lines.Where(l => !l.IsVoided)
                                 .Aggregate(Money.Zero(), (t, l) => t + l.Vat.VatAmount));

        var byTender = new Dictionary<string, decimal>(StringComparer.Ordinal);

        foreach (var payment in sales.SelectMany(s => s.Payments))
        {
            var key = payment.Tender.DisplayName();
            byTender[key] = byTender.GetValueOrDefault(key)
                            + payment.Amount.Amount;
        }

        return Result<DaySummaryDto>.Ok(new DaySummaryDto(day, sales.Count,
            gross.Amount, vat.Amount, gross.Amount - vat.Amount, byTender,
            existing is not null, _options.Accounting.AutoPost));
    }

    public async Task<Result<DayEndJournalBatchDto>> PrepareAsync(DateOnly day,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ReportFinancial);

        // With automatic posting on, every sale journalled itself as it
        // was made. A day-end batch on top put the same takings through
        // the ledger a second time - cash, sales and VAT all doubled -
        // and nothing stopped it, because the check below only looks for
        // an earlier day-end batch (ADM-DEF-19).
        if (_options.Accounting.AutoPost)
            return Result<DayEndJournalBatchDto>.Fail("FIN-004",
                $"{day:dd/MM/yyyy} is already in the ledger: automatic "
                + "posting is on, so each sale wrote its own journal as it "
                + "was made. A day-end journal as well would count the "
                + "takings twice.");

        // A day posted twice is a day counted twice. The check is on the
        // batch rather than a flag on the sales, so a batch deleted by
        // hand in the accounting package can be prepared again.
        var already = await _journals.ForDayAsync(Source, day, ct);

        if (already is not null)
            return Result<DayEndJournalBatchDto>.Fail("FIN-002",
                $"{day:dd/MM/yyyy} already has journal "
                + $"{already.BatchNumber}"
                + (already.IsPosted ? " and it is posted." : " waiting to be posted."));

        var sales = await _journals.FinalisedOnAsync(day, ct);

        if (sales.Count == 0)
            return Result<DayEndJournalBatchDto>.Fail("FIN-001",
                $"Nothing was sold on {day:dd/MM/yyyy}, so there is "
                + "nothing to post.");

        var number = await _numbering.NextAsync(DocumentSeries.Journal, ct);
        var accounts = _options.Accounting;

        var batch = JournalBatch.Create(number, day, Source,
            $"Point of sale takings for {day:dd/MM/yyyy}");

        // What came in, one line per tender, because the bank
        // reconciliation is done per tender and a single lump sum cannot
        // be reconciled against anything.
        foreach (var group in sales
                     .SelectMany(s => s.Payments)
                     .GroupBy(p => p.Tender)
                     .OrderBy(g => g.Key.ToString(), StringComparer.Ordinal))
        {
            var taken = group.Aggregate(Money.Zero(), (s, p) => s + p.Amount);
            if (taken.IsZero) continue;

            batch.AddLine(accounts.CodeFor(group.Key), taken, isDebit: true,
                          $"{group.Key} takings");
        }

        var vat = sales.Aggregate(Money.Zero(),
            (s, x) => s + x.Lines.Where(l => !l.IsVoided)
                                 .Aggregate(Money.Zero(), (t, l) => t + l.Vat.VatAmount));

        var gross = sales.Aggregate(Money.Zero(), (s, x) => s + x.Total);
        var net = gross - vat;

        if (!net.IsZero)
            batch.AddLine(accounts.SalesIncome, net, isDebit: false,
                          "Sales excluding VAT");

        // The VAT was never the pharmacy's money: it is owed to the
        // revenue service, so it credits a liability rather than income.
        if (!vat.IsZero)
            batch.AddLine(accounts.VatOutput, vat, isDebit: false,
                          "VAT collected on sales");

        // Change given out is already netted off the tender lines, but
        // rounding across many sales can still leave a penny. It goes to
        // a difference account rather than being forced into income,
        // because a forced figure is a figure nobody can explain.
        var difference = batch.TotalDebits - batch.TotalCredits;

        if (!difference.IsZero)
        {
            batch.AddLine(accounts.Rounding,
                Money.Of(Math.Abs(difference.Amount)),
                isDebit: difference.Amount < 0m,
                "Rounding difference");
        }

        await _journals.AddAsync(batch, ct);
        await _uow.SaveChangesAsync(ct);

        return Result<DayEndJournalBatchDto>.Ok(await DescribeAsync(batch, ct));
    }

    public async Task<Result<DayEndJournalBatchDto>> PostAsync(Guid batchId,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ReportFinancial);

        var batch = await _journals.GetAsync(batchId, ct);

        if (batch is null)
            return Result<DayEndJournalBatchDto>.Fail("FIN-000",
                "That journal no longer exists.");

        // Post refuses an unbalanced batch on its own account; this is
        // the message the operator should see rather than an exception.
        if (!batch.IsBalanced)
            return Result<DayEndJournalBatchDto>.Fail("FIN-003",
                $"Debits {batch.TotalDebits} do not equal credits "
                + $"{batch.TotalCredits}. This journal will not post.");

        batch.Post(_clock.UtcNow);

        _audit.Applied("fin.journal.post", "JournalBatch", batch.Id,
            $"{batch.BatchNumber} for {batch.PostingDate:dd/MM/yyyy}, "
            + $"{batch.TotalDebits}");

        await _uow.SaveChangesAsync(ct);

        return Result<DayEndJournalBatchDto>.Ok(await DescribeAsync(batch, ct));
    }

    public async Task<Result<IReadOnlyList<DayEndJournalBatchDto>>> RecentAsync(
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ReportFinancial);

        var batches = await _journals.RecentAsync(ct: ct);
        var names = await _journals.AccountNamesAsync(ct);

        IReadOnlyList<DayEndJournalBatchDto> dtos =
            batches.Select(b => Describe(b, names)).ToList();

        return Result<IReadOnlyList<DayEndJournalBatchDto>>.Ok(dtos);
    }

    private async Task<DayEndJournalBatchDto> DescribeAsync(JournalBatch batch,
        CancellationToken ct) =>
        Describe(batch, await _journals.AccountNamesAsync(ct));

    private static DayEndJournalBatchDto Describe(JournalBatch b,
        IReadOnlyDictionary<string, string> names) =>
        new(b.Id, b.BatchNumber, b.PostingDate, b.Source, b.Narration,
            b.IsPosted, b.TotalDebits.Amount, b.TotalCredits.Amount,
            b.IsBalanced,
            b.Lines.OrderBy(l => l.LineNo).Select(l => new DayEndJournalLineDto(
                l.LineNo, l.AccountCode,
                names.GetValueOrDefault(l.AccountCode, ""),
                l.IsDebit ? l.Amount.Amount : 0m,
                l.IsDebit ? 0m : l.Amount.Amount,
                l.Narration)).ToList());
}
