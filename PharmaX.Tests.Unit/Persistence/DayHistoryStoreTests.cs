using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.History;
using PharmaX.Domain.Dispensing;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Patients;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Sales;
using PharmaX.Domain.Security;
using PharmaX.Domain.Suppliers;
using PharmaX.Persistence;
using PharmaX.Persistence.History;
using PharmaX.Persistence.Interceptors;

namespace PharmaX.Tests.Unit.Persistence;

/// <summary>
/// Day history lists the document, then the products and the other facts
/// on it, with the time to the second.
/// </summary>
public sealed class DayHistoryStoreTests : IDisposable
{
    private static readonly DateTime At =
        new(2026, 11, 21, 10, 17, 42, DateTimeKind.Utc);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public DayHistoryStoreTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task A_sale_and_a_script_list_their_products_and_the_time()
    {
        await using var db = Database();
        var day = DateOnly.FromDateTime(At.ToLocalTime());
        var when = At.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");

        var user = User.Create("bk", "Bukhosi Moyo", "hash");
        var patient = Patient.Create("F9", "Moyo", "Thandi", new DateOnly(1990, 4, 2), "63-111");
        patient.UpdateContact("0772000111", null);
        patient.SetAddress("28 Woodstock Road");
        patient.CreatedAtUtc = At;

        var productId = Guid.NewGuid();
        var script = Script.Capture("RX1042", patient.Id, "Dr Ncube", day);
        var item = script.AddItem(ScriptItem.Create(productId, "AMOXICILLIN 500MG",
            21m, "One three times a day"));

        var dispensing = Dispensing.Begin(script.Id, patient.Id, user.Id, 0);
        dispensing.AddLine(DispensingLine.Create(productId, "AMOXICILLIN 500MG", 21m,
            "One three times a day", Money.Of(8.50m), item.Id,
            batchNumber: "AMX1", batchExpiry: new DateOnly(2027, 8, 1)));
        dispensing.Issue(user.Id, "DSP-9", At);

        var sale = Sale.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), user.Id, At);
        sale.CollectFor(dispensing.Id);
        sale.AssignPatient(patient.Id, null);
        dispensing.RecordCollection(sale.Id, At.AddMinutes(4));
        sale.AddLine(SaleLine.Create(productId, "AMOXICILLIN 500MG x 21", 1m,
            Money.Of(8.50m), 0m, "A", PriceSource.MedicalAidTariff, isScript: true));
        sale.AddLine(SaleLine.Create(Guid.NewGuid(), "PANADO 500MG 20S", 2m,
            Money.Of(1.50m), 0m, "A", PriceSource.StandardRetail));
        sale.AddPayment(SalePayment.Create(TenderType.Cash, Money.Of(11.50m)));
        sale.Finalise("R-9", At);

        var supplier = Supplier.Create("ALP", "Alpha Wholesalers");
        var receipt = GoodsReceipt.Create("GR1", supplier.Id, day, user.Id,
            supplierInvoiceNumber: "INV-9");
        receipt.AddLine(productId, "PANADO 500MG", 10m, Money.Of(1.25m),
            isBatchTracked: true, batchNumber: "P1", expiryDate: new DateOnly(2027, 6, 1));
        receipt.CreatedAtUtc = At;

        var changed = Product.Create("PAN500", "PANADO 500MG", Money.Of(2.50m));
        changed.CreatedAtUtc = At.AddDays(-3);
        changed.UpdatedAtUtc = At;
        var audit = AuditEntry.Create(At, user.Id, "till-3", "inv.price.change",
            "Product", changed.Id, reason: "2.00 to 2.50");

        db.Users.Add(user);
        db.Patients.Add(patient);
        db.Scripts.Add(script);
        db.Dispensings.Add(dispensing);
        db.Sales.Add(sale);
        db.Suppliers.Add(supplier);
        db.GoodsReceipts.Add(receipt);
        db.Products.Add(changed);
        db.AuditEntries.Add(audit);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var history = new DayHistoryStore(db);

        var sales = await history.ForAsync(HistorySubject.Sales, day);
        sales.Should().Contain(l => !l.IsItem && l.Reference == "R-9" && l.When == when);
        sales.Should().Contain(l => l.IsItem && l.Detail.Contains("AMOXICILLIN 500MG")
            && l.Detail.Contains("One three times a day")
            && l.Detail.Contains("AMX1"));
        sales.Should().Contain(l => l.IsItem && l.Detail.Contains("2 x PANADO 500MG 20S")
            && l.Amount == 3.00m.ToString("N2"));
        sales.Should().Contain(l => !l.IsItem && l.Detail.Contains("script RX1042")
            && l.Detail.Contains("Bukhosi Moyo") && l.Detail.Contains("Thandi Moyo"));

        var dispensed = await history.ForAsync(HistorySubject.Dispensed, day);
        dispensed.Should().Contain(l => !l.IsItem && l.Reference == "DSP-9" && l.When == when
            && l.Detail.Contains("script RX1042") && l.Detail.Contains("Dr Ncube")
            && l.Id == script.Id && l.OwnerId == patient.Id);
        dispensed.Should().Contain(l => l.IsItem && l.Detail.Contains("21 x AMOXICILLIN 500MG")
            && l.Detail.Contains("batch AMX1 exp 2027-08-01"));

        var collected = await history.ForAsync(HistorySubject.Collected, day);
        collected.Should().Contain(l => !l.IsItem && l.Reference == "DSP-9");
        collected.Should().Contain(l => l.IsItem && l.Detail.Contains("AMOXICILLIN 500MG"));

        var received = await history.ForAsync(HistorySubject.Received, day);
        received.Should().Contain(l => !l.IsItem && l.Reference == "GR1" && l.When == when
            && l.Detail.Contains("Alpha Wholesalers") && l.Detail.Contains("INV-9"));
        received.Should().Contain(l => l.IsItem && l.Detail.Contains("10 x PANADO 500MG")
            && l.Detail.Contains("batch P1"));

        var patients = await history.ForAsync(HistorySubject.Patients, day);
        patients.Should().Contain(l => !l.IsItem && l.Reference == "F9" && l.When == when
            && l.Id == patient.Id);
        patients.Should().Contain(l => l.IsItem && l.Detail == "0772000111");
        patients.Should().Contain(l => l.IsItem && l.Detail.Contains("28 Woodstock Road"));

        var products = await history.ForAsync(HistorySubject.Products, day);
        products.Should().Contain(l => !l.IsItem && l.Reference == "PAN500" && l.When == when
            && l.Id == changed.Id);
        products.Should().Contain(l => l.IsItem && l.When == when && l.Detail.Contains("Price")
            && l.Detail.Contains("2.00 to 2.50") && l.Detail.Contains("Bukhosi Moyo"));
    }

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
