using System.Globalization;
using PharmaX.Application.Abstractions;

namespace PharmaX.Application.Security;

/// <summary>Whether this computer's clock may be trusted with sales.</summary>
public static class ClockCheck
{
    public const int MaxMinutesOut = 2;

    /// Null when the clock is fine; otherwise what to tell the user.
    public static string? Problem(DateTime pcUtc, TimeSpan pcOffset, ServerTime server)
    {
        ArgumentNullException.ThrowIfNull(server);

        // shortcut: a server left on UTC (a Linux box, a container) says
        // nothing about the pharmacy's zone, so only the clock is compared;
        // add a site time zone setting if a pharmacy really trades on UTC.
        if (server.UtcOffset != TimeSpan.Zero && pcOffset != server.UtcOffset)
            return $"This computer's time zone (UTC{Zone(pcOffset)}) is not the "
                   + $"server's (UTC{Zone(server.UtcOffset)}). Set the time zone in "
                   + "Windows, then sign in again.";

        var minutes = Math.Abs((pcUtc - server.UtcNow).TotalMinutes);
        return minutes > MaxMinutesOut
            ? $"This computer's clock is {Math.Round(minutes):0} minutes out from the "
              + "server. Set the correct date and time in Windows, then sign in again."
            : null;
    }

    private static string Zone(TimeSpan offset) =>
        (offset < TimeSpan.Zero ? "-" : "+")
        + offset.Duration().ToString(@"hh\:mm", CultureInfo.InvariantCulture);
}
