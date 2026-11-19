using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Licensing;

namespace PharmaX.Desktop.Setup;

/// <summary>
/// This computer as membership sees it: whether it is the main computer,
/// and the Pharmacy ID that is this computer's alone.
/// </summary>
internal static class ThisComputer
{
    /// The server, or a PC on its own; a till reaches someone else's database.
    public static LicenceMachine Licence(PharmaXOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var main = options.Workstation.IsHost
                   || string.IsNullOrWhiteSpace(options.Database.SharedConnection);
        return new LicenceMachine(main, PharmacyId(WindowsId()));
    }

    /// <summary>
    /// The Pharmacy ID from Windows' own id for this installation, hashed so
    /// the id itself never leaves the PC. The same PC always gets the same
    /// one; any other PC - or this one after Windows is reinstalled - another.
    /// </summary>
    internal static Guid PharmacyId(string windowsId) => new(
        SHA256.HashData(Encoding.UTF8.GetBytes("PharmaX licence|" + windowsId)).AsSpan(0, 16));

    private static string WindowsId()
    {
        // The 64-bit view: a 32-bit process would otherwise read an empty copy.
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        return key?.GetValue("MachineGuid") as string ?? Environment.MachineName;
    }
}
