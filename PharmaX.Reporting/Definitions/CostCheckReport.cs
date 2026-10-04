using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Reporting;
using PharmaX.Domain.Inventory;
using PharmaX.Persistence;
using PharmaX.Reporting.Engine;

namespace PharmaX.Reporting.Definitions;

/// <summary>
/// Products whose cost does not look like the cost of the pack they are
/// priced as: no cost at all, under a tenth of the price, or at or above
/// it.
/// </summary>
/// <remarks>
/// Every margin the sales reports work out stands on these costs, and a
/// supplier's list that gives the cost of one tablet against the price of
/// a thousand reads as a 99.9% margin and nothing else looks wrong: 121
/// lines of the pharmacy's own price list did exactly that (OCT-OBS-07).
/// The price list import counts them into the log; this is the list
/// somebody can actually work through, highest margin first. Nothing here
/// changes a price - which figure is wrong is the pharmacy's to say.
/// </remarks>
public sealed class CostCheckReport : IReportDefinition
{
    public string Key => "CostCheck";

    private readonly PharmaXDbContext _db;

    public CostCheckReport(PharmaXDbContext db) => _db = db;

    public async Task<ReportResult> RunAsync(ResolvedRequest r,
                                             CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(r);

        // Materialised and then judged: the rule is the product's own,
        // so this list and the import's count cannot disagree.
        var products = await _db.Products
            .AsNoTracking()
            .Where(p => p.IsActive)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var department = r.Guid("department");
        var category = r.Guid("category");

        var rows = products
            .Where(p => p.CostNeedsChecking)
            .Where(p => department is null || p.DepartmentId == department)
            .Where(p => category is null || p.CategoryId == category)
            .Select(p => new
            {
                p.Code,
                p.Description,
                Pack = p.PackSize ?? "",
                Cost = p.CostPrice.Amount,
                Price = p.RetailPrice.Amount,
                Margin = (p.RetailPrice.Amount - p.CostPrice.Amount)
                         / p.RetailPrice.Amount * 100m,
                Why = Why(p)
            })
            .OrderByDescending(x => x.Margin)
            .ThenBy(x => x.Description, StringComparer.CurrentCulture)
            .ToList();

        return ReportBuilder
            .For("Cost Prices to Check")
            .Column("Code", "Code")
            .Column("Description", "Description")
            .Column("Pack", "Pack")
            .Column("Cost", "Cost", "N2", rightAlign: true)
            .Column("Price", "Price", "N2", rightAlign: true)
            .Column("Margin", "Margin %", "N1", rightAlign: true)
            .Column("Why", "Why it is listed")
            .Rows(rows.Select(x => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Code"] = x.Code,
                ["Description"] = x.Description,
                ["Pack"] = x.Pack,
                ["Cost"] = x.Cost,
                ["Price"] = x.Price,
                ["Margin"] = x.Margin,
                ["Why"] = x.Why
            }))
            .Total("Count", rows.Count)
            .Build(r.Provenance);
    }

    private static string Why(Product p) =>
        p.CostPrice.Amount <= 0m
            ? "No cost recorded"
            : p.CostPrice.Amount >= p.RetailPrice.Amount
                ? "Costs as much as it sells for, or more"
                : "Cost under a tenth of the price - the cost of one unit?";
}
