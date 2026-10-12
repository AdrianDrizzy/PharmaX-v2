using System.Globalization;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Offline;
using PharmaX.Application.Pharmacy;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Admin;

/// <summary>
/// Assembles the two read-only lists behind Administration's Control
/// Variables and System Console.
/// </summary>
/// <remarks>
/// Both answer a support question that otherwise needs someone to open
/// appsettings.json on the machine: what is this till set to, and what
/// is it doing. Every row names where its value came from, because the
/// commonest confusion is a setting changed at the till not matching the
/// installed file, and the two stores disagreeing is the answer.
/// </remarks>
public sealed class SystemInfoPresenter : PresenterBase<ISystemInfoView>
{
    private readonly PharmaXOptions _options;
    private readonly PharmacyProfile _pharmacy;
    private readonly IPharmacyDetailsService _details;
    private readonly IWorkstationSettings _settings;
    private readonly IConnectivityMonitor _connectivity;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    // appsettings.json, workstation.json or an environment variable,
    // later ones winning. The console cannot tell which of the three a
    // value came from, and labelling a workstation.json value
    // "appsettings.json" sent support to the wrong file (DEF-0024).
    private const string FromFile = "Configuration";
    private const string FromTill = "This workstation";
    private const string FromApp = "Running application";
    private const string FromPharmacy = "System console";
    private const string FromSetup = "System setup, for the whole pharmacy";
    private readonly Mvp.Branding _branding;

    public SystemInfoPresenter(PharmaXOptions options,
        PharmacyProfile pharmacy, IPharmacyDetailsService details,
        IWorkstationSettings settings, IConnectivityMonitor connectivity,
        ICurrentUser user, IClock clock, Mvp.Branding branding)
    {
        _branding = branding;
        _options = options;
        _pharmacy = pharmacy;
        _details = details;
        _settings = settings;
        _connectivity = connectivity;
        _user = user;
        _clock = clock;
    }

    protected override void Subscribe()
    {
        View.RefreshRequested += (_, _) => Enqueue(LoadAsync);
        View.CloseRequested += (_, _) => View.CloseView(true);
        View.SavePharmacyRequested += (_, _) => Enqueue(SavePharmacyAsync);
    }

    public override Task InitialiseAsync()
    {
        if (View.Mode == SystemInfoMode.SystemConsole)
            View.ShowPharmacyDetails(_pharmacy.Current);

        return LoadAsync();
    }

    /// <summary>
    /// Stores the trading name, VAT number and address typed at the
    /// console, then shows the list again so it reflects them.
    /// </summary>
    /// <remarks>
    /// The service applies the change to the running application as
    /// well as storing it, so the next receipt, the next export and
    /// every open window's title carry it without a restart.
    /// </remarks>
    private Task SavePharmacyAsync() => GuardAsync(async () =>
    {
        var result = await _details.SaveAsync(View.EnteredPharmacyDetails);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.ShowPharmacyDetails(_pharmacy.Current);
        View.ShowInfo("Pharmacy details saved. Receipts, reports, window "
                      + "titles and the sign-in and home screens use them "
                      + "from now on.");

        await LoadAsync();
    });

    private Task LoadAsync() => GuardAsync(async () =>
    {
        var rows = View.Mode == SystemInfoMode.ControlVariables
            ? await ControlVariablesAsync()
            : await SystemConsoleAsync();

        View.ShowCaption(View.Mode == SystemInfoMode.ControlVariables
            ? "What this workstation is set to"
            : "What this workstation is doing");

        View.ShowRows(rows);
    });

