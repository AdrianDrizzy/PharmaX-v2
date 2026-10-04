using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Reporting;
using PharmaX.Domain.Pricing;
using PharmaX.Domain.Sales;
using PharmaX.Persistence;
using PharmaX.Reporting.Engine;

namespace PharmaX.Reporting.Definitions;

/// <summary>
/// Every transaction of the day or the month, line by line: when, which
/// receipt, who rang it up, who it was for, how it was paid, and each
/// item with its quantity, price, discount, VAT, cost and margin - with a
/// total for every day and, over a month, for the month.
/// </summary>
/// <remarks>
/// <para>
/// Daily and Monthly Sales say what the day took. This says what it was
/// made of, which is what a manager reconciling the drawer, answering a
/// customer, or checking a discount needs: the other two cannot be
/// traced back to a receipt.
/// </para>
/// <para>
/// Refunds and credit notes are in it, as negative lines, so that a day's
/// total here is what the day actually kept. Script collections are in it
/// at what the patient paid; their cost is left blank, because the
/// patient's share is not the whole price and the medicine's cost belongs
/// to the dispensing.
/// </para>
/// </remarks>
public sealed class SalesLinesReport : IReportDefinition
{
    public string Key => "SalesLines";

    private readonly PharmaXDbContext _db;

    public SalesLinesReport(PharmaXDbContext db) => _db = db;

