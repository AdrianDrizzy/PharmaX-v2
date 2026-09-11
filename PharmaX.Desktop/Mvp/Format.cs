using System.Globalization;
using PharmaX.Domain.Primitives;

namespace PharmaX.Desktop.Mvp;

/// <summary>
/// How PharmaX writes money, quantities and dates on screen. One place,
/// so a figure reads the same on the till as it does on a report.
/// </summary>
public static class Format
{
    /// The currency the pharmacy trades in.
    public static CurrencyOption Currency { get; private set; } =
        Currencies.Default;

    /// <summary>
    /// The culture every figure here formats against, taken from the
    /// chosen currency and set once at start-up.
    /// </summary>
    /// <remarks>
    /// Explicit rather than CurrentCulture, so a background thread
    /// cannot format a total differently from the thread that painted
    /// the label next to it. Dates below do not use its date patterns:
    /// those are pinned to dd/MM/yyyy on purpose, so that choosing a
    /// different currency never changes what 06/07 means on a script.
    /// </remarks>
    public static CultureInfo Culture { get; private set; } =
        Build(Currencies.Default);

    public static void UseCurrency(CurrencyOption currency)
    {
        ArgumentNullException.ThrowIfNull(currency);

        Currency = currency;
        Culture = Build(currency);
    }

    public static void UseCurrency(string? code) =>
        UseCurrency(Currencies.ByCode(code));

    /// <summary>
    /// The currency's culture, with its symbol forced to the one in the
    /// catalogue.
    /// </summary>
    /// <remarks>
    /// Forced because the symbol is the whole point of the setting and
    /// it must not depend on which ICU version is on the machine. If the
    /// culture itself is missing, the invariant one still carries the
    /// right symbol, so the till shows dollars rather than a currency
    /// nobody chose.
    /// </remarks>
    private static CultureInfo Build(CurrencyOption currency)
    {
        CultureInfo culture;

        try
        {
            culture = (CultureInfo)CultureInfo
                .GetCultureInfo(currency.Culture).Clone();
        }
        catch (CultureNotFoundException)
        {
            culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        }

        culture.NumberFormat.CurrencySymbol = currency.Symbol;
        return culture;
    }

    /// The currency symbol and the amount: what a customer is shown.
    public static string Money(decimal amount) =>
        amount.ToString("C2", Culture);

    /// Just the amount, for a column of figures where the symbol on
    /// every row would only get in the way.
    public static string Amount(decimal amount) =>
        amount.ToString("N2", Culture);

    public static string Amount(decimal? amount) =>
        amount is decimal value ? Amount(value) : "";

    /// <summary>
    /// A quantity as a person reads it: three, not 3.000. Pharmacy
    /// quantities are whole far more often than not, and a column of
    /// trailing zeros is harder to scan.
    /// </summary>
    public static string Quantity(decimal quantity) =>
        quantity.ToString("0.###", Culture);

    public static string Quantity(decimal? quantity) =>
        quantity is decimal value ? Quantity(value) : "";

    public static string Date(DateOnly date) =>
        date.ToString("dd/MM/yyyy", Culture);

    public static string Date(DateTime moment) =>
        moment.ToString("dd/MM/yyyy", Culture);

    public static string DateTimeShort(DateTime moment) =>
        moment.ToString("dd/MM/yyyy HH:mm", Culture);

    /// Today, written out. The shell says it once so nobody has to
    /// guess what date a receipt printed at 00:20 will carry.
    public static string Today() =>
        DateTime.Now.ToString("dddd d MMMM yyyy", Culture);

    /// The till clock: day, date and the time to the minute.
    public static string Clock() =>
        DateTime.Now.ToString("dddd d MMMM   HH:mm", Culture);

    /// Month and year, which is how an expiry is printed on a pack.
    public static string Expiry(DateOnly? expiry) =>
        expiry is DateOnly value ? value.ToString("MM/yyyy", Culture) : "";
}
