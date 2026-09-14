// Seeds the data the walkthrough needs, so that every screen in the
// test script has something on it. Idempotent: safe to run twice.
using Microsoft.EntityFrameworkCore;
using PharmaX.Domain.Batches;
using PharmaX.Domain.Dispensing;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Patients;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Suppliers;
using PharmaX.Persistence;

const string Connection =
    "Server=127.0.0.1;Port=3306;Database=PharmaX2;User ID=root;Password=root";

var options = new DbContextOptionsBuilder<HostDbContext>()
    .UseMySql(Connection, new MySqlServerVersion(new Version(8, 0, 42)))
    .Options;

await using var db = new HostDbContext(options);

var today = DateOnly.FromDateTime(DateTime.Today);
var pharmacist = await db.Users.FirstAsync(u => u.Username == "test.pharmacist");

Console.WriteLine("=== Stock levels, for the alert centre ===");

// Six real lines off the pharmacy's own price list. The reorder levels
// are what make them alertable at all: a product with no reorder level
// has no reorder policy, so the alert centre leaves it alone.
var levels = new (string Code, decimal OnHand, decimal Reorder)[]
{
    ("1454", 40m, 100m),    // Ibuprofen - low
    ("1932", 0m, 20m),      // Suture - out of stock
    ("469", 8m, 25m),       // Acetone - low
    ("1", 500m, 100m),      // comfortably stocked, so not every line alerts
};

foreach (var (code, onHand, reorder) in levels)
{
    var product = await db.Products.FirstOrDefaultAsync(p => p.Code == code);
    if (product is null) { Console.WriteLine($"  {code}: not found"); continue; }

    product.SetQuantityOnHand(onHand);
    product.SetReorderLevel(reorder);

    Console.WriteLine($"  {code} {product.Description[..Math.Min(38,
        product.Description.Length)],-38} on hand {onHand,6}, reorder {reorder}");
}

await db.SaveChangesAsync();

Console.WriteLine();
Console.WriteLine("=== Batches, for the expiry alerts ===");

// One of each severity the alert centre knows how to show.
var batches = new (string Code, string Number, int Days, decimal Qty)[]
{
    ("1454", "IBU-2024-A", -12, 24m),   // expired
    ("1454", "IBU-2025-B", 18, 60m),    // critical, inside 30 days
    ("469",  "ACE-2025-C", 55, 15m),    // warning, inside 90 days
};

foreach (var (code, number, days, qty) in batches)
{
    var product = await db.Products.FirstAsync(p => p.Code == code);

    var exists = await db.Batches.AnyAsync(b =>
        b.ProductId == product.Id && b.BatchNumber == number);

    if (exists) { Console.WriteLine($"  {number}: already there"); continue; }

    db.Batches.Add(Batch.Receive(product.Id, number, today.AddDays(days),
        qty, Money.Of(4.20m), DateTime.UtcNow));

    Console.WriteLine($"  {number,-12} expires {today.AddDays(days)}  "
                      + $"({days,4} days)  qty {qty}");
}

await db.SaveChangesAsync();

Console.WriteLine();
Console.WriteLine("=== Patients and scripts ===");

// A patient with an allergy, so the dispensing screen has a clinical
// warning to raise rather than a clean run every time.
var lindiwe = await db.Patients.FirstOrDefaultAsync(p => p.FileNumber == "PF0102");

if (lindiwe is null)
{
    lindiwe = Patient.Create("PF0102", "Dube", "Lindiwe",
        new DateOnly(1979, 11, 2));

    db.Patients.Add(lindiwe);
    await db.SaveChangesAsync();

    var profile = ClinicalProfile.For(lindiwe.Id);
    profile.RecordAllergy("Penicillin", "Severe", "Rash and swelling");

    db.ClinicalProfiles.Add(profile);
    await db.SaveChangesAsync();

    Console.WriteLine("  Lindiwe Dube (PF0102) - allergic to penicillin");
}
else
{
    Console.WriteLine("  Lindiwe Dube (PF0102) already there");
}

// A script with repeats left, for the pharmacist to dispense during the
// walkthrough rather than having to capture one first.
var script = await db.Scripts.FirstOrDefaultAsync(s => s.ScriptNumber == "SC-DEMO-01");

if (script is null)
{
    script = Script.Capture("SC-DEMO-01", lindiwe.Id, "Dr M Chikwanda",
        today, repeatsAuthorised: 5);

    db.Scripts.Add(script);
    await db.SaveChangesAsync();

    Console.WriteLine("  Script SC-DEMO-01 for Lindiwe Dube, 5 repeats");
}
else
{
    Console.WriteLine("  Script SC-DEMO-01 already there");
}

Console.WriteLine();
Console.WriteLine("=== Waiting at the till ===");

var waiting = await db.Dispensings
    .Include(d => d.Lines)
    .Where(d => d.Status == DispensingStatus.Issued && d.SaleId == null)
    .ToListAsync();

foreach (var d in waiting)
{
    var patient = await db.Patients.FirstAsync(p => p.Id == d.PatientId);

    Console.WriteLine($"  {d.DocumentNumber}  {patient.FullName,-16}  "
                      + $"owes {d.CoPaymentTotal.Amount,8:0.00}");
}

if (waiting.Count == 0) Console.WriteLine("  (nothing waiting)");

Console.WriteLine();
Console.WriteLine("=== Suppliers ===");

foreach (var supplier in await db.Suppliers.ToListAsync())
    Console.WriteLine($"  {supplier.Code,-8} {supplier.Name}");

Console.WriteLine();
Console.WriteLine("Done.");