    public async Task<ReportResult> RunAsync(ResolvedRequest r,
                                             CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(r);

        var salesQuery = _db.Sales
            .AsNoTracking()
            .Where(s => s.OccurredAtUtc >= r.FromUtc
                        && s.OccurredAtUtc < r.ToExclusiveUtc)
            .Where(s => s.Status == SaleStatus.Finalised);

        var returnsQuery = _db.SaleReturns
            .AsNoTracking()
            .Where(x => x.OccurredAtUtc >= r.FromUtc
                        && x.OccurredAtUtc < r.ToExclusiveUtc)
            .Where(x => x.Status == ReturnStatus.Finalised);

        if (r.Guid("operator") is Guid op)
        {
            salesQuery = salesQuery.Where(s => s.OperatorId == op);
            returnsQuery = returnsQuery.Where(x => x.OperatorId == op);
        }

        // Materialised and then flattened, as the other sales reports
        // are: SQLite cannot carry a sale's columns down onto its lines,
        // and the workstation database is always SQLite (NET-002).
        var sales = await salesQuery
            .Include(s => s.Lines)
            .Include(s => s.Payments)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var returns = await returnsQuery
            .Include(x => x.Lines)
            .Include(x => x.Refunds)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (r.String("tender") is string tender
            && Enum.TryParse<TenderType>(tender, out var paidBy))
        {
            sales = sales.Where(s => s.Payments.Any(p => p.Tender == paidBy)).ToList();
            returns = returns.Where(x => x.Refunds.Any(p => p.Tender == paidBy)).ToList();
        }

        var products = await _db.Products
            .AsNoTracking()
            .Select(p => new
            {
                p.Id,
                p.Code,
                Cost = p.CostPrice.Amount,
                p.DepartmentId,
                p.CategoryId
            })
            .ToDictionaryAsync(p => p.Id, ct)
            .ConfigureAwait(false);

        var department = r.Guid("department");
        var category = r.Guid("category");

        bool InScope(Guid productId) =>
            (department is null && category is null)
            || (products.TryGetValue(productId, out var p)
                && (department is null || p.DepartmentId == department)
                && (category is null || p.CategoryId == category));

        // Who each was for: the patient, or the account, of the sale -
        // and for a refund, of the sale it refunded.
        var originals = returns.Count == 0
            ? new Dictionary<Guid, (Guid? PatientId, Guid? AccountId)>()
            : await OriginalsAsync(returns.Select(x => x.OriginalSaleId)
                                          .Distinct().ToList(), ct)
                .ConfigureAwait(false);

        // A refund of a collected script gives back a co-payment, which
        // no more carries a cost than the collection did.
        var refundedIds = returns.Select(x => x.OriginalSaleId)
                                 .Distinct().ToList();

        var refundedCollections = refundedIds.Count == 0
            ? new HashSet<Guid>()
            : (await _db.Sales
                .AsNoTracking()
                .Where(s => refundedIds.Contains(s.Id)
                            && s.DispensingId != null)
                .Select(s => s.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false))
              .ToHashSet();

        // A script collection names neither patient nor account on the
        // sale: both belong to the dispensing it is collecting for.
        var collected = await CollectedAsync(sales
                .Where(s => s.DispensingId is not null)
                .Select(s => s.DispensingId!.Value)
                .Distinct().ToList(), ct)
            .ConfigureAwait(false);

        var customers = await CustomersAsync(
            sales.Select(s => (s.PatientId, s.AccountId))
                 .Concat(originals.Values)
                 .Concat(collected.Values.Select(c => ((Guid?)c.PatientId, (Guid?)null))), ct)
            .ConfigureAwait(false);

        string CustomerOf(Guid? patientId, Guid? accountId) =>
            patientId is Guid pid && customers.Patients.TryGetValue(pid, out var patient)
                ? patient
                : accountId is Guid aid && customers.Accounts.TryGetValue(aid, out var account)
                    ? account
                    : "";

        decimal UnitCost(Guid productId) =>
            products.TryGetValue(productId, out var p) ? p.Cost : 0m;

        string CodeOf(Guid productId) =>
            products.TryGetValue(productId, out var p) ? p.Code : "";

        var transactions = new List<Transaction>();

        foreach (var sale in sales)
        {
            var lines = sale.Lines
                .Where(l => !l.IsVoided && InScope(l.ProductId))
                .OrderBy(l => l.LineNo)
                .Select(l => new Line(
                    CodeOf(l.ProductId),
                    Described(l.Description, l.BatchNumber),
                    l.Quantity,
                    l.UnitPrice.Amount,
                    l.DiscountAmount.Amount,
                    l.Vat.VatAmount.Amount,
                    l.LineTotal.Amount,
                    // A collection's line is the patient's share, not the
                    // item's price, so no cost can be set against it.
                    sale.IsCollection ? null : UnitCost(l.ProductId) * l.Quantity))
                .ToList();

            if (lines.Count == 0) continue;

            var script = sale.DispensingId is Guid dispensingId
                         && collected.TryGetValue(dispensingId, out var found)
                ? found
                : ((Guid PatientId, string ScriptNumber)?)null;

            transactions.Add(new Transaction(
                sale.OccurredAtUtc.ToLocalTime(), sale.DocumentNumber,
                script is { } s
                    ? $"Script {s.ScriptNumber}".Trim()
                    : sale.IsCollection ? "Script" : "Sale",
                r.OperatorName(sale.OperatorId),
                CustomerOf(sale.PatientId ?? script?.PatientId, sale.AccountId),
                PaidBy(sale.Payments), lines));
        }

        foreach (var refund in returns)
        {
            var lines = refund.Lines
                .Where(l => InScope(l.ProductId))
                .OrderBy(l => l.LineNo)
                .Select(l =>
                {
                    var total = l.LineTotal.Amount;
                    var vat = VatCalculator.FromInclusive(l.LineTotal,
                        l.VatRatePercent).VatAmount.Amount;

                    // Goods back on the shelf bring their cost back with
                    // them; goods written off, and a credit given without
                    // goods, do not.
                    var restocked = refund.Kind == ReturnKind.GoodsReturn
                                    && l.RestockToShelf;

                    return new Line(CodeOf(l.ProductId), l.Description,
                        -l.Quantity, l.UnitPrice.Amount, 0m, -vat, -total,
                        refundedCollections.Contains(refund.OriginalSaleId)
                            ? null
                            : restocked ? -UnitCost(l.ProductId) * l.Quantity : 0m);
                })
                .ToList();

            if (lines.Count == 0) continue;

            originals.TryGetValue(refund.OriginalSaleId, out var original);

            transactions.Add(new Transaction(
                refund.OccurredAtUtc.ToLocalTime(), refund.DocumentNumber,
                string.Format(CultureInfo.CurrentCulture, "{0} of {1}",
                    refund.Kind == ReturnKind.CreditNote ? "Credit" : "Refund",
                    refund.OriginalDocumentNumber),
                r.OperatorName(refund.OperatorId),
                CustomerOf(original.PatientId, original.AccountId),
                PaidBy(refund.Refunds, negative: true), lines));
        }

        var byMonth = string.Equals(r.String("groupBy"), "month",
                                    StringComparison.Ordinal);

        var rows = Lay(transactions, byMonth);

        var title = byMonth
            ? "Monthly Sales (Line Items)"
            : "Daily Sales (Line Items)";

        return ReportBuilder
            .For(title)
            // What was sold first, next to the receipt it was sold on; how
            // it was paid and who rang it up after the money.
            .Column("When", "Date and time", "dd/MM/yyyy HH:mm")
            .Column("Document", "Receipt")
            .Column("Type", "Type")
            .Column("Customer", "Customer")
            .Column("Code", "Code")
            .Column("Item", "Item")
            .Column("Quantity", "Qty", "0.###", rightAlign: true)
            .Column("UnitPrice", "Price", "N2", rightAlign: true)
            .Column("Discount", "Discount", "N2", rightAlign: true)
            .Column("Vat", "VAT", "N2", rightAlign: true)
            .Column("Total", "Total", "N2", rightAlign: true)
            .Column("Cost", "Cost", "N2", rightAlign: true)
            .Column("Margin", "Margin", "N2", rightAlign: true)
            .Column("PaidBy", "Paid by")
            .Column("Operator", "Operator")
            .Rows(rows)
            .Total("Sales", transactions.Sum(t => t.Lines.Sum(l => l.Total)))
            .Total("Vat", transactions.Sum(t => t.Lines.Sum(l => l.Vat)))
            .Total("Cost", transactions.Sum(t => t.Lines.Sum(l => l.Cost ?? 0m)))
            .Total("Count", transactions.Count)
            .Build(r.Provenance);
    }

