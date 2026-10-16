using System.Text.Json;
using System.Text.Json.Serialization;

namespace PharmaX.Desktop.Setup;

/// <summary>
/// What this particular machine is, as against what PharmaX is.
/// </summary>
/// <remarks>
/// Written once by the first-run wizard and read on every start after
/// that. The two facts that matter are the workstation's identity and
/// where its database lives, and both are properties of the machine
/// rather than of the installation.
/// </remarks>
public sealed record WorkstationProfile
{
    /// <summary>
    /// Standalone: one PC with its own SQLite file, answering to nobody.
    /// Server: the machine holding the database the pharmacy shares -
    /// on a small site, the pharmacist's PC.
    /// Till: a workstation that reads and writes the server's database.
    /// </summary>
    public WorkstationKind Kind { get; init; } = WorkstationKind.Standalone;

    public string BranchCode { get; init; } = "1";
    public string TillNumber { get; init; } = "1";

    /// <summary>
    /// This machine, for the rest of its life.
    /// </summary>
    /// <remarks>
    /// Generated here and nowhere else. It is stamped on every document
    /// this workstation creates, and the unique index on
    /// (WorkstationId, DocumentNumber) means two machines sharing one id
    /// would collide on the first receipt they both issued. An installer
    /// that copied a configured appsettings.json onto three tills would
    /// do exactly that, which is the whole reason this file exists
    /// separately from the one the installer ships.
    /// </remarks>
    public Guid WorkstationId { get; init; } = Guid.NewGuid();

    /// Shared by every machine at one site, so their rows belong together.
    public Guid BranchId { get; init; } =
        Guid.Parse("00000000-0000-0000-0000-000000000001");

    public string ServerHost { get; init; } = "";
    public int ServerPort { get; init; } = 3306;
    public string DatabaseName { get; init; } = "PharmaX";

    /// The login the installer makes, so nobody has to think of one.
    public string DatabaseUser { get; init; } = "pharmax";
    public string DatabasePassword { get; init; } = "";

    /// "8.0.42-mysql" or "10.11.0-mariadb", detected by the wizard.
    public string ServerVersion { get; init; } = "";

    public string TradingName { get; init; } = "PharmaX";
    public string Currency { get; init; } = "USD";
    public string VatNumber { get; init; } = "";
    public string Address { get; init; } = "";

    /// PharmaX's own copy of the logo chosen at setup, or blank. Becomes
    /// the pharmacy's - every till's - at the first sign-in, unless the
    /// pharmacy already has one.
    public string LogoPath { get; init; } = "";

    /// True where this machine reads and writes a database over the
    /// network rather than a file of its own.
    public bool IsShared => Kind != WorkstationKind.Standalone;

    /// <summary>
    /// The connection string the rest of PharmaX will use.
    /// </summary>
    /// <remarks>
    /// The server's own database is reached over the loopback address
    /// rather than through its network name: it is the same database
    /// either way, and a machine that cannot resolve its own hostname -
    /// which happens on a workgroup network with no DNS, which is most
    /// small pharmacies - would otherwise fail to reach a database
    /// sitting on its own disk.
    /// </remarks>
    public string SharedConnection
    {
        get
        {
            if (!IsShared) return "";

            // Built rather than joined, so a password with a ; or a quote
            // in it is quoted instead of cutting the string short.
            return new MySqlConnector.MySqlConnectionStringBuilder
            {
                Server = Kind == WorkstationKind.Server
                    ? "127.0.0.1"
                    : ServerHost,
                Port = (uint)ServerPort,
                Database = DatabaseName,
                UserID = DatabaseUser,
                Password = DatabasePassword
            }.ConnectionString;
        }
    }
}

public enum WorkstationKind { Standalone, Server, Till }

