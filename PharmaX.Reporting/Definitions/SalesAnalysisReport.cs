using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Reporting;
using PharmaX.Domain.Sales;
using PharmaX.Persistence;
using PharmaX.Reporting.Engine;

namespace PharmaX.Reporting.Definitions;

/// <summary>
/// Sales grouped by whatever the preset asked for. Two names on the
/// report menu - "Sales (By Till Operator)" and "Sales Summary (By
/// Markup %)" - reach this one class, which is the whole point of the
/// parameterised design.
/// </summary>
public sealed class SalesAnalysisReport : IReportDefinition
{
    public string Key => "SalesAnalysis";

    private readonly PharmaXDbContext _db;

    public SalesAnalysisReport(PharmaXDbContext db) => _db = db;

    public async Task<ReportResult> RunAsync(ResolvedRequest r,
                                             CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(r);

        // Materialised and then flattened, rather than projected with
        // SelectMany: carrying the sale's operator down onto each line
        // in SQL needs APPLY, which SQLite does not have, and the
        // workstation database is always SQLite (NET-002).
        var sales = await _db.Sales
            .AsNoTracking()
            .Where(s => s.OccurredAtUtc >= r.FromUtc
                        && s.OccurredAtUtc < r.ToExclusiveUtc)
            .Where(s => s.Status == SaleStatus.Finalised)
            .Include(s => s.Lines)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var lines = sales
            .SelectMany(s => s.Lines
                .Where(l => !l.IsVoided)
                .Select(l => new
                {
                    s.OperatorId,
                    l.ProductId,
                    l.Description,
                    l.Quantity,
                    Gross = l.LineTotal.Amount
                }))
            .ToList();

        // Cost comes from the product rather than the line, because a
        // sale line records what was charged and not what it cost.
        var products = await _db.Products
            .AsNoTracking()
            .Select(p => new { p.Id, Cost = p.CostPrice.Amount })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var costs = products.ToDictionary(p => p.Id, p => p.Cost);

        var groupBy = r.String("groupBy") ?? "operator";

        var rows = groupBy switch
        {
            "markupBand" => lines
                .GroupBy(l => Band(l.Gross, Cost(costs, l.ProductId, l.Quantity)))
                .Select(g => Row(g.Key, g.Sum(x => x.Quantity),
                    g.Sum(x => x.Gross),
                    g.Sum(x => Cost(costs, x.ProductId, x.Quantity))))
                .OrderBy(row => (string)row["Group"]!)
                .ToList(),

            "product" => lines
                .GroupBy(l => l.Description)
                .Select(g => Row(g.Key, g.Sum(x => x.Quantity),
                    g.Sum(x => x.Gross),
                    g.Sum(x => Cost(costs, x.ProductId, x.Quantity))))
                .OrderByDescending(row => (decimal)row["Sales"]!)
                .ToList(),

            _ => lines
                .GroupBy(l => l.OperatorId)
                .Select(g => Row(r.OperatorName(g.Key),
                    g.Sum(x => x.Quantity), g.Sum(x => x.Gross),
                    g.Sum(x => Cost(costs, x.ProductId, x.Quantity))))
                .OrderByDescending(row => (decimal)row["Sales"]!)
                .ToList()
        };

        var title = groupBy == "markupBand"
            ? "Sales Summary (By Markup %)"
            : "Sales Analysis";

        return ReportBuilder
            .For(title)
            .Column("Group", GroupHeader(groupBy))
            .Column("Quantity", "Units", "N3", rightAlign: true)
            .Column("Sales", "Sales", "N2", rightAlign: true)
            .Column("Cost", "Cost", "N2", rightAlign: true)
            .Column("Margin", "Margin", "N2", rightAlign: true)
            .Column("MarginPercent", "Margin %", "N1", rightAlign: true)
            .Rows(rows)
            .Total("Sales", rows.Sum(x => (decimal)x["Sales"]!))
            .Total("Cost", rows.Sum(x => (decimal)x["Cost"]!))
            .Total("Margin", rows.Sum(x => (decimal)x["Margin"]!))
            .Total("Count", rows.Count)
            .Build(r.Provenance);
    }

    private static decimal Cost(IReadOnlyDictionary<Guid, decimal> costs,
        Guid productId, decimal quantity) =>
        costs.TryGetValue(productId, out var unit) ? unit * quantity : 0m;

    private static Dictionary<string, object?> Row(string group,
        decimal quantity, decimal sales, decimal cost)
    {
        var margin = sales - cost;

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Group"] = group,
            ["Quantity"] = quantity,
            ["Sales"] = sales,
            ["Cost"] = cost,
            ["Margin"] = margin,
            ["MarginPercent"] = sales == 0m ? 0m : margin / sales * 100m
        };
    }

    /// <summary>
    /// Bands rather than a margin per line, because the question this
    /// report answers is "how much of the month's trade was low margin",
    /// and a list of four thousand percentages does not answer it.
    /// </summary>
    private static string Band(decimal sales, decimal cost)
    {
        if (sales <= 0m) return "Unpriced";

        var markup = cost == 0m ? 100m : (sales - cost) / sales * 100m;

        return markup switch
        {
            < 0m => "1. Negative",
            < 10m => "2. Under 10%",
            < 20m => "3. 10 to 20%",
            < 35m => "4. 20 to 35%",
            < 50m => "5. 35 to 50%",
            _ => "6. Over 50%"
        };
    }

    private static string GroupHeader(string groupBy) => groupBy switch
    {
        "markupBand" => "Markup band",
        "product" => "Product",
        _ => "Operator"
    };
}