    /// <summary>
    /// The transactions a day at a time, each line under its receipt, a
    /// total after every day, after every month where the report is a
    /// month's, and one for the whole of what was asked.
    /// </summary>
    private static List<Dictionary<string, object?>> Lay(
        IReadOnlyList<Transaction> transactions, bool byMonth)
    {
        var rows = new List<Dictionary<string, object?>>();

        var days = transactions
            .OrderBy(t => t.When)
            .ThenBy(t => t.Document, StringComparer.Ordinal)
            .GroupBy(t => DateOnly.FromDateTime(t.When))
            .ToList();

        for (var i = 0; i < days.Count; i++)
        {
            foreach (var transaction in days[i])
            {
                var first = true;

                foreach (var line in transaction.Lines)
                {
                    rows.Add(LineRow(transaction, line, first));
                    first = false;
                }
            }

            rows.Add(TotalRow("Day total", string.Format(
                CultureInfo.CurrentCulture, "{0:dddd dd/MM/yyyy}", days[i].Key),
                days[i].ToList()));

            var month = new DateOnly(days[i].Key.Year, days[i].Key.Month, 1);

            if (byMonth && (i == days.Count - 1
                            || days[i + 1].Key.Month != month.Month
                            || days[i + 1].Key.Year != month.Year))
            {
                rows.Add(TotalRow("Month total", string.Format(
                    CultureInfo.CurrentCulture, "{0:MMMM yyyy}", month),
                    transactions.Where(t => t.When.Year == month.Year
                                            && t.When.Month == month.Month)
                                .ToList()));
            }
        }

        rows.Add(TotalRow("TOTAL", "Everything in the report",
            transactions.ToList()));

        return rows;
    }

