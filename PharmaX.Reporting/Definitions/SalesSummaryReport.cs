using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Reporting;
using PharmaX.Domain.Patients;
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
/// drawer held money that had been handed back. Account payments taken at
/// a till are included too - the cash-up expects them in the drawer, and
/// leaving them out made Sales Summary short of the till cash-up by
/// exactly those amounts.
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

        // Money off a patient's account at a till: same tenders the
        // cash-up counts (ADM-DEF-18).
        var accountPaid = await _db.AccountTransactions
            .AsNoTracking()
            .Where(t => t.Kind == AccountEntryKind.Payment
                        && t.Tender != null
                        && t.TillSessionId != null)
            .Where(t => t.CreatedAtUtc >= r.FromUtc
                        && t.CreatedAtUtc < r.ToExclusiveUtc)
            .Select(t => new { Tender = t.Tender!.Value, Amount = t.Amount.Amount })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var payments = paid.SelectMany(p => p).ToList();
        var refunds = refunded.SelectMany(p => p).ToList();

        // Account payments stay out of the Sales column (that is retail
        // takings). They still join Net so the total matches cash-up
        // NetTakings for the same clock window.
        var rows = payments.Select(p => p.Tender)
            .Concat(refunds.Select(p => p.Tender))
            .Concat(accountPaid.Select(p => p.Tender))
            .Distinct()
            .OrderBy(t => t.DisplayName(), StringComparer.CurrentCulture)
            .Select(tender =>
            {
                var sold = payments.Where(p => p.Tender == tender).ToList();
                var accounts = accountPaid.Where(p => p.Tender == tender).ToList();
                var back = -refunds.Where(p => p.Tender == tender).Sum(p => p.Amount);
                var account = accounts.Sum(p => p.Amount);
                var sales = sold.Sum(p => p.Amount);

                return (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
                {
                    ["Tender"] = tender.DisplayName(),
                    ["Payments"] = sold.Count + accounts.Count,
                    ["Sales"] = sales,
                    ["Account"] = account == 0m ? null : account,
                    ["Refunds"] = back,
                    ["Net"] = sales + account + back
                };
            })
            .ToList();

        // Each total is the sum of its column, so the page adds up.
        decimal Sum(string key) => rows.Sum(x => Convert.ToDecimal(x[key] ?? 0m));

        var accountTotal = accountPaid.Sum(p => p.Amount);
        var net = Sum("Net");

        var result = ReportBuilder
            .For("Sales summary")
            .Column("Tender", "Tender")
            .Column("Payments", "Payments", rightAlign: true)
            .Column("Sales", "Sales", "N2", rightAlign: true)
            .Column("Account", "Account payments", "N2", rightAlign: true)
            .Column("Refunds", "Refunds", "N2", rightAlign: true)
            .Column("Net", "Net", "N2", rightAlign: true)
            .Rows(rows)
            .Total("Payments", (int)Sum("Payments"))
            .Total("Sales", Sum("Sales"))
            .Total("Account", accountTotal)
            .Total("Refunds", Sum("Refunds"))
            .Total("Net", net)
            .Total("AccountPayments", accountTotal)
            .Build(r.Provenance);

        if (accountTotal == 0m) return result;

        var culture = CultureInfo.CurrentCulture;
        return result with
        {
            Figures = new[]
            {
                new ReportFigure("Account payments",
                    accountTotal.ToString("N2", culture)),
                new ReportFigure("Net takings", net.ToString("N2", culture))
            }
        };
    }
}
