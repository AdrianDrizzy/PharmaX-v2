using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Reporting;
using PharmaX.Domain.Dispensing;
using PharmaX.Domain.Patients;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Sales;
using PharmaX.Persistence;
using PharmaX.Persistence.Interceptors;
using PharmaX.Reporting.Definitions;
using PharmaX.Reporting.Engine;
using Purchase = PharmaX.Reporting.Definitions.PatientPurchasesReport.Purchase;

namespace PharmaX.Tests.Unit.Reporting;

/// <summary>
/// What each patient bought, by product name: dispensed and over the
/// counter, week by week, month by month, by patient and by product.
/// </summary>
public sealed class PatientPurchasesReportTests : IDisposable
{
    private static readonly ReportProvenance Provenance =
        new("test", new DateTime(2026, 10, 25), "", "");

    private static readonly Guid Moyo = Guid.NewGuid();
    private static readonly Guid Ncube = Guid.NewGuid();

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public PatientPurchasesReportTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    private static Purchase Bought(Guid patient, DateTime when, string product,
        string source, decimal quantity, decimal value, Guid? visit = null) =>
        new(when, patient, patient == Moyo ? "Moyo, Thandi" : "Ncube, Sipho",
            patient == Moyo ? "F001" : "F002", product, source, quantity, value,
            visit ?? Guid.NewGuid());

    // Monday 6 and Wednesday 8 October are one week; Monday 13 the next.
    private static readonly List<Purchase> Purchases = new()
    {
        Bought(Moyo, new DateTime(2026, 10, 6, 9, 0, 0), "METFORMIN 500MG",
            PatientPurchasesReport.Script, 60m, 3m),
        Bought(Moyo, new DateTime(2026, 10, 8, 9, 0, 0), "Metformin 500mg",
            PatientPurchasesReport.Script, 60m, 3m),
        Bought(Moyo, new DateTime(2026, 10, 8, 9, 5, 0), "PARACETAMOL 20S",
            PatientPurchasesReport.Counter, 1m, 0.5m),
        Bought(Ncube, new DateTime(2026, 10, 13, 12, 0, 0), "METFORMIN 500MG",
            PatientPurchasesReport.Script, 30m, 1.5m)
    };

    [Fact]
    public void Weekly_lists_each_patient_by_name_with_products_not_scripts()
    {
        var result = PatientPurchasesReport.Build(Purchases, "week", Provenance);

        result.Title.Should().Be("Weekly Patient Purchases");
        var rows = result.Rows;

        var firstWeek = rows.Where(r => Equals(r["Period"], rows[0]["Period"])).ToList();
        firstWeek[0]["Patient"].Should().Be("Moyo, Thandi");
        firstWeek[0]["Product"].Should().Be("METFORMIN 500MG");
        firstWeek[0]["Times"].Should().Be(2, "the same medicine twice in a week is one line");
        firstWeek[0]["Quantity"].Should().Be(120m);
        firstWeek[1]["Product"].Should().Be("PARACETAMOL 20S");
        firstWeek[1]["Source"].Should().Be(PatientPurchasesReport.Counter);
        firstWeek[1]["Patient"].Should().BeNull("the name heads the patient's block once");

        rows.Should().Contain(r => Equals(r["Product"], "Patient total")
                                   && Equals(r["Value"], 6.5m));
        rows.Count(r => Equals(r["Product"], "Period total")).Should().Be(2);
        rows[^1]["Value"].Should().Be(1.5m);
        result.Totals["Value"].Should().Be(8m);
        result.Totals["Patients"].Should().Be(2);
    }

    [Fact]
    public void Monthly_puts_the_whole_month_together()
    {
        var rows = PatientPurchasesReport.Build(Purchases, "month", Provenance).Rows;

        rows.Count(r => Equals(r["Product"], "Period total")).Should().Be(1);
        rows.Single(r => Equals(r["Product"], "Period total"))["Value"].Should().Be(8m);
    }

    [Fact]
    public void The_summary_is_one_line_a_patient_highest_spend_first()
    {
        var rows = PatientPurchasesReport.Build(Purchases, "patient", Provenance).Rows;

        rows.Should().HaveCount(2);
        rows[0]["Patient"].Should().Be("Moyo, Thandi");
        rows[0]["Visits"].Should().Be(3);
        rows[0]["Scripts"].Should().Be(2);
        rows[0]["ScriptValue"].Should().Be(6m);
        rows[0]["CounterValue"].Should().Be(0.5m);
        rows[0]["Value"].Should().Be(6.5m);
        rows[0]["Last"].Should().Be(new DateTime(2026, 10, 8, 9, 5, 0));
        rows[0]["Bought"].Should().Be("METFORMIN 500MG, PARACETAMOL 20S");
    }

