using System.Drawing.Printing;
using System.Globalization;
using PharmaX.Application.Sales;
using PharmaX.Application.Sales.Dtos;

namespace PharmaX.Adapters.Printing;

/// <summary>
/// Receipt printing uses GDI+ drawing rather than a report engine,
/// because a till roll is a fixed-width canvas and the arithmetic is
/// simpler done directly. The pharmacy's roll is 76 millimetres and 40
/// columns wide; the type is sized to fit 40 columns to whatever the
/// printer can reach (FixedPitch).
/// </summary>
/// <remarks>
/// Lives in PharmaX.Adapters rather than PharmaX.Desktop so that
/// ReceiptPrinter, which is an adapter, can reach it. Putting the
/// document in the Desktop project would mean an adapter referencing the
/// presentation layer, which is the one direction the dependency rule
/// forbids (Section 4.1).
///
/// The pharmacy's name, address and numbers at the head, then laid out
/// as the pharmacy's previous till slips:
/// <code>
/// ----------------------------------------
///               Tax Invoice
///                Cash Sale
/// ----------------------------------------
/// Trans No: 31159
/// Till Operator : test
/// Till Number : 1
/// ----------------------------------------
/// Code    QTY    Unit Price     Amount Inc
/// ----------------------------------------
/// 1616     10 @        0.25
/// MYPRODOL                         2.50 NV
/// ----------------------------------------
/// Sub Total                        2.50
/// VAT-USD :                        0.00
/// TOTAL-USD:                       2.50
/// ----------------------------------------
/// Cash USD                         2.50
/// ----------------------------------------
/// ----------------------------------------
///
///                Thank you
/// </code>
/// "Tax Invoice" where those slips said "Fiscal Tax Invoice": PharmaX is
/// not connected to a fiscal device, and the word would say it was. No
/// logo: a picture costs roll, time and toner on every sale, and the name
/// says who the pharmacy is.
/// </remarks>
public sealed class ReceiptDocument : PrintDocument
{
    internal const int CharactersPerLine = 40;

    /// Where every amount ends: three columns short of the edge, so "NV"
    /// fits after an item's amount and the totals line up under it.
    private const int AmountEnd = CharactersPerLine - 3;

    /// The code column; an item's quantity starts after it.
    private const int CodeWidth = 8;

    /// The heads of the item columns.
    private const string Heads = "Code    QTY    Unit Price     Amount Inc";

    /// How many and at what each, beside the code: each figure ends under
    /// its head.
    private const string Figures = "{0,3} @{1,12}";

    private readonly SaleDto _sale;
    private readonly ReceiptHeaderDto _header;
    private readonly Font _title = new("Consolas", 11f, FontStyle.Bold);
    private int _next;

    public ReceiptDocument(SaleDto sale, ReceiptHeaderDto header)
    {
        _sale = sale;
        _header = header;

        // 76 mm roll, continuous length.
        DefaultPageSettings.PaperSize = new PaperSize("Roll", 299, 1200);
        DefaultPageSettings.Margins = new Margins(5, 5, 10, 10);
    }

    protected override void OnBeginPrint(PrintEventArgs e)
    {
        base.OnBeginPrint(e);
        _next = 0;
    }

