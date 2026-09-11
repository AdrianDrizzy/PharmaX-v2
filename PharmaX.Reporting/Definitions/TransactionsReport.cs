using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Reporting;
using PharmaX.Domain.Sales;
using PharmaX.Persistence;
using PharmaX.Reporting.Engine;

namespace PharmaX.Reporting.Definitions;

public sealed class TransactionsReport : IReportDefinition
{
    public string Key => "Transactions";

    private readonly PharmaXDbContext _db;

    public TransactionsReport(PharmaXDbContext db) => _db = db;

    public async Task<ReportResult> RunAsync(ResolvedRequest r,
                                             CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(r);

        var query = _db.Sales
            .AsNoTracking()
            .Where(s => s.OccurredAtUtc >= r.FromUtc
                        && s.OccurredAtUtc < r.ToExclusiveUtc)
            .Where(s => s.Status == SaleStatus.Finalised);

        // Slicers are applied here, never by string concatenation.
        if (r.Guid("operator") is Guid op)
            query = query.Where(s => s.OperatorId == op);

        if (r.String("tender") is string tender
            && Enum.TryParse<TenderType>(tender, out var parsed))
            query = query.Where(s => s.Payments.Any(p => p.Tender == parsed));

        if (r.String("taxCode") is string taxCode)
            query = query.Where(s => s.Lines.Any(l => l.TaxCode == taxCode));

        // The money on a Sale is computed from its lines rather than
        // stored, so the line values are projected and added up here.
        // Not in SQL: SQLite cannot apply Sum to a decimal, and the
        // workstation database is always SQLite (NET-002).
        var projected = await query
            .OrderBy(s => s.OccurredAtUtc)
            .Select(s => new
            {
                s.DocumentNumber,
                s.OccurredAtUtc,
                Operator = s.OperatorId,
                Lines = s.Lines
                    .Where(l => !l.IsVoided)
                    .Select(l => new
                    {
                        Value = l.UnitPrice.Amount * l.Quantity
                                - l.DiscountAmount.Amount,
                        Rated = l.PriceSource == PriceSource.MedicalAidTariff
                    })
                    .ToList()
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var sales = projected
            .Select(s => new
            {
                s.DocumentNumber,
                s.OccurredAtUtc,
                s.Operator,
                Lines = s.Lines.Count,
                Total = s.Lines.Sum(l => l.Value),
                IsRated = s.Lines.Exists(l => l.Rated)
            })
            .ToList();

        // Applied after materialising because the rated flag is derived
        // from the lines rather than stored on the sale.
        var rows = r.Bool("ratedOnly")
            ? sales.Where(s => s.IsRated).ToList()
            : sales;

        return ReportBuilder
            .For("Transactions")
            .Column("DocumentNumber", "Document")
            .Column("OccurredAt", "Date and time", "dd/MM/yyyy HH:mm")
            .Column("Operator", "Operator")
            .Column("Lines", "Lines", rightAlign: true)
            .Column("Total", "Total", "N2", rightAlign: true)
            .Rows(rows.Select(x => new Dictionary<string, object?>
            {
                ["DocumentNumber"] = x.DocumentNumber,
                ["OccurredAt"] = x.OccurredAtUtc.ToLocalTime(),
                ["Operator"] = r.OperatorName(x.Operator),
                ["Lines"] = x.Lines,
                ["Total"] = x.Total
            }))
            .Total("Total", rows.Sum(x => x.Total))
            .Total("Count", rows.Count)
            .Build(r.Provenance);
    }
}
