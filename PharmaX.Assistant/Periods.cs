using System.Globalization;

namespace PharmaX.Assistant;

/// <summary>
/// Turns the period the model names into dates.
/// </summary>
/// <remarks>
/// The model is never asked to do date arithmetic, which a small one gets
/// wrong: it says "last_month" and this works out which days that is. It
/// writes dates itself only for a named month or explicit days, and only
/// when the request contains a month or a number to take them from.
/// </remarks>
public static class Periods
{
    public static readonly IReadOnlyList<string> Named = new[]
    {
        "today", "yesterday", "this_week", "last_week", "this_month", "last_month",
        "this_year", "last_year", "last_7_days", "last_30_days", "none",
    };

    public const string Custom = "custom";

    /// <summary>
    /// The days a period covers, or null for "none" or a custom period
    /// whose dates cannot be read. Weeks start on Monday.
    /// </summary>
    public static (DateOnly From, DateOnly To)? Resolve(string period, string? from,
        string? to, DateOnly today)
    {
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var month = new DateOnly(today.Year, today.Month, 1);

        return period switch
        {
            "today" => (today, today),
            "yesterday" => (today.AddDays(-1), today.AddDays(-1)),
            "this_week" => (monday, today),
            "last_week" => (monday.AddDays(-7), monday.AddDays(-1)),
            "this_month" => (month, today),
            "last_month" => (month.AddMonths(-1), month.AddDays(-1)),
            "this_year" => (new DateOnly(today.Year, 1, 1), today),
            "last_year" => (new DateOnly(today.Year - 1, 1, 1), new DateOnly(today.Year - 1, 12, 31)),
            "last_7_days" => (today.AddDays(-6), today),
            "last_30_days" => (today.AddDays(-29), today),
            Custom when TryDate(from, out var start) && TryDate(to, out var end) =>
                start <= end ? (start, end) : (end, start),
            _ => null
        };
    }

    private static bool TryDate(string? text, out DateOnly date) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                               DateTimeStyles.None, out date);
}
