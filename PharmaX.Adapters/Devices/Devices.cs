using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Sales;

namespace PharmaX.Adapters.Devices;

/// <summary>
/// Opens the drawer by sending the ESC/POS kick sequence to the till
/// printer the drawer is wired through, which is how it is wired on
/// almost every till.
/// </summary>
public sealed class EscPosCashDrawer : ICashDrawer
{
    // ESC p 0 25 250: pin 2, 50 ms on, 500 ms off. The standard kick.
    private static readonly byte[] KickSequence = { 0x1B, 0x70, 0x00, 0x19, 0xFA };

    private readonly ILogger<EscPosCashDrawer> _log;
    private readonly IWorkstationSettings _settings;
    private readonly Func<string, string?> _portOf;

    public EscPosCashDrawer(ILogger<EscPosCashDrawer> log,
                            IWorkstationSettings settings)
        : this(log, settings, RawPrinter.PortOf)
    {
    }

    /// <summary>
    /// Lets a test say which port a printer is on without that printer
    /// being installed on the machine running the test.
    /// </summary>
    internal EscPosCashDrawer(ILogger<EscPosCashDrawer> log,
                              IWorkstationSettings settings,
                              Func<string, string?> portOf)
    {
        _log = log;
        _settings = settings;
        _portOf = portOf;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
        => await DrawerPrinterAsync(ct).ConfigureAwait(false) is not null;

    public async Task OpenAsync(CancellationToken ct = default)
    {
        if (await DrawerPrinterAsync(ct).ConfigureAwait(false)
            is not string printer)
        {
            _log.LogInformation("No cash drawer configured; nothing to open.");
            return;
        }

        try
        {
            // Off the till's thread: opening a drawer is a write to a
            // printer port, and a port that does not answer must not
            // take the window down with it.
            await Task.Run(() => RawPrinter.Send(printer, KickSequence), ct)
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Deliberately broad: see the comment below.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // A drawer that will not open is a manual key turn, not a
            // reason to fail a sale that has already taken the money.
            _log.LogWarning(ex, "Could not open the cash drawer.");
        }
    }

    /// <summary>
    /// The printer the drawer is wired through, or null when this till
    /// has not been told which one that is.
    /// </summary>
    /// <remarks>
    /// Never the Windows default printer. The kick is five bytes of
    /// ESC/POS addressed to a device, not a document any driver can
    /// render, so guessing the target does not give a degraded drawer -
    /// it gives a wrong one. Defaulting is what made every cash sale on
    /// a PC with no till printer spool the kick to "Microsoft Print to
    /// PDF": a Save-As dialog in front of the cashier, and a zero-byte
    /// file left behind that no reader will open.
    ///
    /// The read is awaited rather than blocked on. Waiting for it from
    /// the till's own thread deadlocked the screen: the query's
    /// continuation needed that thread, and that thread was waiting for
    /// the query.
    /// </remarks>
    internal async Task<string?> DrawerPrinterAsync(
        CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            // The drawer hangs off the receipt printer on almost every
            // till, so that is the fallback - but only ever a printer
            // somebody chose for this workstation.
            var chosen =
                await SettingAsync(WorkstationSettingKey.CashDrawerPrinter, ct)
                    .ConfigureAwait(false)
                ?? await SettingAsync(WorkstationSettingKey.ReceiptPrinter, ct)
                    .ConfigureAwait(false);

            if (chosen is null) return null;

            if (RawPrinter.IsFilePort(_portOf(chosen)))
            {
                _log.LogInformation(
                    "Printer {Printer} writes to a file, so no cash "
                    + "drawer can be wired through it.", chosen);

                return null;
            }

            return chosen;
        }
#pragma warning disable CA1031 // Deliberately broad: see the remarks.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // The settings live in the local database. A till that
            // cannot read them still sells; it just opens no drawer.
            _log.LogWarning(ex,
                "Could not read which printer the cash drawer is on.");

            return null;
        }
    }

    private async Task<string?> SettingAsync(string key,
                                             CancellationToken ct)
    {
        var value = await _settings.GetAsync(key, ct).ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}

/// <summary>
/// Sends bytes straight to a printer, bypassing the driver's rendering.
/// The only way to reach a device that speaks ESC/POS rather than GDI.
/// </summary>
internal static class RawPrinter
{
    /// <summary>
    /// Ports that write to a file rather than to a device.
    /// </summary>
    /// <remarks>
    /// PORTPROMPT: is the Windows PDF and XPS writers, which stop and
    /// ask the operator where to save; nul: is "Send to OneNote", which
    /// swallows the job. Nothing with a cash drawer hanging off it is
    /// ever on one of these.
    /// </remarks>
    private static readonly string[] FilePorts =
        { "PORTPROMPT:", "FILE:", "nul:" };

    public static bool IsFilePort(string? port) =>
        port is not null
        && FilePorts.Contains(port, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The port a printer is on, or null when Windows will not say.
    /// </summary>
    public static string? PortOf(string printerName)
    {
        if (!OpenPrinter(printerName, out var printer, IntPtr.Zero))
            return null;

        try
        {
            // The documented two-call pattern: ask how much room the
            // answer needs, then ask again with that much.
            GetPrinter(printer, 2, IntPtr.Zero, 0, out var needed);

            if (needed <= 0) return null;

            var buffer = Marshal.AllocHGlobal(needed);

            try
            {
                if (!GetPrinter(printer, 2, buffer, needed, out _))
                    return null;

                var info = Marshal.PtrToStructure<PrinterInfo2>(buffer);

                return Marshal.PtrToStringUni(info.PortName);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            ClosePrinter(printer);
        }
    }

    public static void Send(string printerName, byte[] bytes)
    {
        if (!OpenPrinter(printerName, out var printer, IntPtr.Zero))
            throw new InvalidOperationException(
                $"Could not open printer '{printerName}'.");

        var unmanaged = IntPtr.Zero;

        try
        {
            var info = new DocInfo
            {
                DocName = "PharmaX device command",
                DataType = "RAW"
            };

            if (!StartDocPrinter(printer, 1, info)) return;

            try
            {
                if (!StartPagePrinter(printer)) return;

                unmanaged = Marshal.AllocCoTaskMem(bytes.Length);
                Marshal.Copy(bytes, 0, unmanaged, bytes.Length);

                WritePrinter(printer, unmanaged, bytes.Length, out _);
                EndPagePrinter(printer);
            }
            finally
            {
                EndDocPrinter(printer);
            }
        }
        finally
        {
            if (unmanaged != IntPtr.Zero) Marshal.FreeCoTaskMem(unmanaged);
            ClosePrinter(printer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class DocInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string DocName = "";
        [MarshalAs(UnmanagedType.LPWStr)] public string? OutputFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string DataType = "RAW";
    }

    /// <summary>
    /// The head of PRINTER_INFO_2, as far as the port name. The fields
    /// after it are never read, so they are not declared.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PrinterInfo2
    {
        public IntPtr ServerName;
        public IntPtr PrinterName;
        public IntPtr ShareName;
        public IntPtr PortName;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string printerName,
        out IntPtr printer, IntPtr defaults);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr printer);

    [DllImport("winspool.drv", EntryPoint = "GetPrinterW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetPrinter(IntPtr printer, int level,
        IntPtr buffer, int size, out int needed);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool StartDocPrinter(IntPtr printer, int level,
        [MarshalAs(UnmanagedType.LPStruct)] DocInfo docInfo);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr printer);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr printer);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr printer);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr printer, IntPtr bytes,
        int count, out int written);
}