    [Fact]
    public void By_product_lists_who_had_each_medicine()
    {
        var rows = PatientPurchasesReport.Build(Purchases, "product", Provenance).Rows;

        rows[0]["Product"].Should().Be("METFORMIN 500MG");
        rows[0]["Patient"].Should().Be("Moyo, Thandi");
        rows[0]["Quantity"].Should().Be(120m);
        rows[1]["Patient"].Should().Be("Ncube, Sipho");
        rows[2]["Patient"].Should().Be("Product total: 2 patient(s)");
        rows[2]["Quantity"].Should().Be(150m);
        rows[3]["Product"].Should().Be("PARACETAMOL 20S");
    }

    [Fact]
    public void Nothing_bought_is_an_empty_report() =>
        PatientPurchasesReport.Build(new List<Purchase>(), "week", Provenance)
            .Rows.Should().BeEmpty();

    [Fact]
    public void Every_preset_names_a_layout_the_report_knows()
    {
        var catalogue = new PresetCatalogue();
        var presets = catalogue.All().Where(p => p.Key.StartsWith("pat.", StringComparison.Ordinal))
            .ToList();

        presets.Should().HaveCount(4);
        presets.Select(p => catalogue.Resolve(p.Key).FixedSlicers["groupBy"])
            .Should().BeEquivalentTo("week", "month", "patient", "product");
        presets.Should().OnlyContain(p => p.Group == "Patients");
        catalogue.SlicersFor("pat.purchases.weekly").Select(s => s.Key)
            .Should().Equal("source");
    }

    /// <remarks>
    /// The medicines of a script, by name, are the patient's; what was rung
    /// up with the collection is theirs too; the collection's own line, the
    /// patient's share of the script, is not counted a second time. A sale
    /// to nobody in particular is nobody's.
    /// </remarks>
    [Fact]
    public async Task Dispensed_medicines_and_counter_items_are_linked_to_the_patient()
    {
        var noon = new DateTime(2026, 10, 14, 10, 0, 0, DateTimeKind.Utc);
        await using var db = Database();

        var patient = Patient.Create("F001", "Moyo", "Thandi");
        db.Patients.Add(patient);

        var dispensing = Dispensing.Begin(Guid.NewGuid(), patient.Id, Guid.NewGuid(), 0);
        dispensing.AddLine(DispensingLine.Create(Guid.NewGuid(), "METFORMIN 500MG",
            60m, "One twice a day", Money.Of(3m)));
        dispensing.Issue(Guid.NewGuid(), "SCRIPT-77", noon);
        db.Dispensings.Add(dispensing);

        var collection = Sale.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), noon.AddMinutes(5));
        collection.CollectFor(dispensing.Id);
        collection.AddLine(SaleLine.Create(Guid.NewGuid(), "Script SCRIPT-77", 1m,
            Money.Of(3m), 0m, "B", PriceSource.MedicalAidTariff, isScript: true));
        collection.AddLine(SaleLine.Create(Guid.NewGuid(), "PARACETAMOL 20S", 2m,
            Money.Of(0.5m), 0m, "B", PriceSource.StandardRetail));
        collection.AddPayment(SalePayment.Create(TenderType.Cash, Money.Of(4m)));
        collection.Finalise("R-1", noon.AddMinutes(5));

        var stranger = Sale.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), noon.AddMinutes(6));
        stranger.AddLine(SaleLine.Create(Guid.NewGuid(), "CHEWING GUM", 1m,
            Money.Of(1m), 0m, "B", PriceSource.StandardRetail));
        stranger.AddPayment(SalePayment.Create(TenderType.Cash, Money.Of(1m)));
        stranger.Finalise("R-2", noon.AddMinutes(6));

        db.Sales.AddRange(collection, stranger);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var day = DateOnly.FromDateTime(noon.ToLocalTime());
        var result = await new PatientPurchasesReport(db).RunAsync(Request(day, "week"));

        result.Rows.Where(r => r["Source"] is not null)
            .Select(r => (r["Patient"], r["Product"], r["Source"], r["Value"]))
            .Should().Equal(
                ("Moyo, Thandi", "METFORMIN 500MG", PatientPurchasesReport.Script, 3m),
                (null, "PARACETAMOL 20S", PatientPurchasesReport.Counter, 1m));

        result.Rows.Should().NotContain(r => Equals(r["Product"], "Script SCRIPT-77"));
        result.Rows.Should().NotContain(r => Equals(r["Product"], "CHEWING GUM"));

        var counterOnly = await new PatientPurchasesReport(db)
            .RunAsync(Request(day, "patient", source: "Counter"));
        counterOnly.Rows.Single()["Value"].Should().Be(1m);
    }

    private static ResolvedRequest Request(DateOnly day, string groupBy,
                                           string? source = null) =>
        new(new ReportPreset("pat.test", "Test", "PatientPurchases",
                new Dictionary<string, string?>(), Array.Empty<string>(), ""),
            day, day, "1",
            new Dictionary<string, string?> { ["groupBy"] = groupBy, ["source"] = source },
            Provenance);

    private PharmaXDbContext Database()
    {
        var db = new PharmaXDbContext(new DbContextOptionsBuilder<PharmaXDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new RowVersionInterceptor())
            .Options);

        db.Database.EnsureCreated();
        return db;
    }
}
