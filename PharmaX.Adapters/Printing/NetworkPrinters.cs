using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using PharmaX.Adapters.Devices;
using PharmaX.Application.Abstractions;

namespace PharmaX.Adapters.Printing;

/// <summary>
/// The printers on the pharmacy's other computers: finding them, sharing
/// this computer's, and connecting to one somebody chose.
/// </summary>
/// <remarks>
/// Windows printer sharing underneath, so a receipt printer plugged into a
/// till is printed to at the main computer as \\TILL-2\EPSON exactly as a
/// local one is, and keeps working when PharmaX is closed on the till.
/// What PharmaX adds is the finding: every computer reports its printers
/// to the shared database, so nobody has to know a share name to type.
/// </remarks>
public static class NetworkPrinters
{
    private const int EnumLocal = 0x2;
    private const int EnumName = 0x8;
    private const int SharedAttribute = 0x8;

    /// \\COMPUTER\Share - a printer reached through another computer.
    public static bool IsNetworkPath(string? printer) =>
        printer is not null && printer.StartsWith(@"\\", StringComparison.Ordinal)
        && printer.IndexOf('\\', 2) > 2;

    /// The computer in \\COMPUTER\Share, or null for a local printer.
    public static string? ComputerOf(string? printer) =>
        IsNetworkPath(printer) ? printer![2..printer.IndexOf('\\', 2)] : null;

    /// <summary>
    /// "EPSON on TILL-2" for \\TILL-2\EPSON; a local printer as it is.
    /// </summary>
    public static string Describe(string printer) =>
        ComputerOf(printer) is { } computer
            ? $"{printer[(computer.Length + 3)..]} on {computer}"
            : printer;

    /// <summary>
    /// Printers that write to a file, a fax or an app rather than to paper:
    /// the PDF and XPS writers, Fax, both OneNotes. Not worth offering to
    /// another till.
    /// </summary>
    /// <remarks>
    /// Mostly told by the port, but a Store app's printer - OneNote for
    /// Windows 10 - is on a port named after the app's package, so its
    /// driver is the tell.
    /// </remarks>
    public static bool IsVirtual(string? port, string? driver = null) =>
        RawPrinter.IsFilePort(port)
        || string.Equals(port, "SHRFAX:", StringComparison.OrdinalIgnoreCase)
        || string.Equals(driver, "Microsoft Software Printer Driver",
                         StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A name Windows will share a printer under: its own, less the
    /// characters a share name cannot hold.
    /// </summary>
    public static string ShareNameFor(string printer)
    {
        ArgumentNullException.ThrowIfNull(printer);

        const string refused = "\\/:*?\"<>|,;=+[]";
        var name = new string(printer.Select(c => refused.Contains(c) || c < ' '
            ? '-' : c).ToArray()).Trim();

        return name.Length > 80 ? name[..80].Trim() : name;
    }

    /// <summary>
    /// The printers plugged into this computer, shared or not. Not the
    /// connections to other computers' printers: those are theirs to report.
    /// </summary>
    public static IReadOnlyList<NetworkPrinter> OnThisComputer()
    {
        var computer = Environment.MachineName;

        return Enumerate(EnumLocal, null)
            .Where(p => !IsVirtual(p.Port, p.Driver) && !IsNetworkPath(p.Name))
            .Select(p => new NetworkPrinter(computer, p.Name,
                p.Shared ? p.Share : null))
            .ToList();
    }

    /// <summary>
    /// The printers another computer shares, asked of that computer itself:
    /// for one PharmaX is not running on - the office PC with the laser.
    /// </summary>
    /// <exception cref="Win32Exception">The computer could not be reached.</exception>
    public static IReadOnlyList<NetworkPrinter> SharedOn(string computer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(computer);

        var name = computer.Trim().TrimStart('\\');

        return Enumerate(EnumName, @"\\" + name)
            .Where(p => p.Shared && !string.IsNullOrWhiteSpace(p.Share))
            .Select(p => new NetworkPrinter(name.ToUpperInvariant(),
                // Asked of a server, Windows gives "\\SERVER\Printer".
                p.Name[(p.Name.LastIndexOf('\\') + 1)..], p.Share))
            .ToList();
    }

    /// <summary>
    /// Every computer's printers in the shared database, this one's as they
    /// are now.
    /// </summary>
    public static async Task PublishAsync(IPrinterDirectory directory,
                                          CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(directory);

        var mine = await Task.Run(OnThisComputer, ct).ConfigureAwait(false);
        await directory.PublishAsync(Environment.MachineName, mine, ct)
                       .ConfigureAwait(false);
    }

    /// <summary>
    /// Connects this Windows user to a printer on another computer, so it
    /// is installed here like any other. Null when done; otherwise why not.
    /// </summary>
    /// <remarks>
    /// Windows fetches the printer's driver from the other computer the
    /// first time. Since 2021 it lets only an administrator do that, so on
    /// a fresh computer the first connection may need one.
    /// </remarks>
    public static string? Connect(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (AddPrinterConnection(path)) return null;

        var error = Marshal.GetLastWin32Error();
        var reason = new Win32Exception(error).Message;

        // Access denied, or ERROR_PRINTER_DRIVER_DOWNLOAD_NEEDED.
        return error is 5 or 3019
            ?$"Windows would not install the driver for {Describe(path)} "
              + $"({reason}). An administrator has to connect it once on this "
              + "computer: sign in to Windows as one and choose it again here, "
              + $"or add {path} in Windows Settings > Printers & scanners."
            : $"Windows could not connect to {Describe(path)}: {reason}";
    }

    /// <summary>
    /// Shares printers on this computer with the others, and lets the
    /// others through the firewall to them. One Windows administrator
    /// prompt for the lot. Null when done; otherwise why not.
    /// </summary>
    /// <param name="log">Where to write what Windows said, if it refused.</param>
    public static string? Share(IReadOnlyCollection<string> printers, string log)
    {
        ArgumentNullException.ThrowIfNull(printers);
        if (printers.Count == 0) return null;

        var script = new StringBuilder("$ErrorActionPreference = 'Stop'\ntry {\n");

        foreach (var printer in printers)
            script.Append($"  Set-Printer -Name {Quote(printer)} -Shared $true "
                          + $"-ShareName {Quote(ShareNameFor(printer))}\n");

        // File and Printer Sharing, by the group name Windows uses in every
        // language: the one on screen is translated.
        script.Append("  Enable-NetFirewallRule -Group '@FirewallAPI.dll,-28502'\n")
              .Append("} catch {\n")
              .Append($"  $_ | Out-File -FilePath {Quote(log)} -Append\n")
              .Append("  exit 1\n}\n");

        var start = new ProcessStartInfo("powershell.exe",
            "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand "
            + Convert.ToBase64String(Encoding.Unicode.GetBytes(script.ToString())))
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };

        try
        {
            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("PowerShell did not start.");

            process.WaitForExit();

            return process.ExitCode == 0
                ? null
                : $"Windows did not share the printers. What it said is in {log}.";
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return "Nothing was shared: the Windows administrator prompt was "
                   + "cancelled.";
        }
    }

