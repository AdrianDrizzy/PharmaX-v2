using System.Globalization;
using System.Security.Cryptography;
using PharmaX.Application.Licensing;

// The vendor's tool: makes membership activation codes. The private key and
// the log of codes issued live in the vendor's own profile, never the repo.
var folder = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "PharmaX.CodeMaker");
var keyFile = Path.Combine(folder, "private.key");
var logFile = Path.Combine(folder, "issued.csv");
Directory.CreateDirectory(folder);

switch (args)
{
    case ["keygen"]:
        if (File.Exists(keyFile))
        {
            Console.Error.WriteLine($"A key already exists at {keyFile}. Refusing to replace it: "
                + "every installed PharmaX trusts that one.");
            return 1;
        }
        using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            File.WriteAllText(keyFile, Convert.ToBase64String(key.ExportPkcs8PrivateKey()));
            Console.WriteLine($"Private key written to {keyFile}");
            Console.WriteLine("BACK IT UP somewhere safe and off this PC. Without it no new codes can be made.");
            Console.WriteLine();
            Console.WriteLine("Public key - paste into LicenceCode.PublicKey:");
            Console.WriteLine(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
        }
        return 0;

    case ["issue", var idText, var planText, ..] :
        if (LicenceCode.ParsePharmacyId(idText) is not { } id)
            return Fail("That is not a Pharmacy ID. Copy it from PharmaX, Administration > Membership.");
        if (!Enum.TryParse<MembershipPlan>(planText, ignoreCase: true, out var plan)
            || !Enum.IsDefined(plan))
            return Fail("Plan must be monthly, quarterly or yearly.");
        if (!File.Exists(keyFile))
            return Fail("No private key yet. Run: codemaker keygen");

        var name = args.Length > 4 && args[3] == "--name" ? args[4] : "";
        var today = DateOnly.FromDateTime(DateTime.Today);
        var until = LicenceCode.NextValidUntil(today, LastValidUntil(id), plan);

        using (var key = ECDsa.Create())
        {
            key.ImportPkcs8PrivateKey(Convert.FromBase64String(File.ReadAllText(keyFile)), out _);
            var code = LicenceCode.Issue(new LicencePayload(plan, id, until, today), key);

            File.AppendAllText(logFile, string.Join(',', Iso(today), id, Csv(name), plan,
                Iso(until), code) + Environment.NewLine);

            Console.WriteLine($"{plan} membership{(name.Length > 0 ? " for " + name : "")}, "
                + $"valid until {LicenceState.Day(until)}:");
            Console.WriteLine();
            Console.WriteLine(code);
            Console.WriteLine();
            Console.WriteLine("Message to send:");
            Console.WriteLine($"Thank you for your payment. Your PharmaX {plan.ToString().ToLowerInvariant()} "
                + $"membership runs until {LicenceState.Day(until)}. In PharmaX, open Administration > Membership, "
                + $"paste this code and click Activate:{Environment.NewLine}{code}");
        }
        return 0;

    case ["list"]:
        foreach (var row in Rows().GroupBy(r => r.Id).Select(g => g.MaxBy(r => r.Until)!))
            Console.WriteLine($"{LicenceCode.FormatPharmacyId(row.Id)}  {row.Until:yyyy-MM-dd}  {row.Plan,-9}  {row.Name}");
        return 0;

    default:
        Console.WriteLine("""
            codemaker keygen                                   make your signing key (once, ever)
            codemaker issue <pharmacy-id> <monthly|quarterly|yearly> [--name "Pharmacy name"]
            codemaker list                                     each pharmacy's current end date
            """);
        return args.Length == 0 ? 0 : 1;
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

// Commas would split the column; names are only for the vendor's own reading.
static string Csv(string s) => s.Replace(',', ' ');

DateOnly? LastValidUntil(Guid id) =>
    Rows().Where(r => r.Id == id).Select(r => (DateOnly?)r.Until).Max();

IEnumerable<(Guid Id, string Name, string Plan, DateOnly Until)> Rows() =>
    !File.Exists(logFile) ? [] :
    File.ReadAllLines(logFile)
        .Select(l => l.Split(','))
        .Where(p => p.Length >= 6)
        .Select(p => (Guid.Parse(p[1]), p[2], p[3],
                      DateOnly.ParseExact(p[4], "yyyy-MM-dd", CultureInfo.InvariantCulture)));
