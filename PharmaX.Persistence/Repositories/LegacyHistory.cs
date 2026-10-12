using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Legacy;

namespace PharmaX.Persistence.Repositories;

/// <summary>The history taken over from RSI, searched for the history screen.</summary>
public sealed class LegacyHistory : ILegacyHistory
{
    private readonly PharmaXDbContext _db;

    public LegacyHistory(PharmaXDbContext db) => _db = db;

    public async Task<IReadOnlyList<LegacyRecordDto>> SearchAsync(LegacyQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var records = _db.LegacyRecords.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Kind))
            records = records.Where(r => r.Kind == query.Kind);

        if (query.From is DateOnly from)
        {
            var start = from.ToDateTime(TimeOnly.MinValue);
            records = records.Where(r => r.OccurredAt >= start);
        }

        if (query.To is DateOnly to)
        {
            var end = to.ToDateTime(TimeOnly.MinValue).AddDays(1);
            records = records.Where(r => r.OccurredAt < end);
        }

        if (!string.IsNullOrWhiteSpace(query.PatientFile))
            records = records.Where(r => r.PatientFile == query.PatientFile);

        // Word by word, like every other search: "nifedipine 1757" finds
        // that patient's nifedipine.
        foreach (var word in SearchTerms.Words(query.Text ?? ""))
        {
            var pattern = $"%{word}%";

            records = records.Where(r =>
                EF.Functions.Like(r.Description, pattern)
                || EF.Functions.Like(r.Number ?? "", pattern)
                || EF.Functions.Like(r.ProductCode ?? "", pattern)
                || EF.Functions.Like(r.PatientFile ?? "", pattern)
                || EF.Functions.Like(r.Party ?? "", pattern)
                || EF.Functions.Like(r.Operator ?? "", pattern));
        }

        return await records
            .OrderByDescending(r => r.OccurredAt)
            .Take(query.Limit)
            .Select(r => new LegacyRecordDto(r.Kind, r.Number, r.OccurredAt,
                r.ProductCode, r.PatientFile, r.Party, r.Description, r.Quantity,
                r.Amount, r.Operator))
            .ToListAsync(ct);
    }
}
