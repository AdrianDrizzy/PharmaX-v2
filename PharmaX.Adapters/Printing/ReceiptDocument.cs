using System.Drawing.Printing;
using System.Globalization;
using PharmaX.Application.Sales;
using PharmaX.Application.Sales.Dtos;

namespace PharmaX.Adapters.Printing;

/// <summary>
/// Receipt printing uses GDI+ drawing rather than a report engine,
/// because a 58 or 80 millimetre roll is a fixed-width canvas and the
/// arithmetic is simpler done directly.
/// </summary>
/// <remarks>
/// Lives in PharmaX.Adapters rather than PharmaX.Desktop so that
/// ReceiptPrinter, which is an adapter, can reach it. Putting the
/// document in the Desktop project would mean an adapter referencing the
/// presentation layer, which is the one direction the dependency rule
/// forbids (Section 4.1).
/// </remarks>
public sealed class ReceiptDocument : PrintDocument
{
    private const int CharactersPerLine = 42;

    private readonly SaleDto _sale;
    private readonly ReceiptHeaderDto _header;
    private readonly Font _body = new("Consolas", 8f);
    private readonly Font _bold = new("Consolas", 8f, FontStyle.Bold);
    private readonly Font _title = new("Consolas", 11f, FontStyle.Bold);

    public ReceiptDocument(SaleDto sale, ReceiptHeaderDto header)
    {
        _sale = sale;
        _header = header;

        // 80 mm roll, continuous length.
        DefaultPageSettings.PaperSize = new PaperSize("Roll", 315, 1200);
        DefaultPageSettings.Margins = new Margins(10, 10, 10, 10);
    }

    protected override void OnPrintPage(PrintPageEventArgs e)
    {
        base.OnPrintPage(e);
        ArgumentNullException.ThrowIfNull(e);

        RenderPage(e.Graphics!, e.MarginBounds);
        e.HasMorePages = false;
    }

