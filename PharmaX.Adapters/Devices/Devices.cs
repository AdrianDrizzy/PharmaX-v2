using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PharmaX.Application.Sales;

namespace PharmaX.Adapters.Devices;

/// <summary>
/// Opens the drawer by sending the ESC/POS kick sequence to the receipt
/// printer, which is how the drawer is wired on almost every till.
/// </summary>
public sealed class EscPosCashDrawer : ICashDrawer
{
    // ESC p 0 25 250: pin 2, 50 ms on, 500 ms off. The standard kick.
    private static readonly byte[] KickSequence = { 0x1B, 0x70, 0x00, 0x19, 0xFA };

    private readonly ILogger<EscPosCashDrawer> _log;
    private readonly string _printerName;

    public EscPosCashDrawer(ILogger<EscPosCashDrawer> log,
                            string printerName = "")
    {
        _log = log;
        _printerName = string.IsNullOrWhiteSpace(printerName)
            ? new System.Drawing.Printing.PrinterSettings().PrinterName
            : printerName;
    }

    public bool IsAvailable =>
        !string.IsNullOrWhiteSpace(_printerName)
        && OperatingSystem.IsWindows();

    public void Open()
    {
        if (!IsAvailable)
        {
            _log.LogInformation("No cash drawer configured; nothing to open.");
            return;
        }

        try
        {
            RawPrinter.Send(_printerName, KickSequence);
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
}

/// <summary>
/// The always-available fallback. Before an integrated terminal is
/// commissioned, and whenever one fails, the operator swipes on the
/// standalone terminal and types the authorisation code off the slip.
/// </summary>
public sealed class ManualCardTerminal : ICardTerminal
{
    public string Name => "Manual card terminal";
    public bool IsIntegrated => false;

    /// <summary>
    /// Always approves, because the approval already happened on the
    /// terminal itself. The reference is captured by the payment dialog
    /// and passed to the payment, so the slip and the sale can be
    /// reconciled afterwards.
    /// </summary>
    public Task<CardAuthorisationDto> AuthoriseAsync(decimal amount,
        CancellationToken ct = default) =>
        Task.FromResult(new CardAuthorisationDto(
            Approved: true,
            Reference: null,
            Message: "Capture the authorisation code from the terminal slip."));
}

/// <summary>
/// Sends bytes straight to a printer, bypassing the driver's rendering.
/// The only way to reach a device that speaks ESC/POS rather than GDI.
/// </summary>
internal static class RawPrinter
{
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

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string printerName,
        out IntPtr printer, IntPtr defaults);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr printer);

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
