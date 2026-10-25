using PharmaX.Application.Sales.Dtos;

namespace PharmaX.Application.Sales;

public enum PrintOutcome { Printed, Failed, NoPrinter }

/// <summary>
/// What happened when something was sent to a printer, in words the
/// operator can act on.
/// </summary>
/// <remarks>
/// The printers used to answer with a bare outcome, or - for labels - not
/// at all, and the screens filled the gap with a confident message of
/// their own: "The Z-read has printed" when nothing had (DEF-0018), a
/// label that "printed" to a printer that had been unplugged. Every print
/// path now returns one of these, and the screen reports what it says.
/// </remarks>
/// <param name="PrinterName">The printer it went to, where there was one.</param>
/// <param name="Problem">
/// Why it did not print, or - where it printed somewhere other than the
/// printer chosen for this till - a note saying where it went.
/// </param>
public sealed record PrintResult(PrintOutcome Outcome, string? PrinterName,
                                 string? Problem)
{
    public bool Printed => Outcome == PrintOutcome.Printed;

    public static PrintResult Ok(string printerName, string? note = null) =>
        new(PrintOutcome.Printed, printerName, note);

    public static PrintResult NoPrinter(string problem) =>
        new(PrintOutcome.NoPrinter, null, problem);

    public static PrintResult Failed(string? printerName, string problem) =>
        new(PrintOutcome.Failed, printerName, problem);

    /// <summary>
    /// A bare outcome, for callers that only ever had one.
    /// </summary>
    public static implicit operator PrintResult(PrintOutcome outcome) =>
        FromOutcome(outcome);

    public static PrintResult FromOutcome(PrintOutcome outcome) =>
        outcome switch
        {
            PrintOutcome.Printed => new(outcome, null, null),
            PrintOutcome.NoPrinter => new(outcome, null,
                "No printer is installed on this workstation."),
            _ => new(outcome, null, "The printer reported a fault.")
        };

    /// One sentence for a message box or a status line.
    public string Describe(string what) => Outcome switch
    {
        PrintOutcome.Printed when PrinterName is null => $"{what} printed.",
        PrintOutcome.Printed => Problem is null
            ? $"{what} printed on {PrinterName}."
            : $"{what} printed on {PrinterName}. {Problem}",
        _ => $"{what} did not print. {Problem}".TrimEnd()
    };
}

/// <summary>
/// A document that is lines of text rather than a laid-out form: an X-read
/// or Z-read, a quotation, a goods received note, a purchase order.
/// </summary>
/// <param name="Title">Printed large at the top, and the spooler job name.</param>
/// <param name="Lines">
/// Monospaced, at most 42 characters, so the same document suits the till
/// roll and an A4 printer.
/// </param>
/// <param name="Columns">
/// How wide the lines are laid out. Thirty-two prints as the receipt does,
/// in its larger type under its centred header (the cash-up slip).
/// </param>
public sealed record PrintableDocument(string Title,
                                       IReadOnlyList<string> Lines,
                                       int Columns = SlipLayout.Width);

/// <summary>
/// Prints a PrintableDocument on this workstation's receipt printer, under
/// the pharmacy's fiscal header.
/// </summary>
public interface IDocumentPrinter
{
    Task<PrintResult> PrintAsync(PrintableDocument document,
                                 ReceiptHeaderDto header,
                                 CancellationToken ct = default);
}

/// <summary>
/// Everything the fiscal particulars on a receipt need (specification
/// Section 6.10). A plain record, so the printer adapter needs no
/// reference to the domain.
/// </summary>
/// <param name="LogoPath">
/// The pharmacy's logo, printed above the trading name. A path rather
/// than an image so that the record stays a plain data carrier and the
/// adapter, which already owns the drawing surface, does the loading.
/// </param>
/// <param name="IsCopy">
/// True where the document has already been handed over once and this
/// is it printed again, which the paper has to say (ADM-DEF-08).
/// </param>
/// <param name="TillNumber">
/// This till's number on its own, as the receipt prints it. Falls back to
/// <paramref name="BranchAndTill"/> where it is not given.
/// </param>
/// <param name="CurrencyCode">
/// The ISO code the pharmacy trades in, which the receipt writes beside
/// the VAT, the total and each tender - "TOTAL-USD", "Cash USD".
/// </param>
/// <param name="Telephone">
/// The number the pharmacy answers, under its address on the slip.
/// </param>
/// <param name="WhatsApp">
/// The number it takes messages on, where that is a different one.
/// </param>
/// <param name="Template">
/// Which parts of the slip to print, as the administrator set them. Null
/// where the caller did not say: the printer then reads the pharmacy's.
/// </param>
public sealed record ReceiptHeaderDto(string TradingName,
    string RegisteredName, string Address, string VatNumber,
    string LicenceNumber, string BranchAndTill, string OperatorName,
    DateTime PrintedAtLocal, string? LogoPath = null, bool IsCopy = false,
    string? TillNumber = null, string? CurrencyCode = null,
    string? Telephone = null, string? WhatsApp = null,
    ReceiptTemplate? Template = null);

/// <summary>
/// Prints a till receipt. Returns an outcome rather than throwing,
/// because a printer fault must degrade rather than stop a sale
/// (POS-014): the sale is already committed by the time this is called.
/// </summary>
public interface IReceiptPrinter
{
    /// <remarks>
    /// Asynchronous because printing reads the till's chosen printer out
    /// of the database and then hands a document to the spooler, and
    /// both of those were being waited on from the user-interface
    /// thread. The wait on the database was a deadlock: the query's
    /// continuation needed the very thread that was blocked waiting for
    /// it, so the till froze with the hourglass up and never came back.
    /// </remarks>
    Task<PrintResult> PrintAsync(SaleDto sale, ReceiptHeaderDto header,
                                 CancellationToken ct = default);

    bool IsAvailable { get; }
}

/// <summary>
/// Opens the drawer. Separate from the printer because on some tills it
/// is wired through the printer and on others it is not, and the till
/// operator should not be able to tell the difference.
/// </summary>
public interface ICashDrawer
{
    /// <remarks>
    /// Asynchronous for the same reason as the printers: which printer
    /// the drawer hangs off is a row in the database, and waiting for it
    /// from the user-interface thread deadlocked the till.
    /// </remarks>
    Task OpenAsync(CancellationToken ct = default);

    Task<bool> IsAvailableAsync(CancellationToken ct = default);
}
