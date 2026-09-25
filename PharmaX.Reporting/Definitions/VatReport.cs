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

        // Output VAT from everywhere the ledger takes it: the till's
        // sales, less what refunds gave back, plus the medical aid
        // schemes' share of what was dispensed. The report used to read
        // sales alone, so it never agreed with the VAT Control account on
        // any day with a refund or a medical aid script (DEF-0025). Each
        // line's VAT is extracted exactly as the journal extracts it - per
        // line, through the same calculator - so the two agree to the cent.
        var items = new List<(string Source, string TaxCode, decimal Rate,
                              Money Gross)>();

        var sales = await _db.Sales
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

        items.AddRange(sales.Select(l =>
            ("Sale", l.TaxCode, l.VatRatePercent, Money.Of(l.Gross))));

        var refunds = await _db.SaleReturns
            .AsNoTracking()
            .Where(x => x.OccurredAtUtc >= r.FromUtc
                        && x.OccurredAtUtc < r.ToExclusiveUtc)
            .Where(x => x.Status == ReturnStatus.Finalised)
            .SelectMany(x => x.Lines)
            .Select(l => new
            {
                l.TaxCode,
                l.VatRatePercent,
                Gross = l.UnitPrice.Amount * l.Quantity
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        items.AddRange(refunds.Select(l =>
            ("Refund", l.TaxCode, l.VatRatePercent, Money.Of(-l.Gross))));

        var from = DateOnly.FromDateTime(r.FromUtc.ToLocalTime());
        var to = DateOnly.FromDateTime(r.ToExclusiveUtc.ToLocalTime());

        var claimed = await _db.MedicalAidClaims
            .AsNoTracking()
            .Where(c => c.ClaimedOn >= from && c.ClaimedOn < to)
            .Select(c => c.DispensingId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (claimed.Count > 0)
        {
            var schemeLines = await _db.Dispensings
                .AsNoTracking()
                .Where(d => claimed.Contains(d.Id))
                .SelectMany(d => d.Lines)
                .Select(l => new
                {
                    l.ProductId,
                    Share = l.SchemeContribution.Amount
                })
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var productIds = schemeLines.Select(l => l.ProductId).Distinct()
                                        .ToList();

            var products = await _db.Products
                .AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .Select(p => new { p.Id, p.TaxCode, p.VatRatePercent })
                .ToDictionaryAsync(p => p.Id, ct)
                .ConfigureAwait(false);

            items.AddRange(schemeLines
                .Where(l => l.Share != 0m)
                .Select(l =>
                {
                    var product = products.GetValueOrDefault(l.ProductId);
                    return ("Scheme", product?.TaxCode ?? "A",
                            product?.VatRatePercent ?? 0m, Money.Of(l.Share));
                }));
        }

        var only = r.String("transactionType");

        var byCode = items
            .Where(i => string.IsNullOrWhiteSpace(only)
                        || string.Equals(i.Source, only,
                                         StringComparison.Ordinal))
            .Select(i => (i.TaxCode, i.Rate,
                          Split: VatCalculator.FromInclusive(i.Gross, i.Rate)))
            .GroupBy(i => new { i.TaxCode, i.Rate })
            .Select(g => new
            {
                g.Key.TaxCode,
                VatRatePercent = g.Key.Rate,
                Net = g.Sum(x => x.Split.NetAmount.Amount),
                Vat = g.Sum(x => x.Split.VatAmount.Amount),
                Gross = g.Sum(x => x.Split.GrossAmount.Amount),
                Count = g.Count()
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
