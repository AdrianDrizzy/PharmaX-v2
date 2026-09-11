using System.Globalization;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Offline;
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
    private readonly IWorkstationSettings _settings;
    private readonly IConnectivityMonitor _connectivity;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    private const string FromFile = "appsettings.json";
    private const string FromTill = "This workstation";
    private const string FromApp = "Running application";

    public SystemInfoPresenter(PharmaXOptions options,
        IWorkstationSettings settings, IConnectivityMonitor connectivity,
        ICurrentUser user, IClock clock)
    {
        _options = options;
        _settings = settings;
        _connectivity = connectivity;
        _user = user;
        _clock = clock;
    }

    protected override void Subscribe()
    {
        View.RefreshRequested += (_, _) => _ = LoadAsync();
        View.CloseRequested += (_, _) => View.CloseView(true);
    }

    public override Task InitialiseAsync() => LoadAsync();

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
        var logo = await _settings.GetAsync(WorkstationSettingKey.LogoPath);
        var receipt = await _settings.GetAsync(
            WorkstationSettingKey.ReceiptPrinter);
        var label = await _settings.GetAsync(WorkstationSettingKey.LabelPrinter);
        var drawer = await _settings.GetAsync(
            WorkstationSettingKey.CashDrawerPrinter);

        rows.Add(new("Pharmacy", "Trading name",
            _options.Pharmacy.TradingName, FromFile));
        rows.Add(new("Pharmacy", "Registered name",
            Or(_options.Pharmacy.RegisteredName), FromFile));
        rows.Add(new("Pharmacy", "Address",
            Or(_options.Pharmacy.Address), FromFile));
        rows.Add(new("Pharmacy", "VAT number",
            Or(_options.Pharmacy.VatNumber), FromFile));
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
        rows.Add(new("Printing", "Logo file", Or(logo, "None set"),
            logo is null ? FromApp : FromTill));

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
        rows.Add(new("Features", "Accounting posting",
            OnOff(_options.Features.AccountingPosting), FromFile));

        return rows;
    }

    private Task<IReadOnlyList<SystemInfoRow>> SystemConsoleAsync()
    {
        var rows = new List<SystemInfoRow>
        {
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

            new("Database", "Local database",
                _options.Database.ResolvedLocalConnection, FromFile),
            new("Database", "Host provider",
                _options.Database.Provider, FromFile),
            new("Database", "Host connection",
                _options.Database.HostConnection.Length == 0
                    ? "Not configured - this till trades standalone"
                    : "Configured", FromFile),

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

    private static string Or(string? value, string fallback = "Not set") =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string OnOff(bool value) => value ? "On" : "Off";

    private static string Number(int value) =>
        value.ToString(CultureInfo.CurrentCulture);
}
