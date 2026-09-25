namespace PharmaX.Domain.Primitives;

/// <summary>
/// One note or coin a drawer can hold, as the currency defines it.
/// </summary>
public sealed record CurrencyDenomination(string Key, decimal Value,
                                          bool IsNote);

/// <summary>
/// A currency PharmaX can trade in: what it is called, how it is
/// written, and what is in the drawer.
/// </summary>
/// <param name="Code">ISO 4217, and the code stored on every amount.</param>
/// <param name="Culture">
/// Whose number format money and quantities follow. Dates do not: those
/// are written dd/MM/yyyy everywhere on purpose, so that changing
/// currency never changes what 06/07 means on a script.
/// </param>
public sealed record CurrencyOption(string Code, string Name, string Symbol,
    string Culture, IReadOnlyList<CurrencyDenomination> Denominations)
{
    public override string ToString() => $"{Name} ({Symbol})";
}

/// <summary>
/// The currencies a site can be set to trade in.
/// </summary>
/// <remarks>
/// Data, not a hard-coded list of South African notes. The cash-up grid,
/// the currency symbol on every figure and the code stored against every
/// amount all come from the one entry chosen in system setup, so a till
/// that trades in dollars counts dollar bills rather than rand it will
/// never see.
/// </remarks>
public static class Currencies
{
    /// <summary>
    /// What a new installation trades in until someone says otherwise.
    /// </summary>
    public static CurrencyOption Default => Usd;

    public static CurrencyOption Usd { get; } = new("USD",
        "United States dollar", "$", "en-US", new CurrencyDenomination[]
        {
            new("$100", 100m, true), new("$50", 50m, true),
            new("$20", 20m, true), new("$10", 10m, true),
            new("$5", 5m, true), new("$1", 1m, true),
            new("25c", 0.25m, false), new("10c", 0.10m, false),
            new("5c", 0.05m, false), new("1c", 0.01m, false)
        });

    public static CurrencyOption Zar { get; } = new("ZAR",
        "South African rand", "R", "en-ZA", new CurrencyDenomination[]
        {
            new("R200", 200m, true), new("R100", 100m, true),
            new("R50", 50m, true), new("R20", 20m, true),
            new("R10", 10m, true),
            new("R5", 5m, false), new("R2", 2m, false),
            new("R1", 1m, false), new("50c", 0.50m, false),
            new("20c", 0.20m, false), new("10c", 0.10m, false)
        });

    public static CurrencyOption Gbp { get; } = new("GBP",
        "Pound sterling", "£", "en-GB", new CurrencyDenomination[]
        {
            new("£50", 50m, true), new("£20", 20m, true),
            new("£10", 10m, true), new("£5", 5m, true),
            new("£2", 2m, false), new("£1", 1m, false),
            new("50p", 0.50m, false), new("20p", 0.20m, false),
            new("10p", 0.10m, false), new("5p", 0.05m, false)
        });

    public static CurrencyOption Eur { get; } = new("EUR",
        "Euro", "€", "en-IE", new CurrencyDenomination[]
        {
            new("€100", 100m, true), new("€50", 50m, true),
            new("€20", 20m, true), new("€10", 10m, true),
            new("€5", 5m, true),
            new("€2", 2m, false), new("€1", 1m, false),
            new("50c", 0.50m, false), new("20c", 0.20m, false),
            new("10c", 0.10m, false), new("5c", 0.05m, false)
        });

    public static CurrencyOption Aud { get; } = new("AUD",
        "Australian dollar", "$", "en-AU", new CurrencyDenomination[]
        {
            new("$100", 100m, true), new("$50", 50m, true),
            new("$20", 20m, true), new("$10", 10m, true),
            new("$5", 5m, true),
            new("$2", 2m, false), new("$1", 1m, false),
            new("50c", 0.50m, false), new("20c", 0.20m, false),
            new("10c", 0.10m, false), new("5c", 0.05m, false)
        });

    public static CurrencyOption Cad { get; } = new("CAD",
        "Canadian dollar", "$", "en-CA", new CurrencyDenomination[]
        {
            new("$100", 100m, true), new("$50", 50m, true),
            new("$20", 20m, true), new("$10", 10m, true),
            new("$5", 5m, true),
            new("$2", 2m, false), new("$1", 1m, false),
            new("25c", 0.25m, false), new("10c", 0.10m, false),
            new("5c", 0.05m, false)
        });

    public static CurrencyOption Bwp { get; } = new("BWP",
        "Botswana pula", "P", "en-BW", new CurrencyDenomination[]
        {
            new("P200", 200m, true), new("P100", 100m, true),
            new("P50", 50m, true), new("P20", 20m, true),
            new("P10", 10m, true),
            new("P5", 5m, false), new("P2", 2m, false),
            new("P1", 1m, false), new("50t", 0.50m, false),
            new("25t", 0.25m, false), new("10t", 0.10m, false)
        });

    /// <summary>
    /// Zimbabwe Gold, Zimbabwe's currency since 8 April 2024. ISO 4217
    /// ZWG (924), written ZiG, divided into cents.
    /// </summary>
    /// <remarks>
    /// The drawer holds what is actually in circulation: the ZiG20 and
    /// ZiG10 notes and the ZiG5, ZiG2 and ZiG1 coins. The 50, 100 and 200
    /// notes were announced and never released, and no cent coins have
    /// been issued, so cash is counted to the whole ZiG - as it is for the
    /// naira and the shilling below.
    /// </remarks>
    public static CurrencyOption Zwg { get; } = new("ZWG",
        "Zimbabwe Gold", "ZiG", "en-ZW", new CurrencyDenomination[]
        {
            new("ZiG20", 20m, true), new("ZiG10", 10m, true),
            new("ZiG5", 5m, false), new("ZiG2", 2m, false),
            new("ZiG1", 1m, false)
        });

    public static CurrencyOption Ngn { get; } = new("NGN",
        "Nigerian naira", "₦", "en-NG", new CurrencyDenomination[]
        {
            new("₦1000", 1000m, true), new("₦500", 500m, true),
            new("₦200", 200m, true), new("₦100", 100m, true),
            new("₦50", 50m, true), new("₦20", 20m, true),
            new("₦10", 10m, true), new("₦5", 5m, true)
        });

    public static CurrencyOption Kes { get; } = new("KES",
        "Kenyan shilling", "KSh", "en-KE", new CurrencyDenomination[]
        {
            new("KSh1000", 1000m, true), new("KSh500", 500m, true),
            new("KSh200", 200m, true), new("KSh100", 100m, true),
            new("KSh50", 50m, true),
            new("KSh20", 20m, false), new("KSh10", 10m, false),
            new("KSh5", 5m, false), new("KSh1", 1m, false)
        });

    public static IReadOnlyList<CurrencyOption> All { get; } =
        new[] { Usd, Zar, Gbp, Eur, Aud, Cad, Bwp, Zwg, Ngn, Kes };

    /// <summary>
    /// The currency with this code, or the default.
    /// </summary>
    /// <remarks>
    /// Falls back rather than throwing. A mistyped code in a settings
    /// row must not stop a till from opening; the operator can see what
    /// it is set to in system setup and correct it.
    /// </remarks>
    public static CurrencyOption ByCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return Default;

        foreach (var currency in All)
        {
            if (string.Equals(currency.Code, code.Trim(),
                              StringComparison.OrdinalIgnoreCase))
                return currency;
        }

        return Default;
    }
}