/// <summary>
/// Reads and writes the machine's profile.
/// </summary>
/// <remarks>
/// Deliberately in ProgramData rather than beside the executable. The
/// installer owns Program Files and replaces what is there on every
/// upgrade, so a workstation identity kept next to the .exe would be
/// wiped by the first update - and the machine would come back with a
/// new identity and a numbering series that had already been used.
/// </remarks>
public static class WorkstationProvisioning
{
    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "PharmaX");

    /// The machine's profile, beside the logs and the branding.
    public static string FilePath =>
        Path.Combine(Folder, "workstation.json");

    public static bool Exists => File.Exists(FilePath);

    /// <summary>
    /// Writes the profile as a configuration file PharmaX can layer over
    /// its shipped defaults.
    /// </summary>
    /// <remarks>
    /// Written in the shape of appsettings.json rather than as its own
    /// format, so that reading it needs no code at all: Program.cs adds
    /// it as one more JSON source and the options bind as before.
    /// </remarks>
    public static void Save(WorkstationProfile profile)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, Render(profile));
    }

    /// <summary>
    /// The profile as the JSON that will be written.
    /// </summary>
    /// <remarks>
    /// Separate from Save so that the shape can be asserted without
    /// writing to the machine PharmaX is installed on - a test that
    /// exercised Save would overwrite the real workstation.json, which
    /// on a developer's own PC is the one file they cannot afford to
    /// lose.
    /// </remarks>
    /// <summary>
    /// Readable on purpose: somebody diagnosing a till over the phone
    /// will be reading this file out loud.
    /// </summary>
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string Render(WorkstationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var settings = new Dictionary<string, object>
        {
            ["Pharmacy"] = new Dictionary<string, object>
            {
                ["TradingName"] = profile.TradingName,
                ["Currency"] = profile.Currency,
                ["VatNumber"] = profile.VatNumber,
                ["Address"] = profile.Address,
                ["LogoPath"] = profile.LogoPath
            },

            ["Workstation"] = new Dictionary<string, object>
            {
                ["BranchCode"] = profile.BranchCode,
                ["TillNumber"] = profile.TillNumber,
                ["Role"] = profile.Kind == WorkstationKind.Server
                    ? "Host"
                    : "Client",
                ["BranchId"] = profile.BranchId.ToString(),
                ["WorkstationId"] = profile.WorkstationId.ToString()
            },

            ["Database"] = new Dictionary<string, object>
            {
                ["SharedConnection"] = profile.SharedConnection,
                ["ServerVersion"] = profile.ServerVersion
            },

            // Kept so that a later run of the wizard can show what was
            // chosen rather than making somebody retype it. Ignored by
            // the options binder, which knows nothing about this section.
            ["Setup"] = new Dictionary<string, object>
            {
                ["Kind"] = profile.Kind.ToString(),
                ["ServerHost"] = profile.ServerHost,
                ["ServerPort"] = profile.ServerPort,
                ["DatabaseName"] = profile.DatabaseName,
                ["DatabaseUser"] = profile.DatabaseUser,
                ["ProvisionedAtUtc"] = DateTime.UtcNow.ToString("O")
            }
        };

        return JsonSerializer.Serialize(settings, Indented);
    }

    /// <summary>
    /// What the wizard was told last time, so that re-running it starts
    /// from the existing answers. Null where the machine has never been
    /// set up, or where the file cannot be read.
    /// </summary>
    /// <remarks>
    /// The password is not read back. It is in the connection string in
    /// the same file, and parsing it out to prefill a box the operator
    /// can see would be putting it on screen for no good reason; they
    /// retype it, or leave the machine as it is.
    /// </remarks>
    public static WorkstationProfile? Read()
    {
        if (!Exists) return null;

        try
        {
            using var document = JsonDocument.Parse(
                File.ReadAllText(FilePath));

            var root = document.RootElement;

            var setup = root.TryGetProperty("Setup", out var s)
                ? s
                : default;

            var workstation = root.TryGetProperty("Workstation", out var w)
                ? w
                : default;

            var pharmacy = root.TryGetProperty("Pharmacy", out var p)
                ? p
                : default;

            return new WorkstationProfile
            {
                Kind = Enum.TryParse<WorkstationKind>(
                    Text(setup, "Kind"), out var kind)
                    ? kind
                    : WorkstationKind.Standalone,

                ServerHost = Text(setup, "ServerHost"),
                ServerPort = Number(setup, "ServerPort", 3306),
                DatabaseName = Fallback(Text(setup, "DatabaseName"),
                                        "PharmaX"),
                DatabaseUser = Fallback(Text(setup, "DatabaseUser"),
                                        "pharmax"),

                BranchCode = Fallback(Text(workstation, "BranchCode"), "1"),
                TillNumber = Fallback(Text(workstation, "TillNumber"), "1"),

                WorkstationId = Guid.TryParse(
                    Text(workstation, "WorkstationId"), out var id)
                    ? id
                    : Guid.NewGuid(),

                BranchId = Guid.TryParse(
                    Text(workstation, "BranchId"), out var branch)
                    ? branch
                    : Guid.Parse("00000000-0000-0000-0000-000000000001"),

                TradingName = Fallback(Text(pharmacy, "TradingName"),
                                       "PharmaX"),
                Currency = Fallback(Text(pharmacy, "Currency"), "USD"),
                VatNumber = Text(pharmacy, "VatNumber"),
                Address = Text(pharmacy, "Address"),
                LogoPath = Text(pharmacy, "LogoPath")
            };
        }
        catch (JsonException)
        {
            // A corrupt file is treated as no file: the wizard runs and
            // writes a good one over it. Refusing to start because a
            // settings file will not parse would be worse.
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string Text(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object
        && parent.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static int Number(JsonElement parent, string name, int fallback) =>
        parent.ValueKind == JsonValueKind.Object
        && parent.TryGetProperty(name, out var value)
        && value.TryGetInt32(out var number)
            ? number
            : fallback;

    private static string Fallback(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
