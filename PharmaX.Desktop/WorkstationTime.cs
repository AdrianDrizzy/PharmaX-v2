using System.ComponentModel;
using System.Diagnostics;
using PharmaX.Application.Security;

namespace PharmaX.Desktop;

/// <summary>
/// Puts this PC on Harare, Pretoria time before anyone can sign in.
/// A person cannot choose another zone: the next start sets it back,
/// and sign-in stays closed until that has happened.
/// </summary>
internal static class WorkstationTime
{
    public static string? Ensure()
    {
        TimeZoneInfo.ClearCachedData();
        if (IsHarare()) return null;

        TrySet(elevated: false);
        TimeZoneInfo.ClearCachedData();
        if (IsHarare()) return null;

        TrySet(elevated: true);
        TimeZoneInfo.ClearCachedData();
        if (IsHarare()) return null;

        return "This computer is not on Harare, Pretoria time (UTC+02:00), "
               + "and PharmaX could not change it. An administrator has to allow "
               + "that change. Sign-in stays closed until the time zone is Harare, Pretoria.";
    }

    private static bool IsHarare() =>
        string.Equals(TimeZoneInfo.Local.Id, PharmacyTime.ZoneId, StringComparison.Ordinal);

    private static void TrySet(bool elevated)
    {
        try
        {
            var start = new ProcessStartInfo("tzutil.exe",
                "/s \"" + PharmacyTime.ZoneId + "\"")
            {
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = elevated
            };

            if (elevated)
                start.Verb = "runas";
            else
                start.CreateNoWindow = true;

            using var process = Process.Start(start);
            process?.WaitForExit(15000);
        }
        catch (Win32Exception)
        {
            // The account cannot change the zone, or the prompt was declined.
        }
        catch (InvalidOperationException)
        {
            // tzutil did not start. The sign-in check reports that.
        }
    }
}
