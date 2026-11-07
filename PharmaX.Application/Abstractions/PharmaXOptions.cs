namespace PharmaX.Application.Abstractions;

public sealed class PharmaXOptions
{
    public PharmacyOptions Pharmacy { get; init; } = new();
    public WorkstationOptions Workstation { get; init; } = new();
    public DatabaseOptions Database { get; init; } = new();
    public ConnectivityOptions Connectivity { get; init; } = new();
    public NumberingOptions Numbering { get; init; } = new();
    public FeatureFlags Features { get; init; } = new();
    public AlertOptions Alerts { get; init; } = new();
    public AccountingOptions Accounting { get; init; } = new();
    public LoggingOptions Logging { get; init; } = new();
    public DevelopmentOptions Development { get; init; } = new();
    public AssistantOptions Assistant { get; init; } = new();
}

/// <summary>
/// The offline assistant on the reports screen: a small language model,
/// run on this computer, that turns a request in plain words into a
/// report with its dates and filters filled in.
/// </summary>
/// <remarks>
/// Optional. Where the model file is not on this computer the reports
/// screen simply has no Ask box. The model is a separate download of
/// about 1.3 GB, so it is not carried inside the installer; setup offers
/// to download it (the "assistant" task in installer\PharmaX.iss).
/// </remarks>
public sealed class AssistantOptions
{
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// A Qwen3.5 model in GGUF form. The prompt is written in Qwen's chat
    /// format, so another family of model would need its own.
    /// </summary>
    public string ModelPath { get; init; } =
        @"%PROGRAMDATA%\PharmaX\assistant\Qwen3.5-2B-Q4_K_M.gguf";

    /// <summary>
    /// Where the model's reading of the report list is kept between runs,
    /// so it reads it once rather than every time the screen opens.
    /// </summary>
    public string CacheDirectory { get; init; } =
        @"%PROGRAMDATA%\PharmaX\assistant\cache";

    /// <summary>Processor threads to use; 0 means half the logical processors.</summary>
    public int Threads { get; init; }

    public string ResolvedModelPath =>
        Environment.ExpandEnvironmentVariables(ModelPath);

    public string ResolvedCacheDirectory =>
        Environment.ExpandEnvironmentVariables(CacheDirectory);
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

    public string TradingName { get; init; } = "PharmaX";
    public string RegisteredName { get; init; } = "";
    public string Address { get; init; } = "";
    public string VatNumber { get; init; } = "";
    public string LicenceNumber { get; init; } = "";

    /// The number the receipt and the dispensing label are answered on.
    public string Telephone { get; init; } = "";

    /// The number the pharmacy takes messages on, where it has one.
    public string WhatsApp { get; init; } = "";

    /// The logo chosen when PharmaX was set up, or blank. Becomes the
    /// pharmacy's at the first sign-in unless it already has one.
    public string LogoPath { get; init; } = "";
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

    /// <summary>
    /// One database for the whole pharmacy, on a machine every
    /// workstation can reach. Setting it turns off standalone mode.
    /// </summary>
    /// <remarks>
    /// This is the arrangement a single site on one switch actually
    /// wants: the pharmacist dispenses and the tills see it at once,
    /// with no sync engine in between. It trades away the offline-first
    /// promise of NET-002 - if the machine holding the database is off,
    /// the tills do not fall back to working locally, they stop - which
    /// is the right trade for one site and the wrong one for a branch on
    /// the end of a bad line. Leave it empty and nothing changes: the
    /// workstation keeps its own SQLite file as before.
    /// </remarks>
    public string SharedConnection { get; init; } = "";

    /// <summary>
    /// Which server the shared database runs on, as a version string:
    /// "8.0.42-mysql" or "10.11.0-mariadb". Empty uses the built-in
    /// default.
    /// </summary>
    /// <remarks>
    /// MySQL and MariaDB have diverged enough that the driver emits
    /// different SQL for each, so this is not cosmetic: a context told
    /// it is talking to MariaDB while connected to MySQL generates
    /// statements the server can reject. Configuration rather than a
    /// constant because a pharmacy installs whichever its supplier
    /// supports, and both are reasonable.
    /// </remarks>
    public string ServerVersion { get; init; } = "";

    /// True where the pharmacy runs on one shared database.
    public bool IsShared => !string.IsNullOrWhiteSpace(SharedConnection);

    public string ResolvedLocalConnection =>
        Environment.ExpandEnvironmentVariables(LocalConnection);
}