    /// <remarks>
    /// The receipt's own details on its first line only, so the receipt
    /// reads as one block; the receipt number on every line, so a line
    /// taken out of the report still says where it came from.
    /// </remarks>
    private static Dictionary<string, object?> LineRow(Transaction t,
        Line line, bool first) =>
        new(StringComparer.Ordinal)
        {
            ["When"] = t.When,
            ["Document"] = t.Document,
            ["Type"] = first ? t.Type : null,
            ["Operator"] = first ? t.Operator : null,
            ["Customer"] = first ? t.Customer : null,
            ["PaidBy"] = first ? t.PaidBy : null,
            ["Code"] = line.Code,
            ["Item"] = line.Item,
            ["Quantity"] = line.Quantity,
            ["UnitPrice"] = line.UnitPrice,
            ["Discount"] = line.Discount == 0m ? null : line.Discount,
            ["Vat"] = line.Vat,
            ["Total"] = line.Total,
            ["Cost"] = line.Cost,
            ["Margin"] = line.Cost is decimal cost ? line.Total - cost : null
        };

    private static Dictionary<string, object?> TotalRow(string kind,
        string period, IReadOnlyList<Transaction> transactions)
    {
        var lines = transactions.SelectMany(t => t.Lines).ToList();
        var costed = lines.Where(l => l.Cost is not null).ToList();

        // Every column present, blank where a total has nothing to say -
        // units of different products do not add up to anything.
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["When"] = null,
            ["Document"] = null,
            ["Operator"] = null,
            ["Code"] = null,
            ["Quantity"] = null,
            ["UnitPrice"] = null,
            ["Type"] = kind,
            ["Customer"] = period,
            ["PaidBy"] = PaidByTotals(transactions),
            ["Item"] = string.Format(CultureInfo.CurrentCulture,
                "{0:N0} transaction(s), {1:N0} line(s){2}",
                transactions.Count, lines.Count,
                costed.Count < lines.Count
                    ? "; cost and margin exclude script co-payments"
                    : ""),
            ["Discount"] = lines.Sum(l => l.Discount),
            ["Vat"] = lines.Sum(l => l.Vat),
            ["Total"] = lines.Sum(l => l.Total),
            ["Cost"] = costed.Sum(l => l.Cost!.Value),
            ["Margin"] = costed.Sum(l => l.Total - l.Cost!.Value)
        };
    }

    /// How the money came in over a day or a month, tender by tender.
    private static string PaidByTotals(IEnumerable<Transaction> transactions) =>
        string.Join(", ", transactions
            .SelectMany(t => t.Tenders)
            .GroupBy(x => x.Tender)
            .OrderBy(g => g.Key)
            .Select(g => string.Format(CultureInfo.CurrentCulture, "{0} {1:N2}",
                TenderName(g.Key), g.Sum(x => x.Amount))));

    private static (string Text, IReadOnlyList<(TenderType Tender, decimal Amount)> Tenders)
        PaidBy(IEnumerable<SalePayment> payments, bool negative = false)
    {
        var tenders = payments
            .Select(p => (p.Tender, Amount: negative ? -p.Amount.Amount : p.Amount.Amount))
            .ToList();

        var text = string.Join(", ", tenders.Select(p => string.Format(
            CultureInfo.CurrentCulture, "{0} {1:N2}",
            TenderName(p.Tender), p.Amount)));

        return (text, tenders);
    }

    private static string TenderName(TenderType tender) => tender switch
    {
        TenderType.MedicalAid => "Medical aid",
        TenderType.Eft => "EFT",
        _ => tender.ToString()
    };

    /// The batch a line came from, where it came from one.
    private static string Described(string description, string? batch) =>
        string.IsNullOrWhiteSpace(batch)
            ? description
            : $"{description} (batch {batch})";

    /// Each collected dispensing's patient and script number.
    private async Task<Dictionary<Guid, (Guid PatientId, string ScriptNumber)>>
        CollectedAsync(IReadOnlyList<Guid> dispensingIds, CancellationToken ct)
    {
        if (dispensingIds.Count == 0)
            return new Dictionary<Guid, (Guid PatientId, string ScriptNumber)>();

        var dispensings = await _db.Dispensings
            .AsNoTracking()
            .Where(d => dispensingIds.Contains(d.Id))
            .Select(d => new { d.Id, d.PatientId, d.ScriptId })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var scriptIds = dispensings.Select(d => d.ScriptId).Distinct().ToList();

        var numbers = await _db.Scripts
            .AsNoTracking()
            .Where(s => scriptIds.Contains(s.Id))
            .Select(s => new { s.Id, s.ScriptNumber })
            .ToDictionaryAsync(s => s.Id, s => s.ScriptNumber, ct)
            .ConfigureAwait(false);

        return dispensings.ToDictionary(d => d.Id, d => (d.PatientId,
            numbers.TryGetValue(d.ScriptId, out var number) ? number : ""));
    }

    private async Task<Dictionary<Guid, (Guid? PatientId, Guid? AccountId)>>
        OriginalsAsync(IReadOnlyList<Guid> saleIds, CancellationToken ct) =>
        (await _db.Sales
            .AsNoTracking()
            .Where(s => saleIds.Contains(s.Id))
            .Select(s => new { s.Id, s.PatientId, s.AccountId })
            .ToListAsync(ct)
            .ConfigureAwait(false))
        .ToDictionary(s => s.Id, s => (s.PatientId, s.AccountId));

    /// <summary>
    /// Patients surname first, as the pharmacy looks them up; accounts by
    /// their name and number.
    /// </summary>
    private async Task<(Dictionary<Guid, string> Patients, Dictionary<Guid, string> Accounts)>
        CustomersAsync(IEnumerable<(Guid? PatientId, Guid? AccountId)> wanted,
                       CancellationToken ct)
    {
        var pairs = wanted.ToList();

        var patientIds = pairs.Where(p => p.PatientId is not null)
            .Select(p => p.PatientId!.Value).Distinct().ToList();
        var accountIds = pairs.Where(p => p.AccountId is not null)
            .Select(p => p.AccountId!.Value).Distinct().ToList();

        var patients = patientIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await _db.Patients
                .AsNoTracking()
                .Where(p => patientIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Surname, p.FirstNames })
                .ToListAsync(ct)
                .ConfigureAwait(false))
              .ToDictionary(p => p.Id,
                  p => string.IsNullOrWhiteSpace(p.FirstNames)
                      ? p.Surname
                      : $"{p.Surname}, {p.FirstNames}");

        var accounts = accountIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await _db.Accounts
                .AsNoTracking()
                .Where(a => accountIds.Contains(a.Id))
                .Select(a => new { a.Id, a.Name, a.AccountNumber })
                .ToListAsync(ct)
                .ConfigureAwait(false))
              .ToDictionary(a => a.Id, a => $"{a.Name} ({a.AccountNumber})");

        return (patients, accounts);
    }

    private sealed record Line(string Code, string Item, decimal Quantity,
        decimal UnitPrice, decimal Discount, decimal Vat, decimal Total,
        decimal? Cost);

    private sealed record Transaction(DateTime When, string Document,
        string Type, string Operator, string Customer,
        (string Text, IReadOnlyList<(TenderType Tender, decimal Amount)> Tenders) Paid,
        IReadOnlyList<Line> Lines)
    {
        public string PaidBy => Paid.Text;

        public IReadOnlyList<(TenderType Tender, decimal Amount)> Tenders =>
            Paid.Tenders;
    }
}
