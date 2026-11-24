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

        // The pharmacy is Harare, Pretoria, whatever zone the database server
        // was installed in. A Linux host left on UTC must not pull the tills
        // onto UTC, and nobody gets to pick another zone.
        if (pcOffset != PharmacyTime.Offset)
            return $"This computer's time zone (UTC{Zone(pcOffset)}) is not "
                   + "Harare, Pretoria (UTC+02:00). PharmaX sets that time zone, "
                   + "and sign-in stays closed until it has.";

        var minutes = Math.Abs((pcUtc - server.UtcNow).TotalMinutes);
        return minutes > MaxMinutesOut
            ? $"This computer's clock is {Math.Round(minutes):0} minutes out from the "
              + "server. Sign-in stays closed until Windows time matches the server. "
              + "The time cannot be set to anything else."
            : null;
    }

    /// <summary>
    /// True when the PC clock moved by more than <see cref="MaxMinutesOut"/>
    /// from the time PharmaX started. Elapsed is measured by a stopwatch,
    /// which a change of the Windows clock does not move.
    /// </summary>
    public static bool Moved(DateTime startedUtc, TimeSpan elapsed, DateTime nowUtc) =>
        (nowUtc - (startedUtc + elapsed)).Duration() > TimeSpan.FromMinutes(MaxMinutesOut);

    public const string MovedMessage =
        "This computer's clock was changed while PharmaX was open. "
        + "No sale can be taken. Close PharmaX and leave the clock on Harare, Pretoria time.";

    private static string Zone(TimeSpan offset) =>
        (offset < TimeSpan.Zero ? "-" : "+")
        + offset.Duration().ToString(@"hh\:mm", CultureInfo.InvariantCulture);
}
