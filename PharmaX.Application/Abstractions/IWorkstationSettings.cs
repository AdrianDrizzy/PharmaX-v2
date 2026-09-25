namespace PharmaX.Application.Abstractions;

/// <summary>
/// Settings that belong to this workstation rather than to the
/// pharmacy: which printer this till uses, and anything else that is a
/// property of the machine on the counter.
/// </summary>
/// <remarks>
/// Kept in the local database rather than in appsettings.json because
/// the installer writes that file and would overwrite a choice made at
/// the till. Everything here is per-workstation by definition, so it
/// never syncs to the host.
/// </remarks>
public interface IWorkstationSettings
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);

    Task SetAsync(string key, string? value, CancellationToken ct = default);
}

/// <summary>
/// The setting keys PharmaX reads by name. Constants rather than
/// literals so a typo is a compile error instead of a silently missing
/// printer.
/// </summary>
public static class WorkstationSettingKey
{
    public const string ReceiptPrinter = "printer.receipt";
    public const string LabelPrinter = "printer.label";
    public const string CashDrawerPrinter = "printer.cashdrawer";

    /// <summary>
    /// The trading currency, as an ISO 4217 code.
    /// </summary>
    /// <remarks>
    /// Not strictly a property of the machine - a pharmacy trades in one
    /// currency across all of its tills - but this is the only store a
    /// till can write to without the host, and setting it wrong is
    /// self-announcing: every figure on the screen changes symbol. The
    /// setup screen says plainly that it applies to this workstation.
    /// </remarks>
    public const string Currency = "pharmacy.currency";

    /// The pharmacy's logo, as a path to a file PharmaX keeps its own
    /// copy of, so that moving the original does not blank the shell.
    public const string LogoPath = "pharmacy.logo";
}

/// <summary>
/// Settings that belong to the pharmacy rather than to any one till:
/// the name it trades under, its registered name, its VAT number and
/// its address.
/// </summary>
/// <remarks>
/// The same shape as IWorkstationSettings but a different scope. A
/// receipt printed at the dispensary and one printed at the front till
/// must carry the same VAT number, so on a shared database every till
/// reads the one value rather than each keeping its own.
/// </remarks>
public interface IPharmacySettings
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);

    Task SetAsync(string key, string? value, CancellationToken ct = default);
}

public static class PharmacySettingKey
{
    public const string TradingName = "pharmacy.trading-name";
    public const string RegisteredName = "pharmacy.registered-name";
    public const string VatNumber = "pharmacy.vat-number";
    public const string Address = "pharmacy.address";
}
