using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Reporting;
using PharmaX.Persistence;
using PharmaX.Reporting.Engine;

namespace PharmaX.Reporting.Definitions;

/// <summary>
/// Every retail price change in the period, with who made it and what
/// the price moved from and to.
/// </summary>
/// <remarks>
/// Sourced from the audit trail rather than from ProductPrice. A
/// ProductPrice row is a pricing rule that is in force or is not - it
/// carries no record of when it was changed or by whom - whereas the
/// audit entry ProductService writes on a price change carries exactly
/// that, and is append-only, which is what makes this answerable to a
/// question about who put the price up.
/// </remarks>
public sealed class PriceChangesReport : IReportDefinition
{
    public string Key => "PriceChanges";

    /// The action ProductService records against a retail price change.
    private const string PriceChangeAction = "inv.price.change";

    private readonly PharmaXDbContext _db;

    public PriceChangesReport(PharmaXDbContext db) => _db = db;

    public async Task<ReportResult> RunAsync(ResolvedRequest r,
                                             CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(r);

        var query = _db.AuditEntries
            .AsNoTracking()
            .Where(a => a.Action == PriceChangeAction)
            .Where(a => a.OccurredAtUtc >= r.FromUtc
                        && a.OccurredAtUtc < r.ToExclusiveUtc);

        if (r.Guid("operator") is Guid who)
            query = query.Where(a => a.UserId == who);

        // Left-joined to the product: a product discontinued since the
        // change still has to appear, or the trail has a hole in it.
        var rows = await query
            .OrderByDescending(a => a.OccurredAtUtc)
            .GroupJoin(_db.Products.AsNoTracking(),
                       a => a.EntityId,
                       p => p.Id,
                       (a, ps) => new { Audit = a, Products = ps })
            .SelectMany(x => x.Products.DefaultIfEmpty(),
                        (x, p) => new
                        {
                            x.Audit.OccurredAtUtc,
                            x.Audit.UserId,
                            x.Audit.Reason,
                            x.Audit.Outcome,
                            Code = p != null ? p.Code : "",
                            Description = p != null ? p.Description
                                                    : "(product removed)"
                        })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return ReportBuilder
            .For("Price Changes")
            .Column("OccurredAt", "Date and time", "dd/MM/yyyy HH:mm")
            .Column("Code", "Code")
            .Column("Description", "Item")
            .Column("Change", "Price change")
            .Column("Operator", "Changed by")
            .Column("Outcome", "Outcome")
            .Rows(rows.Select(x => new Dictionary<string, object?>
            {
                ["OccurredAt"] = x.OccurredAtUtc.ToLocalTime(),
                ["Code"] = x.Code,
                ["Description"] = x.Description,
                ["Change"] = x.Reason ?? "",
                ["Operator"] = r.OperatorName(x.UserId),
                ["Outcome"] = x.Outcome
            }))
            .Total("Changes", rows.Count)
            .Build(r.Provenance);
    }
}
