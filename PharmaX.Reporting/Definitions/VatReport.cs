using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Reporting;
using PharmaX.Domain.Pricing;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Sales;
using PharmaX.Persistence;
using PharmaX.Reporting.Engine;

namespace PharmaX.Reporting.Definitions;

/// <summary>
/// VAT by tax code. Serves both the detailed and the summary presets,
/// which is the whole point of the parameterised design: two names on
/// the report menu, one query.
/// </summary>
public sealed class VatReport : IReportDefinition
{
    public string Key => "Vat";

    private readonly PharmaXDbContext _db;

    public VatReport(PharmaXDbContext db) => _db = db;

    public async Task<ReportResult> RunAsync(ResolvedRequest r,
                                             CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(r);

        var lines = await _db.Sales
            .AsNoTracking()
            .Where(s => s.OccurredAtUtc >= r.FromUtc
                        && s.OccurredAtUtc < r.ToExclusiveUtc)
            .Where(s => s.Status == SaleStatus.Finalised)
            .SelectMany(s => s.Lines)
            .Where(l => !l.IsVoided)
            .Select(l => new
            {
                l.TaxCode,
                l.VatRatePercent,
                Gross = l.UnitPrice.Amount * l.Quantity
                        - l.DiscountAmount.Amount
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // The extraction runs through the same VatCalculator the till
        // used, so the report and the receipt cannot disagree.
        var byCode = lines
            .GroupBy(l => new { l.TaxCode, l.VatRatePercent })
            .Select(g =>
            {
                var gross = Money.Of(g.Sum(x => x.Gross));
                var split = VatCalculator.FromInclusive(gross, g.Key.VatRatePercent);

                return new
                {
                    g.Key.TaxCode,
                    g.Key.VatRatePercent,
                    Net = split.NetAmount.Amount,
                    Vat = split.VatAmount.Amount,
                    Gross = split.GrossAmount.Amount,
                    Count = g.Count()
                };
            })
            .Where(x => !r.Bool("aboveZeroOnly") || x.Vat > 0m)
            .OrderBy(x => x.TaxCode)
            .ToList();

        var isSummary = string.Equals(r.String("mode"), "summary",
                                      StringComparison.Ordinal);

        var builder = ReportBuilder
            .For(isSummary ? "V.A.T Summary" : "V.A.T Detailed")
            .Column("TaxCode", "Code")
            .Column("Rate", "Rate %", "N2", rightAlign: true)
            .Column("Net", "Net", "N2", rightAlign: true)
            .Column("Vat", "VAT", "N2", rightAlign: true)
            .Column("Gross", "Gross", "N2", rightAlign: true);

        if (!isSummary)
            builder.Column("Count", "Lines", rightAlign: true);

        return builder
            .Rows(byCode.Select(x => new Dictionary<string, object?>
            {
                ["TaxCode"] = x.TaxCode,
                ["Rate"] = x.VatRatePercent,
                ["Net"] = x.Net,
                ["Vat"] = x.Vat,
                ["Gross"] = x.Gross,
                ["Count"] = x.Count
            }))
            .Total("Net", byCode.Sum(x => x.Net))
            .Total("Vat", byCode.Sum(x => x.Vat))
            .Total("Gross", byCode.Sum(x => x.Gross))
            .Build(r.Provenance);
    }
}

/// <summary>
/// What is on the shelf, and what is about to stop being sellable.
/// </summary>
public sealed class StockOnHandReport : IReportDefinition
{
    public string Key => "StockOnHand";

    private readonly PharmaXDbContext _db;

    public StockOnHandReport(PharmaXDbContext db) => _db = db;

    public async Task<ReportResult> RunAsync(ResolvedRequest r,
                                             CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(r);

        var rows = await _db.Products
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Description)
            .Select(p => new
            {
                p.Id,
                p.Code,
                p.Description,
                p.DepartmentId,
                p.CategoryId,
                p.QuantityOnHand,
                p.ReorderLevel,
                Cost = p.CostPrice.Amount,
                Retail = p.RetailPrice.Amount
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // The preset has offered this filter since it was written and
        // the report never read it, so choosing a department changed
        // nothing at all.
        if (r.Guid("department") is Guid department)
            rows = rows.Where(x => x.DepartmentId == department).ToList();

        if (r.Guid("category") is Guid category)
            rows = rows.Where(x => x.CategoryId == category).ToList();

        if (r.Bool("belowReorderOnly"))
            rows = rows.Where(x => x.QuantityOnHand <= x.ReorderLevel)
                       .ToList();

        return ReportBuilder
            .For("Stock On Hand")
            .Column("Code", "Code")
            .Column("Description", "Description")
            .Column("OnHand", "On hand", "N3", rightAlign: true)
            .Column("Reorder", "Reorder at", "N3", rightAlign: true)
            .Column("Cost", "Unit cost", "N2", rightAlign: true)
            .Column("Value", "Value at cost", "N2", rightAlign: true)
            .Rows(rows.Select(x => new Dictionary<string, object?>
            {
                ["Code"] = x.Code,
                ["Description"] = x.Description,
                ["OnHand"] = x.QuantityOnHand,
                ["Reorder"] = x.ReorderLevel,
                ["Cost"] = x.Cost,
                ["Value"] = x.QuantityOnHand * x.Cost
            }))
            .Total("Value", rows.Sum(x => x.QuantityOnHand * x.Cost))
            .Total("Count", rows.Count)
            .Build(r.Provenance);
    }
}
