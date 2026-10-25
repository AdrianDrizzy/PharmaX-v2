using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Reporting;
using PharmaX.Domain.Dispensing;
using PharmaX.Domain.Sales;
using PharmaX.Persistence;
using PharmaX.Reporting.Engine;

namespace PharmaX.Reporting.Definitions;

/// <summary>
/// What each patient bought, by name: the medicines dispensed to them and
/// what they bought over the counter, as products rather than script
/// numbers - week by week, month by month, a total for each patient, or
/// each product with the patients who had it.
/// </summary>
/// <remarks>
/// <para>
/// A dispensing is the patient's by its own record. A till sale is the
/// patient's when it names them, or when it is the collection of one of
/// their scripts: what else was rung up with the collection was bought by
/// the person collecting. The collection's own line is only the patient's
/// share of the script, already listed as the medicines dispensed, so it
/// is left out rather than counted twice.
/// </para>
/// <para>
/// A dispensed medicine is valued at what it was charged, the scheme's
/// share included; a counter item at what was paid. Refunds of counter
/// items come off as negative lines. Fixed slicers: "groupBy" (week,
/// month, patient, product) and "source" (Script or Counter).
/// </para>
/// </remarks>
public sealed class PatientPurchasesReport : IReportDefinition
{
    public string Key => "PatientPurchases";

    public const string Script = "Script";
    public const string Counter = "Counter";
    public const string Refund = "Refund";

    private readonly PharmaXDbContext _db;

    public PatientPurchasesReport(PharmaXDbContext db) => _db = db;

    /// <summary>One product bought by one patient on one occasion.</summary>
    /// <param name="Visit">
    /// The dispensing or sale it was bought on, so visits can be counted.
    /// </param>
    public sealed record Purchase(DateTime When, Guid PatientId,
        string Patient, string FileNumber, string Product, string Source,
        decimal Quantity, decimal Value, Guid Visit);

