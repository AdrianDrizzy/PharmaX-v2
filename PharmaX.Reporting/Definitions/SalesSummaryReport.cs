using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Reporting;
using PharmaX.Domain.Sales;
using PharmaX.Persistence;
using PharmaX.Reporting.Engine;

namespace PharmaX.Reporting.Definitions;

/// <summary>
/// Takings between two clock times: 08:00 to 13:00 the same day, or
/// 13:00 one day to 20:00 the next. The end minute is included.
/// </summary>
/// <remarks>
/// By tender, what was taken, what was refunded, and what stayed. Refunds
/// come off, as they do on Daily Sales: left out, the summary said the
/// drawer held money that had been handed back.
/// </remarks>
public sealed class SalesSummaryReport : IReportDefinition
{
    public const string PresetKey = "pos.sales.summary";

    public string Key => "SalesSummary";

    private readonly PharmaXDbContext _db;

    public SalesSummaryReport(PharmaXDbContext db) => _db = db;

    public async Task<ReportResult> RunAsync(ResolvedRequest r,
                                             CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(r);

        // Materialised, then grouped: SQLite cannot sum decimals.
        var paid = await _db.Sales
            .AsNoTracking()
            .Where(s => s.OccurredAtUtc >= r.FromUtc
                        && s.OccurredAtUtc < r.ToExclusiveUtc)
            .Where(s => s.Status == SaleStatus.Finalised)
            .Select(s => s.Payments
                .Select(p => new { p.Tender, Amount = p.Amount.Amount })
                .ToList())
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var refunded = await _db.SaleReturns
            .AsNoTracking()
            .Where(x => x.OccurredAtUtc >= r.FromUtc
                        && x.OccurredAtUtc < r.ToExclusiveUtc)
            .Where(x => x.Status == ReturnStatus.Finalised)
            .Select(x => x.Refunds
                .Select(p => new { p.Tender, Amount = p.Amount.Amount })
                .ToList())
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var payments = paid.SelectMany(p => p).ToList();
        var refunds = refunded.SelectMany(p => p).ToList();

        var rows = payments.Select(p => p.Tender)
            .Concat(refunds.Select(p => p.Tender))
            .Distinct()
            .OrderBy(t => t.DisplayName(), StringComparer.CurrentCulture)
            .Select(tender =>
            {
                var sold = payments.Where(p => p.Tender == tender).ToList();
                var back = -refunds.Where(p => p.Tender == tender).Sum(p => p.Amount);

                return (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
                {
                    ["Tender"] = tender.DisplayName(),
                    ["Payments"] = sold.Count,
                    ["Sales"] = sold.Sum(p => p.Amount),
                    ["Refunds"] = back,
                    ["Net"] = sold.Sum(p => p.Amount) + back
                };
            })
            .ToList();

        // Each total is the sum of its column, so the page adds up.
        decimal Sum(string key) => rows.Sum(x => Convert.ToDecimal(x[key]));

        return ReportBuilder
            .For("Sales summary")
            .Column("Tender", "Tender")
            .Column("Payments", "Payments", rightAlign: true)
            .Column("Sales", "Sales", "N2", rightAlign: true)
            .Column("Refunds", "Refunds", "N2", rightAlign: true)
            .Column("Net", "Net", "N2", rightAlign: true)
            .Rows(rows)
            .Total("Payments", (int)Sum("Payments"))
            .Total("Sales", Sum("Sales"))
            .Total("Refunds", Sum("Refunds"))
            .Total("Net", Sum("Net"))
            .Build(r.Provenance);
    }
}