    private async Task<IReadOnlyList<SystemInfoRow>> ControlVariablesAsync()
    {
        var rows = new List<SystemInfoRow>();

        // The till's own store first, because where it has an opinion it
        // is the one that wins over the installed file.
        var currency = await _settings.GetAsync(WorkstationSettingKey.Currency);
        var receipt = await _settings.GetAsync(
            WorkstationSettingKey.ReceiptPrinter);
        var label = await _settings.GetAsync(WorkstationSettingKey.LabelPrinter);
        var drawer = await _settings.GetAsync(
            WorkstationSettingKey.CashDrawerPrinter);

        var installed = PharmacyDetails.From(_options.Pharmacy);
        var pharmacy = _pharmacy.Current;

        rows.Add(new("Pharmacy", "Trading name", pharmacy.TradingName,
            Held(pharmacy.TradingName, installed.TradingName)));
        rows.Add(new("Pharmacy", "Registered name",
            Or(pharmacy.RegisteredName),
            Held(pharmacy.RegisteredName, installed.RegisteredName)));
        rows.Add(new("Pharmacy", "Address", Or(OneLine(pharmacy.Address)),
            Held(pharmacy.Address, installed.Address)));
        rows.Add(new("Pharmacy", "VAT number", Or(pharmacy.VatNumber),
            Held(pharmacy.VatNumber, installed.VatNumber)));
        rows.Add(new("Pharmacy", "Telephone", Or(pharmacy.Telephone),
            Held(pharmacy.Telephone, installed.Telephone)));
        rows.Add(new("Pharmacy", "WhatsApp", Or(pharmacy.WhatsApp),
            Held(pharmacy.WhatsApp, installed.WhatsApp)));
        rows.Add(new("Pharmacy", "Licence number",
            Or(_options.Pharmacy.LicenceNumber), FromFile));

        rows.Add(new("Pharmacy", "Currency",
            currency ?? _options.Pharmacy.Currency,
            currency is null ? FromFile : FromTill));

        rows.Add(new("Workstation", "Branch code",
            _options.Workstation.BranchCode, FromFile));
        rows.Add(new("Workstation", "Till number",
            _options.Workstation.TillNumber, FromFile));
        rows.Add(new("Workstation", "Role",
            _options.Workstation.Role, FromFile));
        rows.Add(new("Workstation", "Branch id",
            _options.Workstation.BranchId, FromFile));
        rows.Add(new("Workstation", "Workstation id",
            _options.Workstation.WorkstationId, FromFile));

        rows.Add(new("Printing", "Receipt printer", Or(receipt, "Windows default"),
            receipt is null ? FromApp : FromTill));
        rows.Add(new("Printing", "Label printer", Or(label, "Windows default"),
            label is null ? FromApp : FromTill));
        rows.Add(new("Printing", "Cash drawer", Or(drawer, "Not configured"),
            drawer is null ? FromApp : FromTill));
        rows.Add(new("Printing", "Logo", _branding.HasLogo ? "Set" : "None set",
            _branding.HasLogo ? FromSetup : FromApp));

        rows.Add(new("Numbering", "Block size (days)",
            Number(_options.Numbering.BlockSizeDays), FromFile));
        rows.Add(new("Numbering", "Warn below (days)",
            Number(_options.Numbering.WarnBelowDays), FromFile));

        rows.Add(new("Connectivity", "Host timeout (seconds)",
            Number(_options.Connectivity.HostTimeoutSeconds), FromFile));
        rows.Add(new("Connectivity", "Reconcile every (seconds)",
            Number(_options.Connectivity.ReconcileIntervalSeconds), FromFile));

        rows.Add(new("Features", "Online claim switching",
            OnOff(_options.Features.OnlineClaimSwitching), FromFile));
        rows.Add(new("Features", "Messaging",
            OnOff(_options.Features.Messaging), FromFile));
        rows.Add(new("Features", "Cloud backup",
            OnOff(_options.Features.CloudBackup), FromFile));
        // Two different things that were easy to read as one: whether
        // PharmaX keeps its own books in real time, and whether it hands
        // figures on to an outside accounting package. "Accounting
        // posting: Off" sat above journals posting at every sale.
        rows.Add(new("Accounting", "Journals posted as trading happens",
            OnOff(_options.Accounting.AutoPost), FromFile));
        rows.Add(new("Features", "Export to accounting package",
            OnOff(_options.Features.AccountingPosting), FromFile));

        // The business thresholds the system runs on, so the pharmacy can
        // see them rather than discover them.
        rows.Add(new("Thresholds", "Controlled medicines from schedule",
            Number(Application.Dispensing.DispensingService
                .ControlledSchedule), FromApp));
        rows.Add(new("Thresholds", "Over-the-counter up to schedule",
            "2", FromApp));

        return rows;
    }

