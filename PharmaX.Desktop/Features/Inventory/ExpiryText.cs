using System.Globalization;

namespace PharmaX.Desktop.Features.Inventory;

/// <summary>
/// An expiry date as somebody types it into a batch cell. One parser for
/// the product list, the product editor and goods receipt, so a date one
/// screen takes another does not refuse.
/// </summary>
/// <remarks>
/// Day first whatever the culture: a till trading in dollars runs en-US,
/// which read 31/12/2029 as month 31 and refused it, so goods receipt
/// said "needs an expiry date" under a date that was there. Read against
/// the invariant culture so the slash is a slash on every machine.
/// </remarks>
internal static class ExpiryText
{
    private static readonly string[] Formats =
        ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd/MM/yy", "d/M/yy"];

    /// As a pack prints it: the month, meaning its last day.
    private static readonly string[] MonthFormats = ["MM/yyyy", "M/yyyy", "MM/yy"];

    public static bool TryParse(string? text, out DateOnly expiry)
    {
        var trimmed = (text ?? "").Trim();
        var invariant = CultureInfo.InvariantCulture;

        if (DateOnly.TryParseExact(trimmed, Formats, invariant,
                DateTimeStyles.None, out expiry))
            return true;

        if (DateOnly.TryParseExact(trimmed, MonthFormats, invariant,
                DateTimeStyles.None, out var month))
        {
            expiry = new DateOnly(month.Year, month.Month,
                DateTime.DaysInMonth(month.Year, month.Month));
            return true;
        }

        return DateOnly.TryParse(trimmed, CultureInfo.CurrentCulture, out expiry);
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
