// Puts a patient on a medical aid scheme, so that the next script
// issued to them raises a claim. Idempotent.
using Microsoft.EntityFrameworkCore;
using PharmaX.Persistence;

const string Connection =
    "Server=127.0.0.1;Port=3306;Database=PharmaX2;User ID=root;Password=root";

var options = new DbContextOptionsBuilder<HostDbContext>()
    .UseMySql(Connection, new MySqlServerVersion(new Version(8, 0, 42)))
    .Options;

await using var db = new HostDbContext(options);

var schemes = await db.MedicalAidSchemes.OrderBy(s => s.Code).ToListAsync();

Console.WriteLine("Schemes on file:");
foreach (var s in schemes) Console.WriteLine($"  {s.Code,-10} {s.Name}");

// The maintained lists are the pharmacy's own, so a fresh database has
// none. One is created here purely so the walkthrough has a scheme to
// claim from.
if (schemes.Count == 0)
{
    var created = PharmaX.Domain.Reference.MedicalAidScheme.Create(
        "ALPHA", "Alpha Medical Aid Society",
        "Demonstration scheme, added for testing");

    db.MedicalAidSchemes.Add(created);
    await db.SaveChangesAsync();

    schemes.Add(created);
    Console.WriteLine($"  created {created.Code}  {created.Name}");
}

var scheme = schemes[0];

foreach (var (file, membership, dependant) in new[]
{
    ("PF0100", "AM-4417290", "00"),
    ("PF0102", "AM-4417291", "01"),
})
{
    var patient = await db.Patients.FirstOrDefaultAsync(p => p.FileNumber == file);

    if (patient is null) { Console.WriteLine($"  {file}: not found"); continue; }

    patient.SetMedicalAid(scheme.Id, membership, dependant);

    Console.WriteLine($"  {patient.FullName,-16} -> {scheme.Name}, "
                      + $"{membership} ({dependant})");
}

await db.SaveChangesAsync();

Console.WriteLine();
Console.WriteLine("Claims on file:");

foreach (var c in await db.MedicalAidClaims.ToListAsync())
    Console.WriteLine($"  {c.ClaimNumber}  claimed {c.Claimed.Amount:0.00}  "
                      + $"outstanding {c.Outstanding.Amount:0.00}  {c.Status}");

Console.WriteLine("Done.");