    private Task<IReadOnlyList<SystemInfoRow>> SystemConsoleAsync()
    {
        var pharmacy = _pharmacy.Current;
        var installed = PharmacyDetails.From(_options.Pharmacy);

        var rows = new List<SystemInfoRow>
        {
            new("Pharmacy", "Trading name", pharmacy.TradingName,
                Held(pharmacy.TradingName, installed.TradingName)),
            new("Pharmacy", "Registered name", Or(pharmacy.RegisteredName),
                Held(pharmacy.RegisteredName, installed.RegisteredName)),
            new("Pharmacy", "VAT number", Or(pharmacy.VatNumber),
                Held(pharmacy.VatNumber, installed.VatNumber)),
            new("Pharmacy", "Address", Or(OneLine(pharmacy.Address)),
                Held(pharmacy.Address, installed.Address)),
            new("Pharmacy", "Telephone", Or(pharmacy.Telephone),
                Held(pharmacy.Telephone, installed.Telephone)),
            new("Pharmacy", "WhatsApp", Or(pharmacy.WhatsApp),
                Held(pharmacy.WhatsApp, installed.WhatsApp)),

            new("Application", "Version",
                typeof(SystemInfoPresenter).Assembly.GetName().Version?
                    .ToString() ?? "unknown", FromApp),
            new("Application", "Machine", Environment.MachineName, FromApp),
            new("Application", "Windows user", Environment.UserName, FromApp),
            new("Application", "Signed in as", _user.Username, FromApp),
            new("Application", "Local time now",
                _clock.UtcNow.ToLocalTime()
                    .ToString("dd/MM/yyyy HH:mm:ss",
                              CultureInfo.CurrentCulture), FromApp),

            // What this till is actually reading and writing. The console
            // said "standalone, local.db" while the till ran on the
            // pharmacy's shared MySQL database (DEF-0024).
            new("Database", "Mode",
                _options.Database.IsShared
                    ? "Shared - every till on one database"
                    : _options.Database.HostConnection.Length == 0
                        ? "Standalone - this till's own database"
                        : "Standalone, reconciling with a host", FromFile),
            new("Database", "Database in use",
                _options.Database.IsShared
                    ? Describe(_options.Database.SharedConnection)
                    : _options.Database.ResolvedLocalConnection, FromFile),
            new("Database", "Host connection",
                _options.Database.IsShared
                    ? "Not used - the shared database is the host"
                    : _options.Database.HostConnection.Length == 0
                        ? "Not configured"
                        : Describe(_options.Database.HostConnection),
                FromFile),

            new("Connectivity", "Host state",
                Describe(_connectivity.State), FromApp),

            new("Logging", "Log folder",
                Environment.ExpandEnvironmentVariables(
                    _options.Logging.Directory), FromFile),
            new("Logging", "Files retained",
                Number(_options.Logging.RetainedFileCount), FromFile),

            new("Environment", "Seeded test users",
                OnOff(_options.Development.SeedTestUsers), FromFile)
        };

        return Task.FromResult<IReadOnlyList<SystemInfoRow>>(rows);
    }

    private static string Describe(ConnectivityState state) => state switch
    {
        ConnectivityState.Connected => "Host connected",
        ConnectivityState.Degraded => "Host slow to respond",
        _ => "Working locally"
    };

    /// <summary>
    /// A connection string as support needs to read it: which server and
    /// which database, and never the password.
    /// </summary>
    internal static string Describe(string connection)
    {
        if (string.IsNullOrWhiteSpace(connection)) return "Not configured";

        var parts = connection.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].Trim().ToUpperInvariant(),
                          p => p[1].Trim());

        string? Get(params string[] keys) => keys
            .Select(k => parts.GetValueOrDefault(k))
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        var server = Get("SERVER", "HOST", "DATA SOURCE") ?? "?";
        var port = Get("PORT");
        var database = Get("DATABASE", "INITIAL CATALOG") ?? "?";

        return port is null
            ? $"{database} on {server}"
            : $"{database} on {server}:{port}";
    }

    private static string Or(string? value, string fallback = "Not set") =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string OnOff(bool value) => value ? "On" : "Off";

    /// Where a pharmacy detail is held: the console once it has been
    /// changed there, otherwise the installed file.
    private static string Held(string current, string installed) =>
        string.Equals(current, installed, StringComparison.Ordinal)
            ? FromFile : FromPharmacy;

    private static string OneLine(string text) =>
        text.Replace(Environment.NewLine, ", ", StringComparison.Ordinal);

    private static string Number(int value) =>
        value.ToString(CultureInfo.CurrentCulture);
}