    /// <summary>
    /// A PowerShell literal that is only ever text. The typographic quotes
    /// count as quotes to PowerShell too, so they are doubled as well.
    /// </summary>
    internal static string Quote(string text)
    {
        var quoted = new StringBuilder("'");

        foreach (var c in text)
        {
            if (c is '\'' or '\u2018' or '\u2019' or '\u201A' or '\u201B')
                quoted.Append(c);

            quoted.Append(c);
        }

        return quoted.Append('\'').ToString();
    }

    private sealed record Found(string Name, string? Share, string? Port,
                                string? Driver, bool Shared);

    private static List<Found> Enumerate(int flags, string? server)
    {
        EnumPrinters(flags, server, 2, IntPtr.Zero, 0, out var needed, out _);

        if (needed <= 0)
        {
            var error = Marshal.GetLastWin32Error();

            // 122 is "the buffer is too small" - the expected answer to the
            // first call - and anything else from a server is that it could
            // not be reached.
            if (server is not null && error is not (0 or 122))
                throw new Win32Exception(error);

            return new List<Found>();
        }

        var buffer = Marshal.AllocHGlobal(needed);

        try
        {
            if (!EnumPrinters(flags, server, 2, buffer, needed, out _,
                              out var count))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var size = Marshal.SizeOf<PrinterStatus.PrinterInfo2>();
            var found = new List<Found>(count);

            for (var i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<PrinterStatus.PrinterInfo2>(
                    buffer + i * size);

                var name = Marshal.PtrToStringUni(info.PrinterName);
                if (string.IsNullOrWhiteSpace(name)) continue;

                found.Add(new Found(name, Marshal.PtrToStringUni(info.ShareName),
                    Marshal.PtrToStringUni(info.PortName),
                    Marshal.PtrToStringUni(info.DriverName),
                    (info.Attributes & SharedAttribute) != 0));
            }

            return found;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("winspool.drv", EntryPoint = "EnumPrintersW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumPrinters(int flags, string? name, int level,
        IntPtr buffer, int size, out int needed, out int returned);

    [DllImport("winspool.drv", EntryPoint = "AddPrinterConnectionW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool AddPrinterConnection(string name);
}
