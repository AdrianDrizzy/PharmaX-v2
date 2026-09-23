using PharmaX.Application.Sales.Dtos;

namespace PharmaX.Application.Sales;

public enum PrintOutcome { Printed, Failed, NoPrinter }

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
public sealed record ReceiptHeaderDto(string TradingName,
    string RegisteredName, string Address, string VatNumber,
    string LicenceNumber, string BranchAndTill, string OperatorName,
    DateTime PrintedAtLocal, string? LogoPath = null);

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
    Task<PrintOutcome> PrintAsync(SaleDto sale, ReceiptHeaderDto header,
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
