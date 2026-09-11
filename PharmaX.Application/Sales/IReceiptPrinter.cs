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
    PrintOutcome Print(SaleDto sale, ReceiptHeaderDto header);
    bool IsAvailable { get; }
}

/// <summary>
/// Opens the drawer. Separate from the printer because on some tills it
/// is wired through the printer and on others it is not, and the till
/// operator should not be able to tell the difference.
/// </summary>
public interface ICashDrawer
{
    void Open();
    bool IsAvailable { get; }
}

public sealed record CardAuthorisationDto(bool Approved, string? Reference,
                                          string? Message);

/// <summary>
/// A card terminal. The manual implementation is always available and is
/// what a site uses before an integrated terminal is commissioned: the
/// operator reads the authorisation code off the slip and types it in.
/// </summary>
public interface ICardTerminal
{
    string Name { get; }
    bool IsIntegrated { get; }

    Task<CardAuthorisationDto> AuthoriseAsync(decimal amount,
        CancellationToken ct = default);
}
