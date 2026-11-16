using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Reporting;
using PharmaX.Domain.Patients;
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
/// <remarks>
/// Refunds and credit notes are in it as negative lines, the way Daily
/// Sales (Line Items) has them, so that the two reports of the same day
/// come to the same figure: goods put back on the shelf bring their
/// cost back with them, goods written off and credit given without goods
/// do not.
/// </remarks>
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

        // What the period gave back. Left out, Daily Sales came to more
        // than Daily Sales (Line Items) for the same day by exactly the
        // day's refunds, and its margin was struck on money the pharmacy
        // had already handed back (OCT-DEF-03).
        var returns = await _db.SaleReturns
            .AsNoTracking()
            .Where(x => x.OccurredAtUtc >= r.FromUtc
                        && x.OccurredAtUtc < r.ToExclusiveUtc)
            .Where(x => x.Status == ReturnStatus.Finalised)
            .Include(x => x.Lines)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // A refund of a collected script gives a co-payment back, and has
        // no more cost to set against it than the collection did.
        var refundedCollections = await CollectionsAsync(returns
                .Select(x => x.OriginalSaleId).Distinct().ToList(), ct)
            .ConfigureAwait(false);

        // Cost comes from the product rather than the line, because a
        // sale line records what was charged and not what it cost.
        var products = await _db.Products
            .AsNoTracking()
            .Select(p => new
            {
                p.Id,
                Cost = p.CostPrice.Amount,
                p.DepartmentId,
                p.CategoryId
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var costs = products.ToDictionary(p => p.Id, p => p.Cost);

        // Where each product is filed, by name, for the department and
        // category groupings. Inactive lists too: last year's sales were
        // filed under them.
        var departmentNames = await _db.Departments.AsNoTracking()
            .ToDictionaryAsync(d => d.Id, d => d.Name, ct).ConfigureAwait(false);
        var categoryNames = await _db.Categories.AsNoTracking()
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct).ConfigureAwait(false);
        var filedUnder = products.ToDictionary(p => p.Id, p => (p.DepartmentId, p.CategoryId));

        string Filed(Guid productId, bool byDepartment)
        {
            if (!filedUnder.TryGetValue(productId, out var filed)) return Unfiled;
            var id = byDepartment ? filed.DepartmentId : filed.CategoryId;
            var names = byDepartment ? departmentNames : categoryNames;
            return id is Guid g && names.TryGetValue(g, out var name) ? name : Unfiled;
        }

        // The presets have offered a department and a category since they
        // were written, and this report never read either: a manager who
        // asked for one department's daily takings was shown the whole
        // shop's, labelled as filtered.
        var department = r.Guid("department");
        var category = r.Guid("category");

        var filtered = department is not null || category is not null;

        var inScope = products
            .Where(p => (department is null || p.DepartmentId == department)
                        && (category is null || p.CategoryId == category))
            .Select(p => p.Id)
            .ToHashSet();

        var lines = sales
            .SelectMany(s => s.Lines
                .Where(l => !l.IsVoided)
                .Where(l => !filtered || inScope.Contains(l.ProductId))
                .Select(l => new SoldLine(
                    s.OperatorId,
                    DateOnly.FromDateTime(s.OccurredAtUtc.ToLocalTime()),
                    s.OccurredAtUtc.ToLocalTime(),
                    l.ProductId,
                    l.Description,
                    l.Quantity,
                    l.LineTotal.Amount,

                    // A collection's line is the patient's share of a
                    // script, not the item's price, so the medicine's cost
                    // cannot be set against it - it was bought against the
                    // dispensing. Charged in full against the co-payment,
                    // it reported a loss on every script collected, and a
                    // day with a few scripts on it read as a day the
                    // pharmacy had traded at a loss. Daily Sales (Line
                    // Items) leaves the same lines out of its cost.
                    l.IsScript,
                    l.IsScript ? 0m : Cost(costs, l.ProductId, l.Quantity),
                    IsRefund: false)))
            .Concat(returns.SelectMany(x => x.Lines
                .Where(l => !filtered || inScope.Contains(l.ProductId))
                .Select(l =>
                {
                    var coPayment = refundedCollections.Contains(x.OriginalSaleId);
                    var restocked = x.Kind == ReturnKind.GoodsReturn
                                    && l.RestockToShelf;

                    return new SoldLine(
                        x.OperatorId,
                        DateOnly.FromDateTime(x.OccurredAtUtc.ToLocalTime()),
                        x.OccurredAtUtc.ToLocalTime(),
                        l.ProductId,
                        l.Description,
                        -l.Quantity,
                        -l.LineTotal.Amount,
                        coPayment,
                        coPayment || !restocked
                            ? 0m
                            : -Cost(costs, l.ProductId, l.Quantity),
                        IsRefund: true);
                })))
            .ToList();

        // What a group sold, and what the part of it the pharmacy can
        // cost actually cost.
        Dictionary<string, object?> Of(string group, IEnumerable<SoldLine> sold)
        {
            var items = sold.ToList();

            return Row(group,
                items.Sum(x => x.Quantity),
                items.Sum(x => x.Gross),
                items.Where(x => x.IsCoPayment).Sum(x => x.Gross),
                items.Where(x => !x.IsCoPayment).Sum(x => x.Cost));
        }

        var groupBy = r.String("groupBy") ?? "operator";

        var rows = groupBy switch
        {
            // A co-payment has no cost to mark up, so it is banded as
            // what it is rather than counted as pure margin; a refund is
            // trade given back rather than trade at a markup.
            "markupBand" => lines
                .GroupBy(l => l.IsCoPayment
                    ? CoPaymentBand
                    : l.IsRefund
                        ? RefundBand
                        : Band(l.Gross, l.Cost))
                .Select(g => Of(g.Key, g))
                .OrderBy(row => (string)row["Group"]!)
                .ToList(),

            // Turnover by trading day and by month, in the till's own
            // time, which is the day the pharmacy thinks it traded on.
            "day" => lines
                .GroupBy(l => l.Day)
                .OrderBy(g => g.Key)
                .Select(g => Of(g.Key.ToString("yyyy-MM-dd",
                    CultureInfo.InvariantCulture), g))
                .ToList(),

            "month" => lines
                .GroupBy(l => new DateOnly(l.Day.Year, l.Day.Month, 1))
                .OrderBy(g => g.Key)
                .Select(g => Of(g.Key.ToString("yyyy-MM",
                    CultureInfo.InvariantCulture), g))
                .ToList(),

            // Profit by Product lists the most profitable first, as its
            // graph does; the plain product list, the best sellers.
            "product" => lines
                .GroupBy(l => l.Description)
                .Select(g => Of(g.Key, g))
                .OrderByDescending(row => (decimal)row[
                    r.String("chart") == "profit" ? "Margin" : "Sales"]!)
                .ToList(),

            "department" or "category" => lines
                .GroupBy(l => Filed(l.ProductId, groupBy == "department"))
                .Select(g => Of(g.Key, g))
                .OrderByDescending(row => (decimal)row["Sales"]!)
                .ToList(),

            // Which days of the week and hours of the day are busy, in
            // the order of the week and the clock rather than by size.
            "weekday" => lines
                .GroupBy(l => ((int)l.Day.DayOfWeek + 6) % 7)
                .OrderBy(g => g.Key)
                .Select(g => Of(CultureInfo.CurrentCulture.DateTimeFormat
                    .GetDayName((DayOfWeek)((g.Key + 1) % 7)), g))
                .ToList(),

            "hour" => lines
                .GroupBy(l => l.At.Hour)
                .OrderBy(g => g.Key)
                .Select(g => Of(string.Format(CultureInfo.InvariantCulture,
                    "{0:00}:00", g.Key), g))
                .ToList(),

            _ => lines
                .GroupBy(l => l.OperatorId)
                .Select(g => Of(r.OperatorName(g.Key), g))
                .OrderByDescending(row => (decimal)row["Sales"]!)
                .ToList()
        };

        var title = groupBy switch
        {
            "markupBand" => "Sales Summary (By Markup %)",
            "day" => "Daily Sales",
            "month" => "Monthly Sales",
            "product" => "Sales and Profit by Product",
            "department" => "Sales and Profit by Department",
            "category" => "Sales and Profit by Category",
            "weekday" => "Sales by Day of the Week",
            "hour" => "Sales by Hour of the Day",
            _ => "Sales Analysis"
        };

        var salesTotal = rows.Sum(x => (decimal)x["Sales"]!);

        // Account payments at the till are not product lines, but the
        // cash-up counts them in net takings. Named on Daily Sales so
        // the day's figure is not short of the till cash-up.
        var accountPayments = 0m;
        if (groupBy == "day")
        {
            accountPayments = (await _db.AccountTransactions
                .AsNoTracking()
                .Where(t => t.Kind == AccountEntryKind.Payment
                            && t.Tender != null
                            && t.TillSessionId != null)
                .Where(t => t.CreatedAtUtc >= r.FromUtc
                            && t.CreatedAtUtc < r.ToExclusiveUtc)
                .Select(t => t.Amount.Amount)
                .ToListAsync(ct)
                .ConfigureAwait(false))
                .Sum();
        }

        var result = ReportBuilder
            .For(title)
            .Column("Group", GroupHeader(groupBy))
            .Column("Quantity", "Units", "N3", rightAlign: true)
            .Column("Sales", "Sales", "N2", rightAlign: true)

            // What was sold over the counter: the sales less the script
            // co-payments.
            .Column("Counter", "Over the counter", "N2", rightAlign: true)

            // Named on the report rather than quietly netted off, so a
            // manager can see why the margin is struck on less than the
            // sales: the co-payment's medicine was costed against the
            // dispensing that issued it.
            .Column("Scripts", "Script co-pay", "N2", rightAlign: true)
            .Column("Cost", "Cost", "N2", rightAlign: true)
            // Profit is the selling price less the cost price, on what
            // the pharmacy can cost (the co-payments are left out).
            .Column("Margin", "Profit", "N2", rightAlign: true)
            .Column("MarginPercent", "Profit %", "N1", rightAlign: true)
            .Rows(rows)
            .Total("Sales", salesTotal)
            .Total("Counter", rows.Sum(x => (decimal)x["Counter"]!))
            .Total("Scripts", rows.Sum(x => (decimal)x["Scripts"]!))
            .Total("Cost", rows.Sum(x => (decimal)x["Cost"]!))
            .Total("Margin", rows.Sum(x => (decimal)x["Margin"]!))
            .Total("Count", rows.Count)
            .Total("AccountPayments", accountPayments)
            .Total("NetTakings", salesTotal + accountPayments)
            .Build(r.Provenance);

        if (accountPayments != 0m)
        {
            result = result with
            {
                Figures = new[]
                {
                    new ReportFigure("Account payments",
                        accountPayments.ToString("N2", CultureInfo.CurrentCulture)),
                    new ReportFigure("Net takings",
                        (salesTotal + accountPayments)
                            .ToString("N2", CultureInfo.CurrentCulture))
                }
            };
        }

        // A profit report graphs the profit; the others the sales.
        return string.Equals(r.String("chart"), "profit", StringComparison.Ordinal)
            ? result with { Chart = Exporters.ReportCharts.Suggest(result, "Margin") }
            : result;
    }

    private static decimal Cost(IReadOnlyDictionary<Guid, decimal> costs,
        Guid productId, decimal quantity) =>
        costs.TryGetValue(productId, out var unit) ? unit * quantity : 0m;

    /// <param name="scripts">
    /// The script co-payments inside <paramref name="sales"/>, which
    /// carry no cost and so are struck out of the margin.
    /// </param>
    private static Dictionary<string, object?> Row(string group,
        decimal quantity, decimal sales, decimal scripts, decimal cost)
    {
        // The turnover this report can cost: everything but the
        // co-payments. A margin taken on the whole of the sales would be
        // wrong either way round - the cost missing, or the co-payment
        // counted as pure profit.
        var costed = sales - scripts;
        var margin = costed - cost;

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Group"] = group,
            ["Quantity"] = quantity,
            ["Sales"] = sales,
            ["Counter"] = costed,
            ["Scripts"] = scripts,
            ["Cost"] = cost,
            ["Margin"] = margin,
            ["MarginPercent"] = costed == 0m ? 0m : margin / costed * 100m
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
        "day" => "Day",
        "month" => "Month",
        "department" => "Department",
        "category" => "Category",
        "weekday" => "Day of week",
        "hour" => "Hour",
        _ => "Operator"
    };

    /// Sorted first, because it is the band that is not a markup.
    private const string CoPaymentBand = "0. Script co-payment";

    /// Sorted last, after the bands of the trade it gives back.
    private const string RefundBand = "7. Refunds and credit notes";

    /// A product filed under no department or category, or one since removed.
    private const string Unfiled = "(not filed)";

    /// The sales, of those given, that collected a script.
    private async Task<HashSet<Guid>> CollectionsAsync(
        IReadOnlyCollection<Guid> saleIds, CancellationToken ct) =>
        saleIds.Count == 0
            ? new HashSet<Guid>()
            : (await _db.Sales
                .AsNoTracking()
                .Where(s => saleIds.Contains(s.Id) && s.DispensingId != null)
                .Select(s => s.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false))
              .ToHashSet();

    /// <summary>
    /// One sold or refunded line, lifted off the document it is on.
    /// </summary>
    /// <param name="IsCoPayment">
    /// True where the sale was a script collection, or the refund gave
    /// one back: the line is the patient's share of a dispensing, not the
    /// item's own price.
    /// </param>
    /// <param name="Cost">
    /// What the line cost the pharmacy: nothing for a co-payment, and on
    /// a refund only what went back on the shelf, as a negative.
    /// </param>
    /// <param name="IsRefund">
    /// True for a line of a refund or credit note, whose quantity and
    /// amount are negative.
    /// </param>
    private sealed record SoldLine(Guid OperatorId, DateOnly Day, DateTime At,
        Guid ProductId, string Description, decimal Quantity, decimal Gross,
        bool IsCoPayment, decimal Cost, bool IsRefund);
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

        // Who opened each session. A session stays open on its till until
        // somebody cashes it up, and that is not always who opened it.
        var sessionIds = declarations.Select(d => d.TillSessionId).Distinct().ToList();

        var openedBy = await _db.TillSessions
            .AsNoTracking()
            .Where(s => sessionIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.OpenedBy, ct)
            .ConfigureAwait(false);

        var rows = new List<Dictionary<string, object?>>();

        foreach (var declaration in declarations)
        {
            var declaredAtLocal = declaration.DeclaredAtUtc.ToLocalTime();
            var opener = openedBy.TryGetValue(declaration.TillSessionId, out var id)
                ? r.OperatorName(id)
                : "";

            // The counted notes first, then the tender lines. One flat
            // table, because the printed form is one page and the
            // operator signs the bottom of it.
            foreach (var count in declaration.Counts.OrderBy(c => c.Key))
                rows.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["DeclaredAt"] = declaredAtLocal,
                    ["OpenedBy"] = opener,
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
                    ["OpenedBy"] = opener,
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
            .Column("OpenedBy", "Session opened by")
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

        // When it was reversed, not when the row was last touched; a
        // dispensing reversed before the reversal was recorded falls back
        // to the latter.
        var query = _db.Dispensings
            .AsNoTracking()
            .Where(d => d.Status == Domain.Dispensing.DispensingStatus.Reversed)
            .Where(d => (d.ReversedAtUtc ?? d.UpdatedAtUtc) >= r.FromUtc
                        && (d.ReversedAtUtc ?? d.UpdatedAtUtc) < r.ToExclusiveUtc);

        if (r.Guid("pharmacist") is Guid pharmacistId)
            query = query.Where(d => d.PharmacistId == pharmacistId);

        // Word by word, like every search box: "wrong strength" finds
        // "Dispensed the wrong strength". The slicer was on the screen
        // and read by nothing, so whatever was typed in it was ignored.
        foreach (var word in Application.Abstractions.SearchTerms.Words(
                     r.String("reason")))
        {
            var pattern = $"%{word}%";

            query = query.Where(d => d.ReversalReason != null
                && EF.Functions.Like(d.ReversalReason, pattern));
        }

        var dispensings = await query
            .OrderBy(d => d.ReversedAtUtc ?? d.UpdatedAtUtc)
            .Select(d => new
            {
                d.Id,
                d.DocumentNumber,
                d.ScriptId,
                d.PatientId,
                d.PharmacistId,
                d.IssuedAtUtc,
                ReversedAtUtc = d.ReversedAtUtc ?? d.UpdatedAtUtc,
                d.ReversedBy,
                d.ReversalReason,
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
            .Column("Pharmacist", "Issued by")
            .Column("ReversedBy", "Reversed by")
            .Column("Reason", "Why")
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
                    ["ReversedBy"] = d.ReversedBy is Guid by
                        ? r.OperatorName(by)
                        : "",
                    ["Reason"] = d.ReversalReason ?? "",
                    ["Items"] = d.Items,
                    ["IssuedAt"] = d.IssuedAtUtc?.ToLocalTime()
                }))
            .Total("Count", dispensings.Count)
            .Build(r.Provenance);
    }
}