/// <summary>
/// The cash declaration form: what the drawer held, denomination by
/// denomination, against what the system expected.
/// </summary>
public sealed class CashUpReport : IReportDefinition
{
    public string Key => "CashUp";

    private readonly PharmaXDbContext _db;

    public CashUpReport(PharmaXDbContext db) => _db = db;

    public async Task<ReportResult> RunAsync(ResolvedRequest r,
                                             CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(r);

        var query = _db.TillDeclarations
            .AsNoTracking()
            .Include(d => d.Lines)
            .Include(d => d.Counts)
            .Where(d => d.DeclaredAtUtc >= r.FromUtc
                        && d.DeclaredAtUtc < r.ToExclusiveUtc);

        if (r.Guid("session") is Guid sessionId)
            query = query.Where(d => d.TillSessionId == sessionId);

        var declarations = await query
            .OrderBy(d => d.DeclaredAtUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var rows = new List<Dictionary<string, object?>>();

        foreach (var declaration in declarations)
        {
            var declaredAtLocal = declaration.DeclaredAtUtc.ToLocalTime();

            // The counted notes first, then the tender lines. One flat
            // table, because the printed form is one page and the
            // operator signs the bottom of it.
            foreach (var count in declaration.Counts.OrderBy(c => c.Key))
                rows.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["DeclaredAt"] = declaredAtLocal,
                    ["Kind"] = "Denomination",
                    ["Detail"] = count.Key,
                    ["Count"] = count.Count,
                    ["Expected"] = null,
                    ["Declared"] = count.Extended.Amount,
                    ["Variance"] = null
                });

            foreach (var line in declaration.Lines)
                rows.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["DeclaredAt"] = declaredAtLocal,
                    ["Kind"] = "Tender",
                    ["Detail"] = line.Tender.ToString(),
                    ["Count"] = null,
                    ["Expected"] = line.Expected.Amount,
                    ["Declared"] = line.Declared.Amount,
                    ["Variance"] = line.Variance.Amount
                });
        }

        return ReportBuilder
            .For("Cash Declaration Form")
            .Column("DeclaredAt", "Declared at", "dd/MM/yyyy HH:mm")
            .Column("Kind", "Kind")
            .Column("Detail", "Detail")
            .Column("Count", "Count", rightAlign: true)
            .Column("Expected", "Expected", "N2", rightAlign: true)
            .Column("Declared", "Declared", "N2", rightAlign: true)
            .Column("Variance", "Variance", "N2", rightAlign: true)
            .Rows(rows)
            .Total("Variance", declarations
                .Sum(d => d.TotalVariance.Amount))
            .Total("Count", declarations.Count)
            .Build(r.Provenance);
    }
}

/// <summary>
/// Reversed dispensings, with the pharmacist who reversed each one. A
/// clinical report rather than an operational one, so it sits behind the
/// clinical reporting permission.
/// </summary>
public sealed class ReversedScriptsReport : IReportDefinition
{
    public string Key => "ReversedScripts";

    private readonly PharmaXDbContext _db;

    public ReversedScriptsReport(PharmaXDbContext db) => _db = db;

    public async Task<ReportResult> RunAsync(ResolvedRequest r,
                                             CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(r);

        var query = _db.Dispensings
            .AsNoTracking()
            .Where(d => d.Status == Domain.Dispensing.DispensingStatus.Reversed)
            .Where(d => d.UpdatedAtUtc >= r.FromUtc
                        && d.UpdatedAtUtc < r.ToExclusiveUtc);

        if (r.Guid("pharmacist") is Guid pharmacistId)
            query = query.Where(d => d.PharmacistId == pharmacistId);

        var dispensings = await query
            .OrderBy(d => d.UpdatedAtUtc)
            .Select(d => new
            {
                d.Id,
                d.DocumentNumber,
                d.ScriptId,
                d.PatientId,
                d.PharmacistId,
                d.IssuedAtUtc,
                ReversedAtUtc = d.UpdatedAtUtc,
                Items = d.Lines.Count
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var scriptNumbers = await _db.Scripts
            .AsNoTracking()
            .Select(s => new { s.Id, s.ScriptNumber })
            .ToDictionaryAsync(s => s.Id, s => s.ScriptNumber, ct)
            .ConfigureAwait(false);

        return ReportBuilder
            .For("Reversed Scripts")
            .Column("ReversedAt", "Reversed", "dd/MM/yyyy HH:mm")
            .Column("Document", "Dispensing")
            .Column("Script", "Script")
            .Column("Pharmacist", "Pharmacist")
            .Column("Items", "Items", rightAlign: true)
            .Column("IssuedAt", "Originally issued", "dd/MM/yyyy HH:mm")
            .Rows(dispensings.Select(d =>
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["ReversedAt"] = d.ReversedAtUtc.ToLocalTime(),
                    ["Document"] = d.DocumentNumber,
                    ["Script"] = scriptNumbers.TryGetValue(d.ScriptId,
                        out var number) ? number : "",
                    ["Pharmacist"] = d.PharmacistId is Guid id
                        ? r.OperatorName(id)
                        : "",
                    ["Items"] = d.Items,
                    ["IssuedAt"] = d.IssuedAtUtc?.ToLocalTime()
                }))
            .Total("Count", dispensings.Count)
            .Build(r.Provenance);
    }
}
