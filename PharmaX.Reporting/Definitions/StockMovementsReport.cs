using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Reporting;
using PharmaX.Domain.Inventory;
using PharmaX.Persistence;
using PharmaX.Reporting.Engine;

namespace PharmaX.Reporting.Definitions;

/// <summary>
/// The stock ledger: every movement of every product, with the reason it
/// moved.
/// </summary>
/// <remarks>
/// One definition serves several menu entries the staff know by
/// different names - Transfers, Breakages and the full transaction list
/// are the same rows under different movement types, so they are presets
/// over this rather than three near-identical reports. The types wanted
/// arrive as a comma-separated "movementTypes" parameter; no value means
/// every type, which is what the full list wants.
/// </remarks>
public sealed class StockMovementsReport : IReportDefinition
{
    public string Key => "StockMovements";

    private readonly PharmaXDbContext _db;

    public StockMovementsReport(PharmaXDbContext db) => _db = db;

    public async Task<ReportResult> RunAsync(ResolvedRequest r,
                                             CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(r);

        // The preset fixes a set ("TransferIn,TransferOut"); the operator
        // may narrow the unfiltered list to a single type. Both are read
        // so one preset can serve the fixed views and the free one.
        var wanted = WantedTypes(r.String("movementTypes"));
        wanted.AddRange(WantedTypes(r.String("movementType")));

        var query = _db.StockMovements
            .AsNoTracking()
            .Where(m => m.OccurredAtUtc >= r.FromUtc
                        && m.OccurredAtUtc < r.ToExclusiveUtc);

        if (wanted.Count > 0)
            query = query.Where(m => wanted.Contains(m.MovementType));

        // Joined to the product so the row names the item rather than
        // showing an identifier nobody can read off a shelf.
        var rows = await query
            .OrderByDescending(m => m.OccurredAtUtc)
            .Join(_db.Products.AsNoTracking(),
                  m => m.ProductId,
                  p => p.Id,
                  (m, p) => new
                  {
                      m.OccurredAtUtc,
                      p.Code,
                      p.Description,
                      m.MovementType,
                      m.Quantity,
                      m.BalanceAfter,
                      m.ReasonCode,
                      m.SourceDocumentType
                  })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return ReportBuilder
            .For(Title(wanted))
            .Column("OccurredAt", "Date and time", "dd/MM/yyyy HH:mm")
            .Column("Code", "Code")
            .Column("Description", "Item")
            .Column("MovementType", "Type")
            .Column("Quantity", "Quantity", "N3", rightAlign: true)
            .Column("BalanceAfter", "On hand after", "N3", rightAlign: true)
            .Column("Reason", "Reason")
            .Column("Source", "Source")
            .Rows(rows.Select(x => new Dictionary<string, object?>
            {
                ["OccurredAt"] = x.OccurredAtUtc.ToLocalTime(),
                ["Code"] = x.Code,
                ["Description"] = x.Description,
                ["MovementType"] = Describe(x.MovementType),
                ["Quantity"] = x.Quantity,
                ["BalanceAfter"] = x.BalanceAfter,
                ["Reason"] = x.ReasonCode ?? "",
                ["Source"] = x.SourceDocumentType ?? ""
            }))
            .Total("Movements", rows.Count)
            .Total("Net quantity", rows.Sum(x => x.Quantity))
            .Build(r.Provenance);
    }

    private static List<MovementType> WantedTypes(string? csv)
    {
        var types = new List<MovementType>();
        if (string.IsNullOrWhiteSpace(csv)) return types;

        foreach (var name in csv.Split(',',
                     StringSplitOptions.RemoveEmptyEntries
                     | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<MovementType>(name, ignoreCase: true,
                                            out var parsed))
                types.Add(parsed);
        }

        return types;
    }

    /// The heading says which ledger this is, so an exported file still
    /// explains itself once it is away from the screen that ran it.
    private static string Title(IReadOnlyList<MovementType> wanted) =>
        wanted.Count switch
        {
            0 => "Stock Transactions",
            _ when wanted.Contains(MovementType.TransferIn)
                   || wanted.Contains(MovementType.TransferOut) => "Transfers",
            _ when wanted.Contains(MovementType.Disposal) => "Breakages and Disposals",
            _ when wanted.Contains(MovementType.ReturnToSupplier) => "Returns to Supplier",
            _ => "Stock Transactions"
        };

    private static string Describe(MovementType type) => type switch
    {
        MovementType.Receipt => "Receipt",
        MovementType.Issue => "Sale or issue",
        MovementType.Return => "Return",
        MovementType.Adjustment => "Adjustment",
        MovementType.TransferOut => "Transfer out",
        MovementType.TransferIn => "Transfer in",
        MovementType.StockTake => "Stock take",
        MovementType.Disposal => "Breakage or disposal",
        MovementType.ReturnToSupplier => "Returned to supplier",
        _ => type.ToString()
    };
}
