namespace PharmaX.Application.Security;

/// <summary>
/// BethRapha trades on Harare, Pretoria time. That zone has no daylight saving.
/// </summary>
public static class PharmacyTime
{
    public const string ZoneId = "South Africa Standard Time";

    public static readonly TimeSpan Offset = TimeSpan.FromHours(2);

    /// <summary>Sales are taken from 07:00 to 21:00. After midnight is too far from the shop day.</summary>
    public static readonly TimeOnly Opens = new(7, 0);

    public static readonly TimeOnly Closes = new(21, 0);

    public const string Closed =
        "A sale or a till can be opened from 07:00 to 21:00. A time after midnight is too far outside the shop day.";

    /// <summary>07:00 on that calendar day, in UTC. shortcut: no daylight saving, so this is always 05:00 UTC.</summary>
    public static DateTime StartUtc(DateOnly day) =>
        DateTime.SpecifyKind(day.ToDateTime(Opens) - Offset, DateTimeKind.Utc);

    /// <summary>21:00 on that calendar day, in UTC. shortcut: no daylight saving, so this is always 19:00 UTC.</summary>
    public static DateTime EndUtc(DateOnly day) =>
        DateTime.SpecifyKind(day.ToDateTime(Closes) - Offset, DateTimeKind.Utc);

    public static bool IsOpen(DateTime utc)
    {
        var local = DateTime.SpecifyKind(utc, DateTimeKind.Utc) + Offset;
        var time = TimeOnly.FromDateTime(local);
        return time >= Opens && time < Closes;
    }

    /// <summary>Why this moment cannot take a sale. Null during trading hours when the clock has not been moved.</summary>
    public static string? Block(DateTime utc, string? clockChanged) =>
        clockChanged ?? (IsOpen(utc) ? null : Closed);
}
