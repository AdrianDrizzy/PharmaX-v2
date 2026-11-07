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

    /// The A4 printer for full-page documents - the medical aid claim
    /// form - which a receipt roll cannot print.
    public const string DocumentPrinter = "printer.document";

    /// "off" where this computer prints no labels when a script is
    /// issued; anything else, or nothing, prints them. Reprint still
    /// prints (DispensePresenter).
    public const string PrintLabels = "label.print";

    /// "off" where this computer prints no trailer label after a
    /// script's own labels; anything else, or nothing, prints one
    /// (TrailerLabels.WantedAsync).
    public const string TrailerLabel = "label.trailer";

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

    /// The colours of the windows: "logo", or nothing, to take them from
    /// the logo - the default - or the id of one of the named palettes.
    public const string Palette = "theme.palette";

    /// Light, dark or one of the other modes, by id: chosen from the
    /// shell's Theme menu, per till. Nothing means light.
    public const string Theme = "theme.mode";

    /// How large the windows are drawn, in per cent: chosen from the
    /// shell's View menu, per till. Nothing means 100.
    public const string Zoom = "view.zoom";

    /// The shell's window: "max" or nothing for the whole screen, or a
    /// size in pixels as "1366x768". Chosen from the View menu, per till.
    public const string WindowSize = "view.window";
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
    public const string Telephone = "pharmacy.telephone";
    public const string WhatsApp = "pharmacy.whatsapp";

    /// The logo image itself, base64, so every till can draw it: a file
    /// path would mean nothing on another machine. Blank once removed.
    public const string LogoImage = "pharmacy.logo-image";

    /// The windows' colour palette, the same for every till and user.
    public const string Palette = "pharmacy.palette";

    /// Percent on cost for an item with no price yet (Pricing).
    public const string Markup = "pricing.markup";

    /// The step every worked-out price is rounded up to, e.g. 0.05.
    public const string Rounding = "pricing.rounding";

    /// The step a cash patient's dispensing charge is rounded up to, e.g.
    /// 1.00; a dollar where it was never set (DispensingCharge).
    public const string DispensingRounding = "pricing.dispensing-rounding";

    /// Pre-pack stickers already printed, newest first, so any till can
    /// print an old one again.
    public const string PrepackLabels = "labels.prepacks";

    /// Template labels an administrator has printed, newest first.
    public const string TemplateLabels = "labels.templates";

    /// <summary>
    /// The dispensary's label printer, as \\COMPUTER\Share, for every
    /// computer whose own label printer is not chosen or not there.
    /// </summary>
    /// <remarks>
    /// A label printer chosen on the computer it is plugged into is saved
    /// there under its bare name, which means nothing on the next
    /// dispensary computer: that one said "no longer installed" and
    /// printed no labels at all. One network path every computer can
    /// reach puts every label on the same printer, through the same driver.
    /// </remarks>
    public const string LabelPrinter = "printer.dispensary-label";
}
