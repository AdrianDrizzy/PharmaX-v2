using System.Globalization;

namespace PharmaX.Desktop.Features.Inventory;

/// <summary>
/// An expiry date as somebody types it into a batch cell. One parser for
/// the product list and the product editor, so a date one screen takes
/// the other does not refuse.
/// </summary>
internal static class ExpiryText
{
    private static readonly string[] Formats =
        ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd/MM/yy"];

    public static bool TryParse(string? text, out DateOnly expiry)
    {
        var culture = CultureInfo.CurrentCulture;
        var trimmed = (text ?? "").Trim();

        if (DateOnly.TryParseExact(trimmed, Formats, culture,
                DateTimeStyles.None, out expiry))
            return true;

        return DateOnly.TryParse(trimmed, culture, out expiry);
    }

    /// <summary>
    /// The expiry that many days from today, or false when there is no
    /// such date.
    /// </summary>
    public static bool TryFromDaysLeft(int days, out DateOnly expiry)
    {
        try
        {
            expiry = DateOnly.FromDateTime(DateTime.Today).AddDays(days);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            expiry = default;
            return false;
        }
    }

    /// The way a batch cell writes an expiry, which TryParse reads back.
    public static string Show(DateOnly expiry) =>
        expiry.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture);

    public static int DaysLeft(DateOnly expiry) =>
        expiry.DayNumber - DateOnly.FromDateTime(DateTime.Today).DayNumber;
}
