using System.Globalization;

namespace PharmaX.Application.Licensing;

public enum LicenceStatus { Active, Expiring, Grace, ReadOnly, ClockWrong }

public sealed record LicenceSnapshot(LicenceStatus Status, DateOnly? ValidUntil,
                                     MembershipPlan? Plan, bool IsTrial)
{
    public bool IsReadOnly =>
        Status is LicenceStatus.ReadOnly or LicenceStatus.ClockWrong;
}

/// <summary>
/// Where a membership stands on a given day: warned for the last seven
/// days, then seven days' grace with everything still working, then
/// read-only. A pharmacy is never stopped from serving a patient the day a
/// payment is late.
/// </summary>
public static class LicenceState
{
    public const int WarningDays = 7;
    public const int GraceDays = 7;

    // A day's slack: time zones and a clock a little off are not tampering.
    private const int ClockSlackDays = 1;

    public static LicenceStatus Evaluate(DateOnly? validUntil, DateOnly today,
                                         DateOnly? lastSeen)
    {
        if (lastSeen is { } seen && today.DayNumber < seen.DayNumber - ClockSlackDays)
            return LicenceStatus.ClockWrong;

        if (validUntil is not { } end) return LicenceStatus.ReadOnly;

        var left = end.DayNumber - today.DayNumber;
        return left > WarningDays ? LicenceStatus.Active
             : left >= 0 ? LicenceStatus.Expiring
             : -left <= GraceDays ? LicenceStatus.Grace
             : LicenceStatus.ReadOnly;
    }

    public static string? BannerText(LicenceSnapshot s)
    {
        const string Where = "Enter a new code in Administration > Membership.";
        var what = s.IsTrial ? "Trial" : "Membership";

        return s.Status switch
        {
            LicenceStatus.Expiring =>
                $"{what} ends on {Day(s.ValidUntil)}. {Where}",
            LicenceStatus.Grace =>
                $"{what} ended on {Day(s.ValidUntil)}. PharmaX becomes read-only on "
                + $"{Day(s.ValidUntil?.AddDays(GraceDays + 1))}. {Where}",
            LicenceStatus.ReadOnly =>
                "PharmaX is read-only: no current membership. An administrator can "
                + "enter a code in Administration > Membership.",
            LicenceStatus.ClockWrong =>
                "PharmaX is read-only: this computer's date looks wrong. Correct the "
                + "date and time, then sign in again.",
            _ => null
        };
    }

    /// How long a membership runs, in words: "for life" or "until 18 Feb 2027".
    public static string Runs(MembershipPlan? plan, DateOnly? until) =>
        plan == MembershipPlan.Lifetime ? "for life" : $"until {Day(until)}";

    public static string Day(DateOnly? day) =>
        day?.ToString("d MMM yyyy", CultureInfo.InvariantCulture) ?? "";
}
