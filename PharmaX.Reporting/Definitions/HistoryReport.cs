using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Reporting;
using PharmaX.Domain.Dispensing;
using PharmaX.Domain.Sales;
using PharmaX.Persistence;
using PharmaX.Reporting.Engine;

namespace PharmaX.Reporting.Definitions;

/// <summary>
/// Sales, dispensing, till overrides and cash-ups over any period, as
/// lines or grouped - the history imported from the system before
/// PharmaX included, because it is held in the same tables.
/// </summary>
/// <remarks>
/// Fixed slicers choose what: "kind" (Sale, Script, Override, NoSale,
/// DayEnd) and "groupBy" (product, day, month, party, operator, or none
/// for the lines themselves). "Party" is the tender of a sale, the doctor
/// of a script, the authoriser of an override, the tender of a cash-up.
/// Small enough to group in memory: a year of the pharmacy's sales is
/// forty thousand lines.
/// </remarks>
public sealed class HistoryReport : IReportDefinition
{
    public string Key => "History";

    private readonly PharmaXDbContext _db;

    public HistoryReport(PharmaXDbContext db) => _db = db;

    private sealed record Line(DateTime When, string Code, string Item,
                               string Party, string Operator, decimal Quantity, decimal Value);

    public async Task<ReportResult> RunAsync(ResolvedRequest r, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(r);

        var kind = r.String("kind") ?? "Sale";
        var groupBy = r.String("groupBy") ?? "none";
        var lines = new List<Line>();

        var names = await _db.Products.AsNoTracking()
            .Select(p => new { p.Id, p.Code, p.Description })
            .ToListAsync(ct).ConfigureAwait(false);
        var byId = names.ToDictionary(p => p.Id);

        if (kind == "Sale")
        {
            var sales = await _db.Sales.AsNoTracking()
                .Where(s => s.Status == SaleStatus.Finalised
                            && s.OccurredAtUtc >= r.FromUtc && s.OccurredAtUtc < r.ToExclusiveUtc)
                .Include(s => s.Lines).Include(s => s.Payments)
                .ToListAsync(ct).ConfigureAwait(false);

            lines.AddRange(sales.SelectMany(s => s.Lines.Where(l => !l.IsVoided).Select(l => new Line(
                s.OccurredAtUtc.ToLocalTime(), byId.TryGetValue(l.ProductId, out var p) ? p.Code : "",
                l.Description, s.Payments.FirstOrDefault()?.Tender.DisplayName() ?? "",
                r.OperatorName(s.OperatorId), l.Quantity, l.LineTotal.Amount))));
        }

        if (kind == "Script")
        {
            var issued = await _db.Dispensings.AsNoTracking()
                .Where(x => x.Status == DispensingStatus.Issued
                            && x.IssuedAtUtc >= r.FromUtc && x.IssuedAtUtc < r.ToExclusiveUtc)
                .Include(x => x.Lines)
                .ToListAsync(ct).ConfigureAwait(false);

            var scriptIds = issued.Select(x => x.ScriptId).Distinct().ToList();
            var prescribers = await _db.Scripts.AsNoTracking()
                .Where(s => scriptIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.PrescriberName, ct).ConfigureAwait(false);

            lines.AddRange(issued.SelectMany(x => x.Lines.Select(l => new Line(
                x.IssuedAtUtc!.Value.ToLocalTime(),
                byId.TryGetValue(l.ProductId, out var p) ? p.Code : "", l.Description,
                prescribers.GetValueOrDefault(x.ScriptId, ""),
                r.OperatorName(x.PharmacistId ?? Guid.Empty), l.Quantity, l.TariffAmount.Amount))));
        }

        if (kind is "Override" or "NoSale")
        {
            // A price brought down at the till is a line discount; the
            // drawer opened with nothing sold is logged by the till
            // session. Both from the audit trail.
            var action = kind == "Override" ? "pos.sale.discount" : "pos.drawer.nosale";

            var audits = await _db.AuditEntries.AsNoTracking()
                .Where(a => a.Action == action
                            && a.OccurredAtUtc >= r.FromUtc && a.OccurredAtUtc < r.ToExclusiveUtc)
                .ToListAsync(ct).ConfigureAwait(false);

            var ids = audits.Select(a => a.EntityId).ToList();
            var discounted = await _db.Sales.AsNoTracking()
                .SelectMany(s => s.Lines)
                .Where(l => ids.Contains(l.Id))
                .Select(l => new { l.Id, l.ProductId, l.Description, l.Quantity, Discount = l.DiscountAmount.Amount })
                .ToDictionaryAsync(l => l.Id, ct).ConfigureAwait(false);

            lines.AddRange(audits.Select(a => discounted.TryGetValue(a.EntityId, out var l)
                ? new Line(a.OccurredAtUtc.ToLocalTime(),
                    byId.TryGetValue(l.ProductId, out var p) ? p.Code : "", l.Description,
                    a.Reason ?? "", r.OperatorName(a.UserId), l.Quantity, l.Discount)
                : byId.TryGetValue(a.EntityId, out var product)
                    ? new Line(a.OccurredAtUtc.ToLocalTime(), product.Code, product.Description,
                        a.Reason ?? "", r.OperatorName(a.UserId), 1m, 0m)
                    : new Line(a.OccurredAtUtc.ToLocalTime(), "", a.Reason ?? "",
                        "", r.OperatorName(a.UserId), 1m, 0m)));
        }

        if (kind == "DayEnd")
        {
            // Each tender counted at a cash-up: what the till expected,
            // what was counted, and the difference as the value.
            var declarations = await _db.TillDeclarations.AsNoTracking()
                .Where(d => d.IsFinal
                            && d.DeclaredAtUtc >= r.FromUtc && d.DeclaredAtUtc < r.ToExclusiveUtc)
                .ToListAsync(ct).ConfigureAwait(false);

            lines.AddRange(declarations.SelectMany(d => d.Lines.Select(l => new Line(
                d.DeclaredAtUtc.ToLocalTime(), "",
                string.Create(CultureInfo.CurrentCulture,
                    $"Expected {l.Expected}, counted {l.Declared}"),
                l.Tender.DisplayName(), r.OperatorName(d.DeclaredBy), 1m, l.Variance.Amount))));
        }

        var builder = groupBy switch
        {
            "none" => Detail(lines),
            _ => Grouped(lines, groupBy)
        };

        return builder
            .Total("Quantity", lines.Sum(l => l.Quantity))
            .Total("Value", lines.Sum(l => l.Value))
            .Build(r.Provenance);
    }

