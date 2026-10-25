using System.ComponentModel;
using System.Drawing.Printing;
using System.Runtime.InteropServices;

namespace PharmaX.Adapters.Printing;

/// <summary>
/// Decides which Windows printer a document goes to, and whether it can
/// go anywhere at all.
/// </summary>
/// <remarks>
/// The printers used to take whatever name was saved for the till and
/// hand it to the spooler. A saved printer that had since been removed,
/// renamed or unplugged failed deep inside the driver with a message
/// about invalid settings, which was logged and forgotten; with nothing
/// saved the document went to the Windows default without checking there
/// was one. This is the one place that looks: the printer chosen for this
/// workstation if it is installed, otherwise the Windows default, and a
/// plain sentence when there is neither. Labels never go to the default.
/// </remarks>
public static class PrinterResolver
{
    /// <summary>The printers Windows has installed, by name.</summary>
    public static IReadOnlyList<string> Installed() =>
        PrinterSettings.InstalledPrinters.Cast<string>().ToList();

    /// <summary>
    /// The printer to use and, where it is not the one asked for, why.
    /// </summary>
    /// <param name="chosen">
    /// The printer saved for this workstation in Printer Setup, if any.
    /// </param>
    /// <param name="purpose">"receipt" or "label", for the messages.</param>
    /// <param name="orDefault">
    /// Whether the Windows default will do when nothing usable is chosen.
    /// Not for labels: at a till the default is the receipt printer, and
    /// labels sent there came out on the receipt roll.
    /// </param>
    public static PrinterChoice Resolve(string? chosen, string purpose,
                                        bool orDefault = true)
    {
        var installed = Installed();

        // A printer on another computer that this Windows user has not been
        // connected to - somebody else signed in to Windows here - is
        // connected now, rather than reported missing.
        if (NetworkPrinters.IsNetworkPath(chosen)
            && !installed.Contains(chosen!.Trim(), StringComparer.OrdinalIgnoreCase)
            && NetworkPrinters.Connect(chosen.Trim()) is null)
            installed = Installed();

        return Resolve(chosen, purpose, installed, WindowsDefault(),
                       PrinterStatus.ProblemWith, orDefault);
    }

    /// <summary>
    /// The decision on its own, with the machine's printers passed in, so
    /// that it can be tested without any installed.
    /// </summary>
    public static PrinterChoice Resolve(string? chosen, string purpose,
        IReadOnlyList<string> installed, string? windowsDefault,
        Func<string, string?> problemWith, bool orDefault = true)
    {
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(problemWith);

        var network = NetworkPrinters.ComputerOf(chosen?.Trim());

        if (installed.Count == 0 && network is null)
            return PrinterChoice.None(
                $"No printer is installed on this workstation, so the "
                + $"{purpose} could not be printed. Add the printer in "
                + "Windows Settings, then choose it in File > Printer "
                + "Setup.");

        string? note = null;

        if (!string.IsNullOrWhiteSpace(chosen))
        {
            var match = installed.FirstOrDefault(p =>
                string.Equals(p, chosen.Trim(), StringComparison.OrdinalIgnoreCase));

            if (match is not null)
                return Checked(match, purpose, problemWith, note: null);

            note = network is null
                ? $"The {purpose} printer chosen for this till, "
                  + $"'{chosen}', is not installed any more"
                : $"The {purpose} printer chosen for this till, "
                  + $"{NetworkPrinters.Describe(chosen.Trim())}, could not be "
                  + $"reached - check {network} is switched on";
        }

        if (!orDefault)
            return PrinterChoice.None(
                (note ?? $"No {purpose} printer is chosen for this computer")
                + $", so the {purpose} was not printed. Choose the {purpose} "
                + "printer in File > Printer Setup.");

        if (string.IsNullOrWhiteSpace(windowsDefault)
            || !installed.Contains(windowsDefault, StringComparer.OrdinalIgnoreCase))
            return PrinterChoice.None(
                (note is null ? "" : note + ", and ")
                + (note is null ? "No" : "no")
                + " default printer is set in Windows, so the "
                + $"{purpose} could not be printed. Choose a printer in "
                + "File > Printer Setup.");

        return Checked(windowsDefault, purpose, problemWith,
            note is null
                ? null
                : note + $"; it went to the Windows default, "
                  + $"'{windowsDefault}'. Choose the right printer in "
                  + "File > Printer Setup.");
    }

