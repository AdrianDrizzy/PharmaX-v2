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
///
/// Laid out as the pharmacy's fiscal tax invoice slip: the pharmacy's
/// name and address, the transaction, a code-and-quantity line over a
/// description-and-amount line for each item, then the totals and the
/// tenders in the trading currency. No logo: a picture costs roll, time
/// and toner on every sale, and the name says who the pharmacy is.
/// </remarks>
public sealed class ReceiptDocument : PrintDocument
{
    private const int CharactersPerLine = 42;

    /// Where every amount ends, so the item amounts line up under the
    /// totals and a two-letter VAT mark still fits after them.
    private const int AmountEnd = 39;

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
    /// The slip, top to bottom, as it is printed. Public so a test can
    /// read what the customer will be handed without a printer.
    /// </summary>
    public IReadOnlyList<ReceiptRow> Rows()
    {
        var culture = CultureInfo.CurrentCulture;
        var rows = new List<ReceiptRow>();
        var currency = (_header.CurrencyCode ?? "").Trim();

        void Line(string text, ReceiptStyle style = ReceiptStyle.Body) =>
            rows.Add(new ReceiptRow(text, style, Centred: false));

        void Centre(string text, ReceiptStyle style = ReceiptStyle.Body) =>
            rows.Add(new ReceiptRow(text, style, Centred: true));

        void Rule() => Line(new string('-', CharactersPerLine));

        void Field(string label, string value) =>
            Line($"{label,-12}: {value}");

        void Amount(string label, decimal value,
                    ReceiptStyle style = ReceiptStyle.Body) =>
            Line(AmountLine(label, value, culture), style);

        // Who sold it: the name and the address, and the VAT number a tax
        // invoice cannot be one without (specification Section 6.10).
        Centre(_header.TradingName, ReceiptStyle.Title);

        // One printed line per address line: a multi-line address drawn
        // in one call overprinted whatever came under it.
        foreach (var addressLine in (_header.Address ?? "")
                     .Split('\n')
                     .Select(part => part.Trim())
                     .Where(part => part.Length > 0))
            Centre(addressLine);

        // A pharmacy not yet registered for VAT prints no VAT number
        // rather than a bare label.
        if (!string.IsNullOrWhiteSpace(_header.VatNumber))
            Centre($"VAT No: {_header.VatNumber.Trim()}");

        Rule();
        Centre("Fiscal Tax Invoice", ReceiptStyle.Bold);
        Centre(SaleKind());

        if (_header.IsCopy) Centre(CopyMark, ReceiptStyle.Bold);

        Rule();

        Field("Trans No", _sale.DocumentNumber);
        Field("Date", _header.PrintedAtLocal.ToString("dd/MM/yyyy HH:mm",
            culture));
        Field("Operator", _header.OperatorName);
        Field("Till Number", string.IsNullOrWhiteSpace(_header.TillNumber)
            ? _header.BranchAndTill
            : _header.TillNumber.Trim());

        // Whose it is. The account a sale went on, and the patient and
        // script a collection paid for, were on no receipt at all
        // (ADM-DEF-09, ADM-OBS-15).
        var who = _sale.Particulars;

        if (who?.AccountNumber is string account)
            Field("Account", $"{account} {who.AccountName}".Trim());

        if (who?.PatientName is string patient)
            Field("Patient", who.FileNumber is null
                ? patient
                : $"{patient} ({who.FileNumber})");

        if (who?.ScriptNumber is string script)
            Line($"REF: Script No-{script}");

        Rule();
        Line(string.Format(culture, "{0,-10}{1,-7}{2,11}{3,11}",
            "Code", "QTY", "Unit Price", "Amount Inc"));
        Rule();

        var sold = _sale.Lines.Where(x => !x.IsVoided).ToList();

        foreach (var l in sold)
        {
            Line(CodeLine(l, culture));

            // The whole description, over as many lines as it takes. It
            // was cut short once, which took "60s x 30" off every
            // collected script - the only place the receipt says how much
            // of the medicine was paid for.
            var description = Wrap(l.Description, 28, AmountEnd);
            var mark = l.VatRatePercent == 0m ? " NV" : "";

            Line(string.Format(culture, "{0,-28}{1,11:N2}{2}",
                description[0], l.LineTotal, mark));

            foreach (var more in description.Skip(1))
                Line(more);

            if (l.Discount > 0m)
                Amount("  less discount", l.Discount);
        }

        Rule();
        Amount("Sub Total", _sale.Totals.Subtotal);

        if (_sale.Totals.Discount > 0m)
            Amount("Discount", _sale.Totals.Discount);

        foreach (var (label, vat) in VatLines(sold, currency, culture))
            Amount(label, vat);

        Amount(Labelled("TOTAL", currency) + ":", _sale.Totals.Total,
               ReceiptStyle.Bold);
        Rule();

        // How it was paid. A receipt that does not say whether it was
        // cash or card cannot settle the commonest query at the counter.
        foreach (var payment in _sale.Payments ?? Array.Empty<PaymentDto>())
            Amount(TenderLabel(payment.TenderType)
                   + (currency.Length > 0 ? " " + currency : ""),
                   payment.Amount);

        if (_sale.Totals.Change > 0m)
        {
            Amount("Tendered", _sale.Totals.Tendered);
            Amount("Change", _sale.Totals.Change);
        }

        // The patient's portion is what was paid here; the scheme's is
        // claimed. Both, so the patient can see the script was not
        // charged to them in full.
        if (who?.SchemeShare is decimal share)
        {
            Rule();
            Amount($"Claimed from {who.SchemeName ?? "medical aid"}", share);
            Amount("Your portion", _sale.Totals.Total);
        }

        // Somebody signs for a sale put on their account.
        if (_sale.Payments?.Any(p => p.TenderType == "Account") == true)
        {
            Line("");
            Line("Signed ______________________________");
        }

        Rule();

        if (_header.IsCopy) Centre(CopyMark, ReceiptStyle.Bold);

        Centre("Thank you");

        return rows;
    }