    public async Task<ReportResult> RunAsync(ResolvedRequest r,
                                             CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(r);

        var source = r.String("source");
        var raw = new List<(DateTime When, Guid PatientId, string Product,
                            string Source, decimal Quantity, decimal Value, Guid Visit)>();

        var products = await _db.Products.AsNoTracking()
            .Select(p => new { p.Id, p.Description })
            .ToDictionaryAsync(p => p.Id, p => p.Description, ct)
            .ConfigureAwait(false);

        string Named(Guid productId, string description) =>
            !string.IsNullOrWhiteSpace(description)
                ? description
                : products.GetValueOrDefault(productId, "");

        if (source is null or Script)
        {
            var issued = await _db.Dispensings.AsNoTracking()
                .Where(d => d.Status == DispensingStatus.Issued
                            && d.IssuedAtUtc >= r.FromUtc
                            && d.IssuedAtUtc < r.ToExclusiveUtc)
                .Include(d => d.Lines)
                .ToListAsync(ct).ConfigureAwait(false);

            raw.AddRange(issued.SelectMany(d => d.Lines.Select(l => (
                d.IssuedAtUtc!.Value.ToLocalTime(), d.PatientId,
                Named(l.ProductId, l.Description), Script,
                l.Quantity, l.TariffAmount.Amount, d.Id))));
        }

        if (source is null or Counter)
        {
            var sales = await _db.Sales.AsNoTracking()
                .Where(s => s.Status == SaleStatus.Finalised
                            && s.OccurredAtUtc >= r.FromUtc
                            && s.OccurredAtUtc < r.ToExclusiveUtc
                            && (s.PatientId != null || s.DispensingId != null))
                .Include(s => s.Lines)
                .ToListAsync(ct).ConfigureAwait(false);

            var returns = await _db.SaleReturns.AsNoTracking()
                .Where(x => x.Status == ReturnStatus.Finalised
                            && x.OccurredAtUtc >= r.FromUtc
                            && x.OccurredAtUtc < r.ToExclusiveUtc)
                .Include(x => x.Lines)
                .ToListAsync(ct).ConfigureAwait(false);

            // The sales refunded, for whose they were and which of their
            // lines were a script's share rather than an item.
            var refundedIds = returns.Select(x => x.OriginalSaleId).Distinct().ToList();
            var refunded = refundedIds.Count == 0
                ? new Dictionary<Guid, Sale>()
                : await _db.Sales.AsNoTracking()
                    .Where(s => refundedIds.Contains(s.Id))
                    .Include(s => s.Lines)
                    .ToDictionaryAsync(s => s.Id, ct).ConfigureAwait(false);

            var dispensingIds = sales.Concat(refunded.Values)
                .Where(s => s.PatientId is null && s.DispensingId is not null)
                .Select(s => s.DispensingId!.Value)
                .Distinct().ToList();

            var collectedFor = dispensingIds.Count == 0
                ? new Dictionary<Guid, Guid>()
                : await _db.Dispensings.AsNoTracking()
                    .Where(d => dispensingIds.Contains(d.Id))
                    .Select(d => new { d.Id, d.PatientId })
                    .ToDictionaryAsync(d => d.Id, d => d.PatientId, ct)
                    .ConfigureAwait(false);

            Guid? PatientOf(Sale s) =>
                s.PatientId
                ?? (s.DispensingId is Guid d && collectedFor.TryGetValue(d, out var p)
                    ? p : null);

            foreach (var sale in sales)
            {
                if (PatientOf(sale) is not Guid patient) continue;

                raw.AddRange(sale.Lines
                    .Where(l => !l.IsVoided && !l.IsScript)
                    .Select(l => (sale.OccurredAtUtc.ToLocalTime(), patient,
                        Named(l.ProductId, l.Description), Counter,
                        l.Quantity, l.LineTotal.Amount, sale.Id)));
            }

            foreach (var refund in returns)
            {
                if (!refunded.TryGetValue(refund.OriginalSaleId, out var original)
                    || PatientOf(original) is not Guid patient)
                    continue;

                var scriptLines = original.Lines.Where(l => l.IsScript)
                    .Select(l => l.Id).ToHashSet();

                raw.AddRange(refund.Lines
                    .Where(l => !scriptLines.Contains(l.OriginalSaleLineId))
                    .Select(l => (refund.OccurredAtUtc.ToLocalTime(), patient,
                        Named(l.ProductId, l.Description), Refund,
                        -l.Quantity, -l.LineTotal.Amount, refund.Id)));
            }
        }

        var patientIds = raw.Select(x => x.PatientId).Distinct().ToList();
        var patients = patientIds.Count == 0
            ? new Dictionary<Guid, (string Name, string File)>()
            : (await _db.Patients.AsNoTracking()
                .Where(p => patientIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Surname, p.FirstNames, p.FileNumber })
                .ToListAsync(ct).ConfigureAwait(false))
              .ToDictionary(p => p.Id, p => (
                  string.IsNullOrWhiteSpace(p.FirstNames)
                      ? p.Surname
                      : $"{p.Surname}, {p.FirstNames}",
                  p.FileNumber));

        var purchases = raw.Select(x =>
        {
            var (name, file) = patients.GetValueOrDefault(x.PatientId, ("(patient not on file)", ""));
            return new Purchase(x.When, x.PatientId, name, file, x.Product,
                x.Source, x.Quantity, x.Value, x.Visit);
        }).ToList();