    private static PrinterChoice Checked(string printer, string purpose,
        Func<string, string?> problemWith, string? note)
    {
        var problem = problemWith(printer);
        var network = NetworkPrinters.ComputerOf(printer);

        return problem is null
            ? PrinterChoice.Use(printer, note)
            : PrinterChoice.Unusable(printer, network is null
                ? $"The {purpose} printer '{printer}' {problem}."
                : $"The {purpose} printer {NetworkPrinters.Describe(printer)} "
                  + $"{problem}. Check {network} is switched on and its "
                  + "printer is on.");
    }

    /// <summary>The Windows default printer, if one is set.</summary>
    public static string? WindowsDefault()
    {
        try
        {
            var settings = new PrinterSettings();
            return settings.IsValid ? settings.PrinterName : null;
        }
        catch (InvalidPrinterException)
        {
            return null;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }
}

/// <summary>The outcome of choosing a printer.</summary>
public sealed record PrinterChoice(string? Printer, string? Note,
                                   string? Problem)
{
    public bool CanPrint => Printer is not null && Problem is null;

    public static PrinterChoice Use(string printer, string? note) =>
        new(printer, note, null);

    public static PrinterChoice Unusable(string printer, string problem) =>
        new(printer, null, problem);

    public static PrinterChoice None(string problem) =>
        new(null, null, problem);
}

/// <summary>
/// Asks the Windows spooler whether a printer is offline or in error,
/// before a document is sent to it.
/// </summary>
/// <remarks>
/// Not every driver reports its state - a virtual printer never does -
/// and a printer can fail after the check, so a clear bill of health here
/// is not a guarantee. What it does catch is the printer that is plainly
/// switched off or out of paper, which is most of what goes wrong at a
/// till, and turns a job that would sit in the queue for ever into a
/// message the cashier can act on.
/// </remarks>
internal static class PrinterStatus
{
    private const int Offline = 0x00000080;
    private const int Error = 0x00000002;
    private const int PaperOut = 0x00000010;
    private const int NotAvailable = 0x00001000;
    private const int DoorOpen = 0x00400000;
    private const int PaperJam = 0x00000008;
    private const int WorkOfflineAttribute = 0x00000400;

    public static string? ProblemWith(string printer)
    {
        if (!OpenPrinter(printer, out var handle, IntPtr.Zero))
            return "cannot be reached";

        try
        {
            GetPrinter(handle, 2, IntPtr.Zero, 0, out var needed);
            if (needed <= 0) return null;

            var buffer = Marshal.AllocHGlobal(needed);

            try
            {
                if (!GetPrinter(handle, 2, buffer, needed, out _)) return null;

                var info = Marshal.PtrToStructure<PrinterInfo2>(buffer);

                if ((info.Attributes & WorkOfflineAttribute) != 0
                    || (info.Status & Offline) != 0)
                    return "is offline";

                if ((info.Status & PaperOut) != 0) return "is out of paper";
                if ((info.Status & PaperJam) != 0) return "has a paper jam";
                if ((info.Status & DoorOpen) != 0) return "has its door open";
                if ((info.Status & NotAvailable) != 0) return "is not available";
                if ((info.Status & Error) != 0) return "is reporting an error";

                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            ClosePrinter(handle);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct PrinterInfo2
    {
        public IntPtr ServerName;
        public IntPtr PrinterName;
        public IntPtr ShareName;
        public IntPtr PortName;
        public IntPtr DriverName;
        public IntPtr Comment;
        public IntPtr Location;
        public IntPtr DevMode;
        public IntPtr SepFile;
        public IntPtr PrintProcessor;
        public IntPtr Datatype;
        public IntPtr Parameters;
        public IntPtr SecurityDescriptor;
        public int Attributes;
        public int Priority;
        public int DefaultPriority;
        public int StartTime;
        public int UntilTime;
        public int Status;
        public int Jobs;
        public int AveragePpm;
    }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true,
               EntryPoint = "OpenPrinterW")]
    private static extern bool OpenPrinter(string printerName,
        out IntPtr printer, IntPtr defaults);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr printer);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true,
               EntryPoint = "GetPrinterW")]
    private static extern bool GetPrinter(IntPtr printer, int level,
        IntPtr buffer, int size, out int needed);
}