    /// <summary>
    /// Draws the whole slip onto a surface.
    /// </summary>
    /// <remarks>
    /// Separate from OnPrintPage so that a test can render the receipt
    /// to a bitmap and look at it. A receipt is the one thing PharmaX
    /// produces that a customer keeps and an auditor reads, and
    /// "it compiled" is not evidence that the fiscal particulars actually
    /// landed on the paper.
    /// </remarks>
    internal void RenderPage(Graphics g, Rectangle margins)
    {
        ArgumentNullException.ThrowIfNull(g);

        float y = margins.Top;

        using var centred = new StringFormat
        {
            Alignment = StringAlignment.Center
        };

        foreach (var row in Rows())
        {
            var font = row.Style switch
            {
                ReceiptStyle.Title => _title,
                ReceiptStyle.Bold => _bold,
                _ => _body
            };

            // Centred rows wrap within the roll: a long trading name in
            // the large type would otherwise run off both edges.
            if (row.Centred)
            {
                var height = Math.Max(
                    g.MeasureString(row.Text, font, margins.Width).Height,
                    font.GetHeight(g));

                g.DrawString(row.Text, font, Brushes.Black,
                    new RectangleF(margins.Left, y, margins.Width, height),
                    centred);

                y += height;
                continue;
            }

            g.DrawString(row.Text, font, Brushes.Black, margins.Left, y);
            y += font.GetHeight(g);
        }
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
    /// What kind of sale the slip is for, under the title.
    /// </summary>
    private string SaleKind() =>
        _sale.Payments?.Any(p => p.TenderType == "Account") == true
            ? "Account Sale"
            : "Cash Sale";

    /// <summary>
    /// The first line of an item: its code, how many, and the price of
    /// one.
    /// </summary>
    private static string CodeLine(SaleLineDto line, CultureInfo culture)
    {
        var code = (line.ProductCode ?? "").Trim();
        var quantity = line.Quantity.ToString("0.###", culture) + " @";
        var price = line.UnitPrice.ToString("N2", culture);

        // A code longer than its column pushes the quantity along rather
        // than running into it.
        var lead = (code.Length < 10 ? code.PadRight(10) : code + " ")
                   + quantity;

        const int priceEnd = 28;

        return lead.PadRight(Math.Max(lead.Length + 1,
                   priceEnd - price.Length)) + price;
    }

    /// <summary>
    /// The VAT, one line per rate the sale was charged at.
    /// </summary>
    /// <remarks>
    /// One rate is the usual case and prints the sale's own VAT total
    /// beside it. A sale mixing rates prints each rate's share, worked
    /// out from the lines, so the rate printed is never a rate nothing on
    /// the slip was charged at.
    /// </remarks>
    private IEnumerable<(string Label, decimal Vat)> VatLines(
        IReadOnlyList<SaleLineDto> lines, string currency,
        CultureInfo culture)
    {
        var label = Labelled("VAT", currency);

        var rates = lines.Select(l => l.VatRatePercent)
            .Where(rate => rate > 0m)
            .Distinct()
            .OrderByDescending(rate => rate)
            .ToList();

        if (rates.Count == 0)
        {
            yield return (label + " :", _sale.Totals.VatTotal);
            yield break;
        }

        if (rates.Count == 1)
        {
            yield return (Rated(rates[0]), _sale.Totals.VatTotal);
            yield break;
        }

        foreach (var rate in rates)
        {
            var vat = Math.Round(lines
                .Where(l => l.VatRatePercent == rate)
                .Sum(l => l.LineTotal * rate / (100m + rate)), 2,
                MidpointRounding.AwayFromZero);

            yield return (Rated(rate), vat);
        }

        string Rated(decimal rate) =>
            string.Format(culture, "{0} : {1:0.0}%", label, rate);
    }

    private static string Labelled(string label, string currency) =>
        currency.Length == 0 ? label : $"{label}-{currency}";

    private static string AmountLine(string label, decimal value,
                                     CultureInfo culture)
    {
        var amount = value.ToString("N2", culture);
        var room = AmountEnd - amount.Length - 1;

        return Trim(label, room).PadRight(room + 1) + amount;
    }

    /// <summary>
    /// Splits a description at spaces: the first piece fits beside the
    /// quantity and the amount, the rest fit the line beneath. A word too
    /// long for either is split where it has to be.
    /// </summary>
    internal static IReadOnlyList<string> Wrap(string value, int first,
                                               int rest) =>
        SlipLayout.Wrap(value, first, rest);

    /// What a reprint says, above the sale and again at the foot.
    internal const string CopyMark = "*** COPY - REPRINTED ***";

    private static string Trim(string text, int width) =>
        text.Length <= width ? text : text[..width];

    /// The tender as the till offers it, not as the enum spells it.
    private static string TenderLabel(string tender) => tender switch
    {
        "Eft" => "EFT",
        "EcoCash" => "EcoCash",
        "MedicalAid" => "Medical aid",
        _ => tender
    };
}

public enum ReceiptStyle { Body, Bold, Title }

/// <summary>One printed line of a receipt.</summary>
public sealed record ReceiptRow(string Text, ReceiptStyle Style,
                                bool Centred);