    /// <summary>
    /// Draws the whole slip onto a surface.
    /// </summary>
    /// <remarks>
    /// Separate from OnPrintPage so that a test can render the receipt
    /// to a bitmap and look at it. A receipt is the one thing PharmaX
    /// produces that a customer keeps and an auditor reads, and
    /// "it compiled" is not evidence that the fiscal particulars and
    /// the pharmacy's logo actually landed on the paper.
    /// </remarks>
    internal void RenderPage(Graphics g, Rectangle margins)
    {
        ArgumentNullException.ThrowIfNull(g);

        var culture = CultureInfo.CurrentCulture;
        var width = margins.Width;
        float y = margins.Top;
        var left = margins.Left;

        void Line(string text, Font? font = null)
        {
            g.DrawString(text, font ?? _body, Brushes.Black, left, y);
            y += (font ?? _body).GetHeight(g);
        }

        void Centre(string text, Font font)
        {
            var size = g.MeasureString(text, font);
            g.DrawString(text, font, Brushes.Black,
                left + (width - size.Width) / 2, y);
            y += font.GetHeight(g);
        }

        void Rule() => Line(new string('-', CharactersPerLine));

        y += DrawLogo(g, left, width, y);

        // Fiscal particulars are mandatory (specification Section 6.10).
        Centre(_header.TradingName, _title);

        // A pharmacy with no company name behind it - one cleared at the
        // System Console - prints no blank line where it would have been.
        if (!string.IsNullOrWhiteSpace(_header.RegisteredName))
            Centre(_header.RegisteredName, _body);
        // One printed line per address line. Centre advances by a single
        // line height, so a multi-line address drawn in one call
        // overprinted the VAT line beneath it.
        foreach (var addressLine in (_header.Address ?? "")
                     .Split('\n')
                     .Select(part => part.Trim())
                     .Where(part => part.Length > 0))
            Centre(addressLine, _body);

        // A pharmacy not yet registered for VAT prints no VAT number
        // rather than a bare "VAT" label.
        Centre(string.IsNullOrWhiteSpace(_header.VatNumber)
                ? $"Lic {_header.LicenceNumber}"
                : $"VAT {_header.VatNumber}  Lic {_header.LicenceNumber}",
            _body);
        Rule();

        Line($"{_header.BranchAndTill}  {_header.OperatorName}");
        Line(_header.PrintedAtLocal.ToString("dd/MM/yyyy HH:mm", culture));
        Line($"Document {_sale.DocumentNumber}", _bold);
        Rule();

        foreach (var l in _sale.Lines.Where(x => !x.IsVoided))
        {
            // The whole description, over as many lines as it takes. It
            // was cut at 24 characters, which took "60s x 30" off every
            // collected script - the only place the receipt says how much
            // of the medicine was paid for.
            var description = Wrap(l.Description, 24, 38);

            // 3 + 1 + 24 + 14 = the 42 characters of the roll, so each
            // amount ends under the totals rather than four short of them.
            Line(string.Format(culture, "{0,3:0.##} {1,-24}{2,14:N2}",
                l.Quantity, description[0], l.LineTotal));

            foreach (var more in description.Skip(1))
                Line("    " + more);

            if (l.Discount > 0m)
                Line(string.Format(culture, "      less discount {0,22:N2}",
                    l.Discount));

            if (l.BatchNumber is not null)
                Line(string.Format(culture, "      Batch {0} Exp {1:MM/yyyy}",
                    l.BatchNumber, l.Expiry));
        }

        Rule();
        Amount("Subtotal", _sale.Totals.Subtotal);
        Amount("Discount", _sale.Totals.Discount);
        Amount("VAT", _sale.Totals.VatTotal);
        Amount("TOTAL", _sale.Totals.Total, _bold);
        Rule();

        // How it was paid. A receipt that does not say whether it was
        // cash or card cannot settle the commonest query at the counter.
        foreach (var payment in _sale.Payments ?? Array.Empty<PaymentDto>())
            Amount($"Paid by {TenderLabel(payment.TenderType)}",
                   payment.Amount);

        Amount("Tendered", _sale.Totals.Tendered);
        Amount("Change", _sale.Totals.Change);
        Rule();
        Centre("Thank you", _body);

        void Amount(string label, decimal value, Font? font = null) =>
            Line(string.Format(culture, "{0,-30}{1,12:N2}", label, value),
                 font);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _body.Dispose();
            _bold.Dispose();
            _title.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// The pharmacy's logo, centred above the trading name. Returns the
    /// height it used, or zero.
    /// </summary>
    /// <remarks>
    /// Capped at a third of the roll's height allowance and never
    /// enlarged past its own size: a logo that pushes the fiscal
    /// particulars off the first page is worse than no logo. Failure to
    /// read the file is silent for the same reason a printer fault is
    /// (POS-014) - the sale is already committed and the customer is
    /// waiting for their slip.
    /// </remarks>
    private float DrawLogo(Graphics g, int left, int width, float top)
    {
        if (string.IsNullOrWhiteSpace(_header.LogoPath)
            || !File.Exists(_header.LogoPath)) return 0f;

        try
        {
            using var stream =
                new MemoryStream(File.ReadAllBytes(_header.LogoPath));

            using var logo = Image.FromStream(stream);

            const int maximumHeight = 120;

            var scale = Math.Min(
                Math.Min((float)width / logo.Width,
                         (float)maximumHeight / logo.Height), 1f);

            var drawnWidth = logo.Width * scale;
            var drawnHeight = logo.Height * scale;

            g.DrawImage(logo, left + (width - drawnWidth) / 2, top,
                        drawnWidth, drawnHeight);

            return drawnHeight + 6f;
        }
#pragma warning disable CA1031 // Deliberately broad: see the remarks.
        catch (Exception)
#pragma warning restore CA1031
        {
            return 0f;
        }
    }

    /// <summary>
    /// Splits a description at spaces: the first piece fits beside the
    /// quantity and the amount, the rest fit the line beneath. A word too
    /// long for either is split where it has to be.
    /// </summary>
    internal static IReadOnlyList<string> Wrap(string value, int first,
                                               int rest)
    {
        var pieces = new List<string>();
        var remaining = (value ?? "").Trim();

        while (remaining.Length > 0)
        {
            var width = pieces.Count == 0 ? first : rest;

            if (remaining.Length <= width)
            {
                pieces.Add(remaining);
                break;
            }

            var cut = remaining.LastIndexOf(' ', width);
            if (cut <= 0) cut = width;

            pieces.Add(remaining[..cut].TrimEnd());
            remaining = remaining[cut..].TrimStart();
        }

        if (pieces.Count == 0) pieces.Add("");

        return pieces;
    }

    /// The tender as the till offers it, not as the enum spells it.
    private static string TenderLabel(string tender) => tender switch
    {
        "Eft" => "EFT",
        "MedicalAid" => "medical aid",
        _ => tender.ToLowerInvariant()
    };
}