        return Build(purchases, r.String("groupBy") ?? "patient", r.Provenance);
    }

    /// <summary>The purchases laid out as the preset asks.</summary>
    public static ReportResult Build(IReadOnlyList<Purchase> purchases,
        string groupBy, ReportProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(purchases);

        var builder = groupBy switch
        {
            "week" => ByPeriod(purchases, "Weekly Patient Purchases", WeekOf),
            "month" => ByPeriod(purchases, "Monthly Patient Purchases", MonthOf),
            "product" => ByProduct(purchases),
            _ => ByPatient(purchases)
        };

        return builder
            .Total("Patients", purchases.Select(p => p.PatientId).Distinct().Count())
            .Total("Quantity", purchases.Sum(p => p.Quantity))
            .Total("Value", purchases.Sum(p => p.Value))
            .Build(provenance);
    }

    /// The Monday the week starts on, and how the week is headed.
    private static (DateOnly Start, string Label) WeekOf(DateTime when)
    {
        var day = DateOnly.FromDateTime(when);
        var monday = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
        return (monday, string.Format(CultureInfo.CurrentCulture,
            "Week of {0:ddd dd/MM/yyyy}", monday));
    }

    private static (DateOnly Start, string Label) MonthOf(DateTime when) =>
        (new DateOnly(when.Year, when.Month, 1),
         string.Format(CultureInfo.CurrentCulture, "{0:MMMM yyyy}", when));

    /// <summary>
    /// Each week or month, each patient in it by name, and what they
    /// bought in it a product to a line - the same product bought twice
    /// in the period is one line with how many times.
    /// </summary>
    private static ReportBuilder ByPeriod(IReadOnlyList<Purchase> purchases,
        string title, Func<DateTime, (DateOnly Start, string Label)> periodOf)
    {
        var rows = new List<Dictionary<string, object?>>();

        foreach (var period in purchases
                     .GroupBy(p => periodOf(p.When))
                     .OrderBy(g => g.Key.Start))
        {
            foreach (var patient in period
                         .GroupBy(p => p.PatientId)
                         .OrderBy(g => g.First().Patient, StringComparer.CurrentCultureIgnoreCase))
            {
                var first = true;
                var lines = Lines(patient).ToList();

                foreach (var line in lines)
                {
                    rows.Add(Row(period.Key.Label, first ? line.Patient : null,
                        first ? line.FileNumber : null, line.Product, line.Source,
                        line.Times, line.Quantity, line.Value));
                    first = false;
                }

                if (lines.Count > 1)
                    rows.Add(Row(null, null, null, "Patient total", null,
                        patient.Select(p => p.Visit).Distinct().Count(), null,
                        patient.Sum(p => p.Value)));
            }

            rows.Add(Row(period.Key.Label, string.Format(CultureInfo.CurrentCulture,
                    "{0:N0} patient(s)", period.Select(p => p.PatientId).Distinct().Count()),
                null, "Period total", null,
                period.Select(p => p.Visit).Distinct().Count(), null,
                period.Sum(p => p.Value)));
        }

        return ReportBuilder.For(title)
            .Column("Period", "Period")
            .Column("Patient", "Patient")
            .Column("FileNumber", "File no.")
            .Column("Product", "Product")
            .Column("Source", "How")
            .Column("Times", "Times", "N0", rightAlign: true)
            .Column("Quantity", "Qty", "0.###", rightAlign: true)
            .Column("Value", "Value", "N2", rightAlign: true)
            .Rows(rows);
    }

    /// <summary>
    /// One line a patient: how often they came, what came from scripts
    /// and what from the counter, and the products they bought most.
    /// </summary>
    private static ReportBuilder ByPatient(IReadOnlyList<Purchase> purchases)
    {
        var rows = purchases
            .GroupBy(p => p.PatientId)
            .Select(g => new
            {
                g.First().Patient,
                g.First().FileNumber,
                Visits = g.Select(p => p.Visit).Distinct().Count(),
                Scripts = g.Where(p => p.Source == Script)
                           .Select(p => p.Visit).Distinct().Count(),
                ScriptValue = g.Where(p => p.Source == Script).Sum(p => p.Value),
                CounterValue = g.Where(p => p.Source != Script).Sum(p => p.Value),
                Value = g.Sum(p => p.Value),
                Last = g.Max(p => p.When),
                Bought = Products(g)
            })
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Patient, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Patient"] = x.Patient,
                ["FileNumber"] = x.FileNumber,
                ["Visits"] = x.Visits,
                ["Scripts"] = x.Scripts,
                ["ScriptValue"] = x.ScriptValue,
                ["CounterValue"] = x.CounterValue,
                ["Value"] = x.Value,
                ["Last"] = x.Last,
                ["Bought"] = x.Bought
            });

        return ReportBuilder.For("Patient Purchases Summary")
            .Column("Patient", "Patient")
            .Column("FileNumber", "File no.")
            .Column("Visits", "Visits", "N0", rightAlign: true)
            .Column("Scripts", "Scripts", "N0", rightAlign: true)
            .Column("ScriptValue", "Dispensed", "N2", rightAlign: true)
            .Column("CounterValue", "Counter", "N2", rightAlign: true)
            .Column("Value", "Total", "N2", rightAlign: true)
            .Column("Last", "Last visit", "dd/MM/yyyy")
            .Column("Bought", "Bought most")
            .Rows(rows);
    }

    /// <summary>
    /// Each product, the patients who had it, and how much each had: who
    /// takes a medicine, and who to call when it is recalled.
    /// </summary>
    private static ReportBuilder ByProduct(IReadOnlyList<Purchase> purchases)
    {
        var rows = new List<Dictionary<string, object?>>();

        foreach (var product in purchases
                     .GroupBy(p => p.Product, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            var first = true;
            var patients = product.GroupBy(p => p.PatientId)
                .OrderByDescending(g => g.Sum(p => p.Quantity))
                .ThenBy(g => g.First().Patient, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            foreach (var patient in patients)
            {
                rows.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["Product"] = first ? product.Key : null,
                    ["Patient"] = patient.First().Patient,
                    ["FileNumber"] = patient.First().FileNumber,
                    ["Times"] = patient.Select(p => p.Visit).Distinct().Count(),
                    ["Quantity"] = patient.Sum(p => p.Quantity),
                    ["Value"] = patient.Sum(p => p.Value),
                    ["Last"] = patient.Max(p => p.When)
                });
                first = false;
            }

            rows.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Product"] = null,
                ["Patient"] = string.Format(CultureInfo.CurrentCulture,
                    "Product total: {0:N0} patient(s)", patients.Count),
                ["FileNumber"] = null,
                ["Times"] = product.Select(p => p.Visit).Distinct().Count(),
                ["Quantity"] = product.Sum(p => p.Quantity),
                ["Value"] = product.Sum(p => p.Value),
                ["Last"] = null
            });
        }

        return ReportBuilder.For("Products by Patient")
            .Column("Product", "Product")
            .Column("Patient", "Patient")
            .Column("FileNumber", "File no.")
            .Column("Times", "Times", "N0", rightAlign: true)
            .Column("Quantity", "Qty", "0.###", rightAlign: true)
            .Column("Value", "Value", "N2", rightAlign: true)
            .Column("Last", "Last bought", "dd/MM/yyyy")
            .Rows(rows);
    }

    private sealed record Summed(string Patient, string FileNumber,
        string Product, string Source, int Times, decimal Quantity, decimal Value);

    /// A patient's purchases in a period, one line a product and how.
    private static IEnumerable<Summed> Lines(IEnumerable<Purchase> patient) =>
        patient
            .GroupBy(p => (Product: p.Product.ToUpperInvariant(), p.Source))
            .Select(g => new Summed(g.First().Patient, g.First().FileNumber,
                g.First().Product, g.Key.Source,
                g.Select(p => p.Visit).Distinct().Count(),
                g.Sum(p => p.Quantity), g.Sum(p => p.Value)))
            .OrderBy(s => s.Source == Script ? 0 : 1)
            .ThenBy(s => s.Product, StringComparer.CurrentCultureIgnoreCase);

    /// The products a patient bought most often, most often first.
    private static string Products(IEnumerable<Purchase> purchases)
    {
        const int shown = 5;

        var names = purchases
            .Where(p => p.Source != Refund)
            .GroupBy(p => p.Product, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Select(p => p.Visit).Distinct().Count())
            .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => g.Key)
            .ToList();

        return names.Count <= shown
            ? string.Join(", ", names)
            : string.Format(CultureInfo.CurrentCulture, "{0} and {1:N0} more",
                string.Join(", ", names.Take(shown)), names.Count - shown);
    }

    private static Dictionary<string, object?> Row(string? period,
        string? patient, string? file, string product, string? source,
        int times, decimal? quantity, decimal value) =>
        new(StringComparer.Ordinal)
        {
            ["Period"] = period,
            ["Patient"] = patient,
            ["FileNumber"] = file,
            ["Product"] = product,
            ["Source"] = source,
            ["Times"] = times,
            ["Quantity"] = quantity,
            ["Value"] = value
        };
}