    private static ReportBuilder Detail(List<Line> lines) =>
        ReportBuilder.For("History")
            .Column("When", "Date and time", "dd/MM/yyyy HH:mm")
            .Column("Code", "Code")
            .Column("Item", "Item")
            .Column("Party", "Doctor, tender or authoriser")
            .Column("Quantity", "Qty", "0.###", rightAlign: true)
            .Column("Value", "Value", "N2", rightAlign: true)
            .Column("Operator", "By")
            .Rows(lines.OrderBy(l => l.When).Select(l => new Dictionary<string, object?>
            {
                ["When"] = l.When, ["Code"] = l.Code, ["Item"] = l.Item, ["Party"] = l.Party,
                ["Quantity"] = l.Quantity, ["Value"] = l.Value, ["Operator"] = l.Operator
            }));

    private static ReportBuilder Grouped(List<Line> lines, string groupBy)
    {
        var header = groupBy switch
        {
            "product" => "Item", "day" => "Day", "month" => "Month", "operator" => "By",
            _ => "Doctor, tender or authoriser"
        };

        string Key(Line l) => groupBy switch
        {
            "product" => l.Code.Length > 0 ? $"{l.Code}  {l.Item}" : l.Item,
            "day" => l.When.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            "month" => l.When.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            "operator" => l.Operator,
            _ => l.Party
        };

        // Products by value, highest first: the top sellers. Dates in order.
        var groups = lines.GroupBy(Key).Select(g => new
        {
            g.Key,
            Lines = g.Count(),
            Quantity = g.Sum(l => l.Quantity),
            Value = g.Sum(l => l.Value)
        });

        groups = groupBy is "day" or "month"
            ? groups.OrderBy(g => g.Key, StringComparer.Ordinal)
            : groups.OrderByDescending(g => g.Value).ThenByDescending(g => g.Quantity);

        return ReportBuilder.For("History")
            .Column("Key", header)
            .Column("Lines", "Lines", "N0", rightAlign: true)
            .Column("Quantity", "Qty", "N0", rightAlign: true)
            .Column("Value", "Value", "N2", rightAlign: true)
            .Rows(groups.Select(g => new Dictionary<string, object?>
            {
                ["Key"] = g.Key, ["Lines"] = g.Lines, ["Quantity"] = g.Quantity, ["Value"] = g.Value
            }));
    }
}