/// <summary>
/// When stock counts as a problem worth telling the staff about.
/// </summary>
/// <remarks>
/// Pharmacy-wide rather than per-workstation: whether a batch three
/// months from expiry is a worry is a property of how the business
/// buys, not of which till happens to be showing it, and two tills
/// disagreeing about what is urgent would be worse than either answer.
/// </remarks>
public sealed class AlertOptions
{
    /// <summary>
    /// How far ahead an expiry starts being worth mentioning.
    /// </summary>
    /// <remarks>
    /// Ninety days is roughly the point at which something can still be
    /// sold through, returned to the wholesaler, or swapped - after
    /// which the pharmacy is going to eat the cost.
    /// </remarks>
    public int ExpiryWarningDays { get; init; } = 90;

    /// Inside this, it is no longer a warning but a thing to act on today.
    public int ExpiryCriticalDays { get; init; } = 30;

    /// <summary>
    /// Turns the whole alert centre off, for a site that would rather
    /// run its own stock reports.
    /// </summary>
    public bool Enabled { get; init; } = true;
}

/// <summary>
/// The pharmacy's own books: whether PharmaX keeps a ledger, and which
/// accounts a day's takings post to.
/// </summary>
/// <remarks>
/// Not to be confused with Features.AccountingPosting, which is about
/// handing figures to an outside accounting package. This one is about
/// whether PharmaX keeps a ledger of its own at all, and where the
/// figures land when it does.
/// <para>
/// The account codes are configuration rather than constants because
/// every pharmacy's accountant has their own chart, and a hard-coded
/// account code posts somebody else's trial balance into the wrong row.
/// The defaults match the chart PharmaX seeds, so an installation that
/// has not been told otherwise still balances.
/// </para>
/// </remarks>
public sealed class AccountingOptions
{
    /// <summary>
    /// Writes a journal for every sale, refund, delivery and cash-up.
    /// </summary>
    /// <remarks>
    /// On by default, because books that only start when somebody
    /// remembers to switch them on have a hole in them from opening day
    /// to whenever that was. Turning it off stops new journals; it does
    /// not remove the ones already posted, and the chart, the manual
    /// journals and the trial balance all keep working.
    /// </remarks>
    public bool AutoPost { get; init; } = true;

    // Defaults are taken from LedgerCode rather than written as literals,
    // so that the configurable chart and the seeded one cannot drift
    // apart. They did drift once (DEF-0004): two charts numbered the same
    // accounts differently, and a cash sale posted to two different
    // places depending on which engine wrote the journal.
    public string Cash { get; init; } = Domain.Finance.LedgerCode.CashOnHand;
    public string Card { get; init; } = Domain.Finance.LedgerCode.Bank;
    public string Eft { get; init; } = Domain.Finance.LedgerCode.Bank;
    public string EcoCash { get; init; } = Domain.Finance.LedgerCode.EcoCashWallet;
    public string AccountsReceivable { get; init; } = Domain.Finance.LedgerCode.TradeDebtors;
    public string MedicalAidReceivable { get; init; } = Domain.Finance.LedgerCode.MedicalAidDebtors;
    public string Voucher { get; init; } = Domain.Finance.LedgerCode.VouchersRedeemed;
    public string SalesIncome { get; init; } = Domain.Finance.LedgerCode.Sales;
    public string VatOutput { get; init; } = Domain.Finance.LedgerCode.VatControl;
    public string Rounding { get; init; } = Domain.Finance.LedgerCode.RoundingDifferences;

    /// <summary>
    /// Where a tender lands. Every journal reads this - the ones posted
    /// as trading happens and the day-end - so a tender can only ever
    /// land in one place. A tender with no setting of its own falls back
    /// to the standard chart rather than to nothing at all: a journal
    /// that refuses to balance because somebody added a tender type is
    /// worse than one posting a line an accountant can see and move.
    /// </summary>
    public string CodeFor(Domain.Sales.TenderType tender) => tender switch
    {
        Domain.Sales.TenderType.Cash => Cash,
        Domain.Sales.TenderType.Card => Card,
        Domain.Sales.TenderType.Eft => Eft,
        Domain.Sales.TenderType.EcoCash => EcoCash,
        Domain.Sales.TenderType.Account => AccountsReceivable,
        Domain.Sales.TenderType.MedicalAid => MedicalAidReceivable,
        Domain.Sales.TenderType.Voucher => Voucher,
        _ => Domain.Finance.LedgerCode.For(tender)
    };
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
