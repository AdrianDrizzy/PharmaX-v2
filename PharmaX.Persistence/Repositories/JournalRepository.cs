using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Finance;
using PharmaX.Domain.Finance;
using PharmaX.Domain.Sales;

namespace PharmaX.Persistence.Repositories;

public sealed class JournalRepository : IJournalRepository
{
    private readonly PharmaXDbContext _db;

    public JournalRepository(PharmaXDbContext db) => _db = db;

    public async Task<JournalBatch?> GetAsync(Guid id,
        CancellationToken ct = default) =>
        await _db.JournalBatches
            .Include(b => b.Lines)
            .FirstOrDefaultAsync(b => b.Id == id, ct);

    public async Task<IReadOnlyList<JournalBatch>> RecentAsync(int take = 100,
        CancellationToken ct = default) =>
        await _db.JournalBatches
            .Include(b => b.Lines)
            .OrderByDescending(b => b.PostingDate)
            .ThenByDescending(b => b.BatchNumber)
            .Take(take)
            .ToListAsync(ct);

    public async Task<JournalBatch?> ForDayAsync(string source, DateOnly day,
        CancellationToken ct = default) =>
        await _db.JournalBatches
            .Include(b => b.Lines)
            .FirstOrDefaultAsync(b => b.Source == source
                                   && b.PostingDate == day, ct);

    /// <summary>
    /// The finalised sales of one trading day.
    /// </summary>
    /// <remarks>
    /// Selected on local date rather than UTC. A day's takings are the
    /// day the shop had, and a pharmacy trading past midnight UTC would
    /// otherwise have its evening posted to tomorrow.
    /// </remarks>
    public async Task<IReadOnlyList<Sale>> FinalisedOnAsync(DateOnly day,
        CancellationToken ct = default)
    {
        var from = day.ToDateTime(TimeOnly.MinValue).ToUniversalTime();
        var to = day.AddDays(1).ToDateTime(TimeOnly.MinValue)
                    .ToUniversalTime();

        return await _db.Sales
            .Include(s => s.Lines)
            .Include(s => s.Payments)
            .Where(s => s.Status == SaleStatus.Finalised)
            .Where(s => s.OccurredAtUtc >= from && s.OccurredAtUtc < to)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<string, string>> AccountNamesAsync(
        CancellationToken ct = default) =>
        await _db.LedgerAccounts
            .AsNoTracking()
            .ToDictionaryAsync(a => a.Code, a => a.Name, StringComparer.Ordinal,
                               ct);

    public async Task AddAsync(JournalBatch batch,
        CancellationToken ct = default) =>
        await _db.JournalBatches.AddAsync(batch, ct);
}
