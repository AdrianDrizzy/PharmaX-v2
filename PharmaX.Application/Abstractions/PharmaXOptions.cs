namespace PharmaX.Application.Abstractions;

public sealed class PharmaXOptions
{
    public PharmacyOptions Pharmacy { get; init; } = new();
    public WorkstationOptions Workstation { get; init; } = new();
    public DatabaseOptions Database { get; init; } = new();
    public ConnectivityOptions Connectivity { get; init; } = new();
    public NumberingOptions Numbering { get; init; } = new();
    public FeatureFlags Features { get; init; } = new();
    public LoggingOptions Logging { get; init; } = new();
    public DevelopmentOptions Development { get; init; } = new();
}

/// <summary>
/// The fiscal particulars that must appear on every receipt
/// (specification Section 6.10). Configuration rather than constants,
/// because the installer writes them per site and a hard-coded VAT
/// number on someone else's receipt is a compliance problem.
/// </summary>
public sealed class PharmacyOptions
{
    /// <summary>
    /// The currency the pharmacy trades in, as an ISO 4217 code. Drives
    /// the symbol on every figure, the notes and coins in the cash-up
    /// grid, and the code stored against every amount.
    /// </summary>
    /// <remarks>
    /// Deliberately the pharmacy's currency rather than the
    /// workstation's locale. Section 4.3 leaves InvariantGlobalization
    /// false so that formatting follows a real culture, but which one is
    /// a property of the business: a workstation reinstalled with a
    /// different Windows locale must not start quoting a different
    /// currency at customers. This is the installed default; system
    /// setup can change it at the till and that choice wins.
    /// </remarks>
    public string Currency { get; init; } = "USD";

    public string TradingName { get; init; } = "PharmaX Pharmacy";
    public string RegisteredName { get; init; } = "";
    public string Address { get; init; } = "";
    public string VatNumber { get; init; } = "";
    public string LicenceNumber { get; init; } = "";
}

public sealed class WorkstationOptions
{
    public string BranchCode { get; init; } = "1";
    public string TillNumber { get; init; } = "1";
    public string Role { get; init; } = "Client";     // Client or Host

    // Stamped onto every record so that rows created on two workstations
    // during an outage can be told apart on reconciliation (NET-020).
    // The installer writes these once; they never change for the machine.
    public string BranchId { get; init; } =
        "00000000-0000-0000-0000-000000000001";

    public string WorkstationId { get; init; } =
        "00000000-0000-0000-0000-000000000001";

    public bool IsHost =>
        Role.Equals("Host", StringComparison.OrdinalIgnoreCase);
}

public sealed class DatabaseOptions
{
    public string Provider { get; init; } = "MariaDb"; // MariaDb or SqlServer
    public string HostConnection { get; init; } = "";
    public string LocalConnection { get; init; } = "";

    public string ResolvedLocalConnection =>
        Environment.ExpandEnvironmentVariables(LocalConnection);
}

public sealed class ConnectivityOptions
{
    public int HostTimeoutSeconds { get; init; } = 2;
    public int ReconcileIntervalSeconds { get; init; } = 30;
}

public sealed class NumberingOptions
{
    public int BlockSizeDays { get; init; } = 30;
    public int WarnBelowDays { get; init; } = 7;
}

public sealed class FeatureFlags
{
    public bool OnlineClaimSwitching { get; init; }
    public bool Messaging { get; init; }
    public bool CloudBackup { get; init; }
    public bool AccountingPosting { get; init; }
}

/// <summary>
/// Switches that exist only to make a development machine convenient,
/// and that must never be on at a site. Default off, and the file that
/// turns them on - appsettings.Development.json - is excluded by
/// .gitignore and is not written by the installer.
/// </summary>
public sealed class DevelopmentOptions
{
    /// <summary>
    /// Creates one account per standard role with a known password, so
    /// that permission and authorisation paths can be walked without an
    /// administrator hand-building six users first.
    /// </summary>
    public bool SeedTestUsers { get; init; }
}

public sealed class LoggingOptions
{
    public string Directory { get; init; } = @"%PROGRAMDATA%\PharmaX\logs";
    public int RetainedFileCount { get; init; } = 30;

    public string ResolvedDirectory =>
        Environment.ExpandEnvironmentVariables(Directory);
}
