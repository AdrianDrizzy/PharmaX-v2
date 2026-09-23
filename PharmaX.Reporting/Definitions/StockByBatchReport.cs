using PharmaX.Application.Reporting;
using PharmaX.Persistence;
using PharmaX.Reporting.Engine;
using Microsoft.EntityFrameworkCore;

namespace PharmaX.Reporting.Definitions;

/// <summary>
/// Everything on the shelf, a batch at a time: what it is, which batch it
/// came in on, how much of it is left and the date it stops being
/// sellable.
/// </summary>
/// <remarks>
/// Separate from StockOnHandReport rather than folded into it. That one
/// answers "what is the stock worth", one row per product, and a
/// valuation that suddenly had four rows for the same product would
/// double-count in the eye of anyone reading it. This one answers "what
/// is actually in the dispensary and how long have we got", which is a
/// different question with a different row.
///
/// A product that is not batch tracked still appears, once, with no batch
/// and no expiry against it. Leaving it out would make the report a list
/// of batches rather than a list of stock, and the front shop - which is
/// most of the shelf by line count - would silently vanish from it.
/// </remarks>
public sealed class StockByBatchReport : IReportDefinition
{
    public string Key => "StockByBatch";

    private readonly PharmaXDbContext _db;

    public StockByBatchReport(PharmaXDbContext db) => _db = db;

    public async Task<ReportResult> RunAsync(ResolvedRequest r,
                                             CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(r);

        var department = r.Guid("department");
        var category = r.Guid("category");

        var products = await _db.Products
            .AsNoTracking()
            .Where(p => p.IsActive)
            .Where(p => department == null || p.DepartmentId == department)
            .Where(p => category == null || p.CategoryId == category)
            .Select(p => new
            {
                p.Id,
                p.Code,
                p.Description,
                p.IsBatchTracked,
                p.QuantityOnHand,
                Cost = p.CostPrice.Amount
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var batches = await _db.Batches
            .AsNoTracking()
            .Select(b => new
            {
                b.ProductId,
                b.BatchNumber,
                b.ExpiryDate,
                b.QuantityOnHand,
                Cost = b.CostPrice.Amount
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var byProduct = batches.ToLookup(b => b.ProductId);
        var today = r.To;

        var rows = new List<Row>();

        foreach (var product in products.OrderBy(p => p.Description,
                     StringComparer.CurrentCulture))
        {
            var found = byProduct[product.Id].ToList();

            if (found.Count == 0)
            {
                // Not batch tracked, or tracked and never received. The
                // product's own balance is all there is to report.
                rows.Add(new Row(product.Code, product.Description, null,
                    null, null, product.QuantityOnHand, product.Cost));

                continue;
            }

            foreach (var batch in found.OrderBy(b => b.ExpiryDate))
            {
                rows.Add(new Row(product.Code, product.Description,
                    batch.BatchNumber, batch.ExpiryDate,
                    batch.ExpiryDate.DayNumber - today.DayNumber,
                    batch.QuantityOnHand, batch.Cost));
            }
        }

        // Stock means stock. A batch counted down to nothing is history,
        // and history belongs in the movement report, so it is off unless
        // it is asked for.
        if (!r.Bool("includeZero"))
            rows = rows.Where(x => x.Quantity > 0m).ToList();

        // The expiry slicers. Both are about dates, so both drop anything
        // with no expiry to read - which is every untracked line.
        if (r.Bool("expiringOnly"))
            rows = rows.Where(x => x.Expiry is not null).ToList();

        if (r.Decimal("withinDays") is decimal days)
        {
            var horizon = today.AddDays((int)days);

            rows = rows
                .Where(x => x.Expiry is DateOnly e && e <= horizon)
                .ToList();
        }

        // Soonest to expire first: the only ordering anyone reading this
        // report is looking for. Untracked lines have no date and sort to
        // the end rather than to the top.
        rows = rows
            .OrderBy(x => x.Expiry ?? DateOnly.MaxValue)
            .ThenBy(x => x.Description, StringComparer.CurrentCulture)
            .ToList();

        return ReportBuilder
            .For("Stock On Hand by Batch")
            .Column("Code", "Code")
            .Column("Description", "Description")
            .Column("Batch", "Batch")
            .Column("Expiry", "Expires", "dd/MM/yyyy")
            .Column("Days", "Days left", "N0", rightAlign: true)
            .Column("OnHand", "On hand", "N3", rightAlign: true)
            .Column("Cost", "Unit cost", "N2", rightAlign: true)
            .Column("Value", "Value at cost", "N2", rightAlign: true)
            .Rows(rows.Select(x => new Dictionary<string, object?>
            {
                ["Code"] = x.Code,
                ["Description"] = x.Description,
                ["Batch"] = x.Batch,
                ["Expiry"] = x.Expiry?.ToDateTime(TimeOnly.MinValue),
                ["Days"] = x.DaysLeft,
                ["OnHand"] = x.Quantity,
                ["Cost"] = x.Cost,
                ["Value"] = x.Quantity * x.Cost
            }))
            .Total("Value", rows.Sum(x => x.Quantity * x.Cost))
            .Total("OnHand", rows.Sum(x => x.Quantity))
            .Total("Count", rows.Count)

            // Counted separately because it is the number the report is
            // usually run to find, and reading it off the grid means
            // counting rows by eye.
            .Total("Expired", rows.Count(x => x.DaysLeft < 0))
            .Build(r.Provenance);
    }

    private sealed record Row(string Code, string Description, string? Batch,
        DateOnly? Expiry, int? DaysLeft, decimal Quantity, decimal Cost);
}
