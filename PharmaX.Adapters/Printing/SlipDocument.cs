using System.Drawing.Printing;
using System.Globalization;
using PharmaX.Application.Sales;

namespace PharmaX.Adapters.Printing;

/// <summary>
/// A till-roll document laid out thirty-two across - the cash-up slip -
/// printed exactly as the receipt is: the pharmacy's name, address, VAT
/// and telephone centred at the head, then the lines in the receipt's own
/// large type, on a page as long as the slip.
/// </summary>
public sealed class SlipDocument : PrintDocument
{
    private readonly PrintableDocument _document;
    private readonly ReceiptHeaderDto _header;
    private int _next;

    public SlipDocument(PrintableDocument document, ReceiptHeaderDto header)
    {
        _document = document;
        _header = header;

        ReceiptDocument.ApplyRoll(DefaultPageSettings, 40);
    }

    /// <summary>The slip, top to bottom. Public so a test can read it.</summary>
    public IReadOnlyList<ReceiptRow> Rows()
    {
        const int columns = ReceiptDocument.CharactersPerLine;
        var rows = new List<ReceiptRow>();

        void Centre(string text, ReceiptStyle style = ReceiptStyle.Body)
        {
            foreach (var piece in SlipLayout.Wrap(text, columns, columns))
                rows.Add(new ReceiptRow(piece, style, Centred: true));
        }

        var rule = new ReceiptRow(new string('-', columns), ReceiptStyle.Body, false);

        Centre(_header.TradingName, ReceiptStyle.Title);

        foreach (var line in (_header.Address ?? "").Split('\n')
                     .Select(p => p.Trim()).Where(p => p.Length > 0))
            Centre(line);

        if (!string.IsNullOrWhiteSpace(_header.VatNumber))
            Centre($"VAT No: {_header.VatNumber.Trim()}");

        if (!string.IsNullOrWhiteSpace(_header.Telephone))
            Centre($"Tel: {_header.Telephone.Trim()}");

        rows.Add(rule);
        Centre(_document.Title, ReceiptStyle.Bold);
        Centre(string.Format(CultureInfo.CurrentCulture, "by {0}", _header.OperatorName));
        rows.Add(rule);

        // Wider lines than the roll wrap rather than run off it.
        foreach (var line in _document.Lines)
            foreach (var piece in line.Length <= columns
                         ? new[] { line }
                         : SlipLayout.Wrap(line, columns, columns))
                rows.Add(new ReceiptRow(piece, IsHeading(piece)
                    ? ReceiptStyle.Bold
                    : ReceiptStyle.Body, Centred: false));

        return rows;
    }

    /// Capitalised totals stand out: NET TAKINGS, CASH IN DRAWER.
    private static bool IsHeading(string line)
    {
        var label = line.TrimEnd().Split("  ", StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault() ?? "";
        return label.Length > 3 && label.Any(char.IsLetter)
               && label.Equals(label.ToUpperInvariant(), StringComparison.Ordinal);
    }

    internal void FitRoll()
    {
        var lines = Rows().Count;
        ReceiptDocument.ApplyRoll(DefaultPageSettings, lines);

        try
        {
            ReceiptDocument.ApplyRoll(PrinterSettings.DefaultPageSettings, lines);
        }
        catch (InvalidPrinterException)
        {
        }
    }

    protected override void OnQueryPageSettings(QueryPageSettingsEventArgs e)
    {
        base.OnQueryPageSettings(e);
        ReceiptDocument.ApplyRoll(e.PageSettings, Math.Max(1, Rows().Count - _next));
    }

    protected override void OnBeginPrint(PrintEventArgs e)
    {
        base.OnBeginPrint(e);
        _next = 0;
        FitRoll();
    }

    protected override void OnPrintPage(PrintPageEventArgs e)
    {
        base.OnPrintPage(e);
        ArgumentNullException.ThrowIfNull(e);

        e.HasMorePages = ReceiptDocument.Draw(e.Graphics!, FixedPitch.Area(e),
                                              Rows(), ref _next);
    }

    /// For a till printer on the "Generic / Text Only" driver.
    internal byte[] AsText() => ReceiptDocument.AsText(Rows());
}