    protected override void OnPrintPage(PrintPageEventArgs e)
    {
        base.OnPrintPage(e);
        ArgumentNullException.ThrowIfNull(e);

        e.HasMorePages = RenderPage(e.Graphics!, FixedPitch.Area(e));
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

        // The value wraps under itself rather than running off the roll:
        // on forty columns a longer patient's name pushed their file
        // number off the edge.
        void Field(string lead, string value)
        {
            var room = CharactersPerLine - lead.Length;
            var pieces = Wrap(value, room, room);

            Line(lead + pieces[0]);

            foreach (var more in pieces.Skip(1))
                Line(new string(' ', lead.Length) + more);
        }

        void Amount(string label, decimal value) =>
            Line(AmountLine(label, value, culture));

        // Who sold it: the name and the address, the VAT number a tax
        // invoice cannot be one without (specification Section 6.10), and
        // the numbers to ring about what is on the slip.
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

        if (!string.IsNullOrWhiteSpace(_header.Telephone))
            Centre($"Tel: {_header.Telephone.Trim()}");

        if (!string.IsNullOrWhiteSpace(_header.WhatsApp))
            Centre($"WhatsApp: {_header.WhatsApp.Trim()}");

        Rule();
        Centre("Tax Invoice");
        Centre(Kind());
        Rule();

        if (_header.IsCopy) Centre(CopyMark, ReceiptStyle.Bold);

        Field("Trans No: ", _sale.DocumentNumber);
        Field("Till Operator : ", _header.OperatorName);
        Field("Till Number : ", Till());

        // When, which a tax invoice has to say.
        Line(string.Format(culture, "Date : {0:dd/MM/yyyy HH:mm}",
            _header.PrintedAtLocal));

        // Whose it is. The account a sale went on, and the patient and
        // script a collection paid for, were on no receipt at all
        // (ADM-DEF-09, ADM-OBS-15).
        var who = _sale.Particulars;

        if (who?.AccountNumber is string account)
            Field("Account : ", $"{account} {who.AccountName}".Trim());

        if (who?.PatientName is string patient)
            Field("Patient : ", who.FileNumber is null
                ? patient
                : $"{patient} ({who.FileNumber})");

        if (who?.ScriptNumber is string script)
            Line($"REF: Script No-{script}");

        Rule();
        Line(Heads);
        Rule();

        var sold = _sale.Lines.Where(x => !x.IsVoided).ToList();
        var printed = Printed(sold, who?.ScriptNumber);

        foreach (var l in printed)
        {
            var code = (l.ProductCode ?? "").Trim();
            var figures = new string(' ', CodeWidth)
                          + string.Format(culture, Figures,
                              l.Quantity.ToString("0.###", culture),
                              l.UnitPrice.ToString("N2", culture));

            // The code in the space before the quantity, with a space still
            // between them. A code longer than that takes a line of its
            // own, rather than pushing the figures out from under their
            // heads.
            var before = figures.Length - figures.TrimStart().Length - 1;

            if (code.Length <= before)
            {
                Line(code + figures[code.Length..]);
            }
            else
            {
                Line(code);
                Line(figures);
            }

            // The name with what the line came to, after any discount, so
            // the amounts add up to what was paid; the whole name, over as
            // many lines as it takes.
            var amount = l.LineTotal.ToString("N2", culture);
            var room = AmountEnd - amount.Length - 1;
            var name = Wrap(l.Description, room, CharactersPerLine);

            Line(name[0].PadRight(room + 1) + amount
                 + (l.VatRatePercent == 0m ? " NV" : ""));

            foreach (var more in name.Skip(1)) Line(more);
        }

        Rule();

        // The sum of the amounts above. The sale's own subtotal is before
        // its discounts, which the amounts are not, so a discounted slip
        // did not add up.
        Amount("Sub Total", printed.Sum(l => l.LineTotal));

        foreach (var (label, vat) in VatLines(sold, currency, culture))
            Amount(label, vat);

        Amount(Labelled("TOTAL", currency) + ":", _sale.Totals.Total);
        Rule();

        // What was handed over for each tender. A payment is recorded at
        // what stayed in the drawer, and the change came out of the cash,
        // so the cash line adds it back: 20.00 handed over, not 14.90.
        var change = _sale.Totals.Change;

        foreach (var payment in _sale.Payments ?? Array.Empty<PaymentDto>())
        {
            var handed = payment.Amount;

            if (payment.TenderType == "Cash")
            {
                handed += change;
                change = 0m;
            }

            Amount(TenderLabel(payment.TenderType)
                   + (currency.Length > 0 ? " " + currency : ""), handed);
        }

        if (_sale.Totals.Change > 0m)
            Amount("Change" + (currency.Length > 0 ? " " + currency : ""),
                   _sale.Totals.Change);

        Rule();

        // The patient's portion is what was paid here; the scheme's is
        // claimed. Both, so the patient can see the script was not
        // charged to them in full.
        if (who?.SchemeShare is decimal share)
        {
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

        Line("");
        Centre("Thank you");

        return rows;
    }

    /// <summary>
    /// Draws as much of the slip as fits on one page, from where the last
    /// page stopped, and says whether there is more.
    /// </summary>
    /// <remarks>
    /// Separate from OnPrintPage so that a test can render the receipt
    /// to a bitmap and look at it. A receipt is the one thing PharmaX
    /// produces that a customer keeps and an auditor reads, and
    /// "it compiled" is not evidence that the fiscal particulars actually
    /// landed on the paper.
    ///
    /// The slip used to be drawn as one page whatever its length, so on a
    /// long sale everything past the foot of the page - the total and the
    /// tenders first - was never printed.
    /// </remarks>
    internal bool RenderPage(Graphics g, Rectangle margins)
    {
        ArgumentNullException.ThrowIfNull(g);

        float y = margins.Top;

        using var centred = new StringFormat
        {
            Alignment = StringAlignment.Center
        };

        // Forty columns to the width the printer reaches, up to the 8
        // points the slip was laid out in.
        var size = FixedPitch.Size(g, CharactersPerLine, margins.Width, 8f);
        using var body = new Font(FixedPitch.Family, size);
        using var bold = new Font(FixedPitch.Family, size, FontStyle.Bold);
        using var exact = FixedPitch.Format();

        var rows = Rows();

        while (_next < rows.Count)
        {
            var row = rows[_next];
            var font = row.Style switch
            {
                ReceiptStyle.Title => _title,
                ReceiptStyle.Bold => bold,
                _ => body
            };

            // Centred rows wrap within the roll: a long trading name in
            // the large type would otherwise run off both edges.
            var height = row.Centred
                ? Math.Max(g.MeasureString(row.Text, font, margins.Width).Height,
                           font.GetHeight(g))
                : font.GetHeight(g);

            // A row that will not fit goes to the next page - unless
            // nothing is on this one yet, or it would never be printed.
            if (y > margins.Top && y + height > margins.Bottom) return true;

            if (row.Centred)
                g.DrawString(row.Text, font, Brushes.Black,
                    new RectangleF(margins.Left, y, margins.Width, height),
                    centred);
            else
                g.DrawString(row.Text, font, Brushes.Black, margins.Left, y,
                             exact);

            y += height;
            _next++;
        }

        return false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _title.Dispose();

        base.Dispose(disposing);
    }

    /// <summary>
    /// The items as the slip prints them: everything sold over the
    /// counter as it was rung up, and a collected script as one line,
    /// "Script Charge: 13972", for the co-payment on all of it.
    /// </summary>
    /// <remarks>
    /// What the patient was given is on the labels and the script. A
    /// receipt is handed across the counter, kept in a purse and sent to
    /// an employer, and is no place to list somebody's medicines - the
    /// pharmacy's previous system printed the script number alone.
    /// </remarks>
    private static IReadOnlyList<SaleLineDto> Printed(
        IReadOnlyList<SaleLineDto> sold, string? scriptNumber)
    {
        var script = sold.Where(l => l.IsScript).ToList();
        if (script.Count == 0) return sold;

        var charge = script[0] with
        {
            ProductId = Guid.Empty,
            ProductCode = null,
            Description = string.IsNullOrWhiteSpace(scriptNumber)
                ? "Script Charge"
                : $"Script Charge: {scriptNumber.Trim()}",
            Quantity = 1m,
            UnitPrice = script.Sum(l => l.Quantity * l.UnitPrice),
            Discount = script.Sum(l => l.Discount),
            LineTotal = script.Sum(l => l.LineTotal),

            // Marked NV only when nothing on the script carried VAT.
            VatRatePercent = script.Max(l => l.VatRatePercent)
        };

        return sold.Where(l => !l.IsScript || ReferenceEquals(l, script[0]))
                   .Select(l => l.IsScript ? charge : l)
                   .ToList();
    }

    /// <summary>
    /// The kind of sale, by how it was paid: "Cash Sale", "Card Sale",
    /// "Cash/Card Sale" for a sale settled two ways.
    /// </summary>
    private string Kind()
    {
        var tenders = (_sale.Payments ?? Array.Empty<PaymentDto>())
            .Select(p => TenderLabel(p.TenderType))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return (tenders.Count == 0 ? "Cash" : string.Join("/", tenders))
               + " Sale";
    }

    private string Till() =>
        string.IsNullOrWhiteSpace(_header.TillNumber)
            ? _header.BranchAndTill
            : _header.TillNumber.Trim();

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
