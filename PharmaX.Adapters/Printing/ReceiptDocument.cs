using System.Drawing.Printing;
using System.Globalization;
using System.Text;
using PharmaX.Application.Sales;
using PharmaX.Application.Sales.Dtos;

namespace PharmaX.Adapters.Printing;

/// <summary>
/// Receipt printing uses GDI+ drawing rather than a report engine,
/// because a till roll is a fixed-width canvas and the arithmetic is
/// simpler done directly. The pharmacy's roll is 75 millimetres; the slip
/// is laid out 32 columns wide, the type sized so they fill the width the
/// printer reaches, and the block centred on it. The page is as long as the slip,
/// so the printer feeds the whole of it, including the foot, without a
/// button press on the printer.
/// </summary>
/// <remarks>
/// Lives in PharmaX.Adapters rather than PharmaX.Desktop so that
/// ReceiptPrinter, which is an adapter, can reach it. Putting the
/// document in the Desktop project would mean an adapter referencing the
/// presentation layer, which is the one direction the dependency rule
/// forbids (Section 4.1).
///
/// The pharmacy's name, address and numbers at the head, then laid out
/// as the pharmacy's previous till slips, an item's code, name and
/// figures each on a line of their own:
/// <code>
/// --------------------------------
///           Tax Invoice
///            Cash Sale
/// --------------------------------
/// Trans No: 31159
/// Till Operator : test
/// Till Number : 1
/// --------------------------------
/// Code  QTY  Unit Price Amount Inc
/// --------------------------------
/// 1616
/// MYPRODOL
///      10 @        0.25    2.50 NV
/// --------------------------------
/// Sub Total                2.50
/// VAT-USD :                0.00
/// TOTAL-USD:               2.50
/// --------------------------------
/// Cash USD                 2.50
/// Change USD               0.00
/// --------------------------------
/// --------------------------------
///
///            Thank you
/// </code>
/// Thirty-two columns rather than forty so the type can be larger: at
/// forty, larger type pushed the totals off the 75 mm roll.
/// "Tax Invoice" where those slips said "Fiscal Tax Invoice": PharmaX is
/// not connected to a fiscal device, and the word would say it was. No
/// logo: a picture costs roll, time and toner on every sale, and the name
/// says who the pharmacy is.
/// </remarks>
public sealed class ReceiptDocument : PrintDocument
{
    internal const int CharactersPerLine = 32;

    /// 75 mm, in hundredths of an inch.
    internal const int RollWidthHundredths = 295;

    /// The same inset both sides, about 1 mm, so the type fills the roll.
    internal const int MarginHundredths = 4;

    /// Where every amount ends: three columns short of the edge, so "NV"
    /// fits after an item's amount and the totals line up under it.
    internal const int AmountEnd = CharactersPerLine - 3;

    /// <summary>
    /// The heads of the item columns, and how many and at what each under
    /// them: the quantity under QTY, the price ending under Unit Price.
    /// The amount follows, three columns short of the edge. Thirty-two
    /// across on the roll as drawn; forty-two, as the pharmacy's previous
    /// slips were, as text in the printer's own type (AsText).
    /// </summary>
    private static (string Heads, string Figures) ItemColumns(int columns) =>
        columns == SlipLayout.Width
            ? ("Code   QTY      Unit Price      Amount Inc", "{0,10} @{1,14}")
            : ("Code  QTY  Unit Price Amount Inc", "{0,7} @{1,12}");

    private readonly SaleDto _sale;
    private readonly ReceiptHeaderDto _header;
    private int _next;

    public ReceiptDocument(SaleDto sale, ReceiptHeaderDto header)
    {
        _sale = sale;
        _header = header;

        ApplyRoll(DefaultPageSettings, 24);
    }

    /// <summary>
    /// Puts the 75 mm roll back, as long as this slip, after the printer
    /// is chosen. Choosing the printer resets the page to the driver's
    /// own paper, and that paper is a short form: the slip then stops
    /// part way down and the printer waits for its feed button.
    /// </summary>
    internal void FitRoll()
    {
        var lines = Rows().Count;
        ApplyRoll(DefaultPageSettings, lines);

        try
        {
            ApplyRoll(PrinterSettings.DefaultPageSettings, lines);
        }
        catch (InvalidPrinterException)
        {
        }
    }

    /// <summary>
    /// How tall the page is for <paramref name="lines"/> of type, in
    /// hundredths of an inch, with a feed after the last line so it
    /// clears the tear bar.
    /// </summary>
    internal static int RollHeight(int lines)
    {
        var height = 24 + Math.Max(1, lines) * 24 + 140;

        return Math.Clamp(height, 220, 8000);
    }

    /// <summary>
    /// The roll as a receipt prints on it. Public so the Printer Setup
    /// test page goes out on the same paper as the receipts.
    /// </summary>
    public static void ApplyRoll(PageSettings page, int lines)
    {
        ArgumentNullException.ThrowIfNull(page);

        page.Landscape = false;
        page.Margins = new Margins(MarginHundredths, MarginHundredths, 8, 8);
        page.PaperSize = new PaperSize("Roll 75 mm", RollWidthHundredths,
            RollHeight(lines))
        {
            RawKind = 256
        };
    }

    protected override void OnQueryPageSettings(QueryPageSettingsEventArgs e)
    {
        base.OnQueryPageSettings(e);
        ApplyRoll(e.PageSettings, Math.Max(1, Rows().Count - _next));
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

        e.HasMorePages = RenderPage(e.Graphics!, FixedPitch.Area(e));
    }

    /// <summary>
    /// The largest type at which a full line still fits the roll.
    /// </summary>
    private static float Fitted(Graphics g, float width)
    {
        var size = FixedPitch.Size(g, CharactersPerLine, width, 14f);

        using var probe = new Font(FixedPitch.Family, size);
        using var format = FixedPitch.Format();
        var across = g.MeasureString(new string('0', CharactersPerLine),
                                     probe, PointF.Empty, format).Width;

        if (across > width && across > 0)
            size *= width / across * 0.98f;

        return Math.Max(6f, size);
    }

    /// <summary>
    /// The slip, top to bottom, as it is printed. Public so a test can
    /// read what the customer will be handed without a printer.
    /// </summary>
    public IReadOnlyList<ReceiptRow> Rows() => Rows(CharactersPerLine);

    /// <summary>The slip laid out <paramref name="columns"/> across.</summary>
    private List<ReceiptRow> Rows(int columns)
    {
        var culture = CultureInfo.CurrentCulture;
        var rows = new List<ReceiptRow>();
        var currency = (_header.CurrencyCode ?? "").Trim();
        var amountEnd = columns - 3;
        var (heads, itemFigures) = ItemColumns(columns);

        // The slip is one block of columns in the middle of the roll.
        // A line starts at the block's left edge, so the amounts line up
        // under each other; centring each line on its own moved every
        // amount by half the difference in the lines' lengths.
        void Line(string text, ReceiptStyle style = ReceiptStyle.Body) =>
            rows.Add(new ReceiptRow(text, style, Centred: false));

        void Centre(string text, ReceiptStyle style = ReceiptStyle.Body)
        {
            foreach (var piece in Wrap(text, columns, columns))
                rows.Add(new ReceiptRow(piece, style, Centred: true));
        }

        void Rule() => Line(new string('-', columns));

        // The value wraps under itself rather than running off the roll:
        // on forty columns a longer patient's name pushed their file
        // number off the edge.
        void Field(string lead, string value)
        {
            var room = columns - lead.Length;
            var pieces = Wrap(value, room, room);

            Line(lead + pieces[0]);

            foreach (var more in pieces.Skip(1))
                Line(new string(' ', lead.Length) + more);
        }

        void Amount(string label, decimal value)
        {
            foreach (var row in AmountLines(label, value, culture, amountEnd))
                Line(row);
        }

        // Who sold it: the name and the address, the VAT number a tax
        // invoice cannot be one without (specification Section 6.10), and
        // the numbers to ring about what is on the slip.
        // What the administrator chose to print (Receipt Template).
        var show = _header.Template ?? ReceiptTemplate.Default;

        if (show.TradingName) Centre(_header.TradingName, ReceiptStyle.Title);

        // One printed line per address line: a multi-line address drawn
        // in one call overprinted whatever came under it.
        if (show.Address)
            foreach (var addressLine in (_header.Address ?? "")
                         .Split('\n')
                         .Select(part => part.Trim())
                         .Where(part => part.Length > 0))
                Centre(addressLine);

        // A pharmacy not yet registered for VAT prints no VAT number
        // rather than a bare label.
        if (show.VatNumber && !string.IsNullOrWhiteSpace(_header.VatNumber))
            Centre($"VAT No: {_header.VatNumber.Trim()}");

        if (show.Telephone && !string.IsNullOrWhiteSpace(_header.Telephone))
            Centre($"Tel: {_header.Telephone.Trim()}");

        if (show.WhatsApp && !string.IsNullOrWhiteSpace(_header.WhatsApp))
            Centre($"WhatsApp: {_header.WhatsApp.Trim()}");

        Rule();

        if (show.DocumentTitle)
        {
            Centre("Tax Invoice");
            Centre(Kind());
            Rule();
        }

        // Never optional: the copy mark is what stops a reprint being
        // passed off as the original, and without the number a slip
        // cannot be found again.
        if (_header.IsCopy && show.CopyMark) Centre(CopyMark, ReceiptStyle.Bold);

        Field("Trans No: ", _sale.DocumentNumber);
        if (show.Operator) Field("Till Operator : ", _header.OperatorName);
        if (show.TillNumber) Field("Till Number : ", Till());

        // When, which a tax invoice has to say.
        if (show.Date)
            Line(string.Format(culture, "Date : {0:dd/MM/yyyy HH:mm}",
                _header.PrintedAtLocal));

        // Whose it is. The account a sale went on, and the patient and
        // script a collection paid for, were on no receipt at all
        // (ADM-DEF-09, ADM-OBS-15).
        var who = _sale.Particulars;

        if (show.AccountAndPatient)
        {
            if (who?.AccountNumber is string account)
                Field("Account : ", $"{account} {who.AccountName}".Trim());

            if (who?.PatientName is string patient)
                Field("Patient : ", who.FileNumber is null
                    ? patient
                    : $"{patient} ({who.FileNumber})");

            if (who?.ScriptNumber is string script)
                Field("REF: Script No-", script);
        }

        Rule();

        if (show.ColumnHeadings)
        {
            Line(heads);
            Rule();
        }

        var sold = _sale.Lines.Where(x => !x.IsVoided).ToList();
        var printed = Printed(sold, who?.ScriptNumber);

        foreach (var l in printed)
        {
            // The code on a line of its own (a script charge has none),
            // the whole name under it, over as many lines as it takes.
            var code = (l.ProductCode ?? "").Trim();
            if (show.ProductCodes && code.Length > 0) Line(code);

            // A collected script is "Script Charge: <number>", never what
            // was in it, and it prints whatever the template says about
            // product names: it names no product, and it is the only line
            // that says what the money was for.
            if (show.ProductNames || l.IsScript)
                foreach (var piece in Wrap(l.Description, columns, columns))
                    Line(piece);

            // Then how many, at what each, and what the line came to after
            // any discount, so the amounts add up to what was paid. An
            // amount too long to sit beside the price goes under it.
            var figures = show.QuantityAndUnitPrice
                ? string.Format(culture, itemFigures,
                    l.Quantity.ToString("0.###", culture),
                    l.UnitPrice.ToString("N2", culture))
                : "";
            var amount = show.LineAmounts
                ? l.LineTotal.ToString("N2", culture)
                  + (l.VatRatePercent == 0m ? " NV" : "")
                : "";
            var end = amountEnd + (l.VatRatePercent == 0m ? 3 : 0);

            if (figures.Length == 0 && amount.Length == 0)
            {
                // Neither chosen: the item is its code and name only.
            }
            else if (figures.Length < end - amount.Length)
            {
                Line(figures.PadRight(end - amount.Length) + amount);
            }
            else
            {
                Line(figures);
                Line(amount.PadLeft(end));
            }
        }

        Rule();

        // The sum of the amounts above. The sale's own subtotal is before
        // its discounts, which the amounts are not, so a discounted slip
        // did not add up.
        if (show.SubTotal) Amount("Sub Total", printed.Sum(l => l.LineTotal));

        if (show.Vat)
            foreach (var (label, vat) in VatLines(sold, currency, culture))
                Amount(label, vat);

        if (show.Total) Amount(Labelled("TOTAL", currency) + ":", _sale.Totals.Total);
        if (show.SubTotal || show.Vat || show.Total) Rule();

        // What was handed over for each tender. A payment is recorded at
        // what stayed in the drawer, and the change came out of the cash,
        // so the cash line adds it back: 20.00 handed over, not 14.90.
        var change = _sale.Totals.Change;

        if (show.Tenders)
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

        // Every sale paid in cash says what change was handed back, 0.00
        // where the cash was exact: a slip with no change line left the
        // customer to ask whether any was due. Other tenders give none.
        var paidCash = (_sale.Payments ?? Array.Empty<PaymentDto>())
            .Any(p => p.TenderType == "Cash");

        if (show.Change && (_sale.Totals.Change > 0m || paidCash))
            Amount("Change" + (currency.Length > 0 ? " " + currency : ""),
                   _sale.Totals.Change);

        if (show.Tenders || show.Change) Rule();

        // The patient's portion is what was paid here; the scheme's is
        // claimed. Both, so the patient can see the script was not
        // charged to them in full.
        if (show.MedicalAidShare && who?.SchemeShare is decimal share)
        {
            Amount($"Claimed from {who.SchemeName ?? "medical aid"}", share);
            Amount("Your portion", _sale.Totals.Total);
        }

        // Somebody signs for a sale put on their account.
        if (show.AccountSignature
            && _sale.Payments?.Any(p => p.TenderType == "Account") == true)
        {
            Line("");
            Line("Signed ".PadRight(columns, '_'));
        }

        if (_header.IsCopy && show.CopyMark) Centre(CopyMark, ReceiptStyle.Bold);

        // Last line of text, after the totals and the cash. The page is
        // taller than the type, so this is not left on a second page the
        // printer waits to be fed. Whatever the administrator wrote.
        if (show.Footer && !string.IsNullOrWhiteSpace(show.FooterText))
            foreach (var footerLine in show.FooterText.Split('\n')
                         .Select(part => part.Trim())
                         .Where(part => part.Length > 0))
                Centre(footerLine);

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
    internal bool RenderPage(Graphics g, Rectangle margins) =>
        Draw(g, margins, Rows(), ref _next);

    /// <summary>
    /// Draws slip rows from <paramref name="next"/> on as one block of
    /// thirty-two columns in the roll's largest type, and says whether
    /// there is more. The receipt and the cash-up slip both print here.
    /// </summary>
    internal static bool Draw(Graphics g, Rectangle margins,
                              IReadOnlyList<ReceiptRow> rows, ref int next)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(rows);

        float y = margins.Top;

        using var centred = FixedPitch.Format();
        centred.Alignment = StringAlignment.Center;

        // As large as the 75 mm roll will hold, so a full line fills it
        // and a line of 32 characters still ends inside the paper.
        var size = Fitted(g, margins.Width);
        using var body = new Font(FixedPitch.Family, size);
        using var bold = new Font(FixedPitch.Family, size, FontStyle.Bold);
        using var exact = FixedPitch.Format();

        // The 32 columns as one block, centred on what the printer reaches.
        var across = Math.Min(margins.Width, g.MeasureString(
            new string('0', CharactersPerLine), body, PointF.Empty, exact).Width);
        var block = new RectangleF(margins.Left + (margins.Width - across) / 2f,
                                   margins.Top, across, margins.Height);

        while (next < rows.Count)
        {
            var row = rows[next];
            var font = row.Style == ReceiptStyle.Body ? body : bold;

            // Centred rows wrap within the roll: a long trading name in
            // the large type would otherwise run off both edges.
            var height = row.Centred
                ? Math.Max(g.MeasureString(row.Text, font, (int)block.Width).Height,
                           font.GetHeight(g))
                : font.GetHeight(g);

            // A row that will not fit goes to the next page - unless
            // nothing is on this one yet, or it would never be printed.
            // The foot, including Thank you, stays on this page: a second
            // page is what the receipt printer holds until its button
            // is pressed, and the foot never came out.
            var remaining = rows.Count - next;
            if (y > margins.Top && y + height > margins.Bottom && remaining > 6)
                return true;

            if (row.Centred)
                g.DrawString(row.Text, font, Brushes.Black,
                    new RectangleF(block.Left, y, block.Width, height),
                    centred);
            else
                g.DrawString(row.Text, font, Brushes.Black, block.Left, y,
                             exact);

            y += height;
            next++;
        }

        return false;
    }

    /// <summary>
    /// The slip as ESC/POS text, forty-two across, for the receipt printer
    /// to print in its own type: what a printer on Windows' "Generic / Text
    /// Only" driver is sent, which is how the pharmacy's previous system
    /// had the till printer set up.
    /// </summary>
    /// <remarks>
    /// Drawn, the slip went through that driver's own grid of characters
    /// (photographed 22 October 2026): it came out over little more than
    /// half the roll, its last three columns lost, and without its last
    /// line - the printer prints a line when its line feed comes, and the
    /// driver sent none after Thank you. As text, every line is centred on
    /// the roll and ends in a line feed, and the slip is fed past the tear
    /// bar and cut.
    /// </remarks>
    internal byte[] AsText() => AsText(Rows(SlipLayout.Width));

    /// <summary>
    /// Rows no wider than SlipLayout.Width as ESC/POS text: centred rows
    /// centred on their own, the rest as one centred block. The till-roll
    /// documents go to a text-only printer the same way.
    /// </summary>
    internal static byte[] AsText(IEnumerable<ReceiptRow> rows)
    {
        var text = new List<byte>(Initialise);

        foreach (var row in rows)
        {
            var bold = row.Style != ReceiptStyle.Body;
            if (bold) text.AddRange(BoldOn);

            // Every left-aligned line the full width, so they centre as
            // one block and the amounts stay under each other.
            text.AddRange(Encoding.ASCII.GetBytes(row.Centred
                ? row.Text
                : row.Text.PadRight(SlipLayout.Width)));

            if (bold) text.AddRange(BoldOff);
            text.Add(LineFeed);
        }

        text.AddRange(FeedAndCut);
        return text.ToArray();
    }

    /// ESC @, then ESC a 1: start clean, and centre every line.
    private static readonly byte[] Initialise = { 0x1B, 0x40, 0x1B, 0x61, 0x01 };

    private static readonly byte[] BoldOn = { 0x1B, 0x45, 0x01 };
    private static readonly byte[] BoldOff = { 0x1B, 0x45, 0x00 };
    private const byte LineFeed = 0x0A;

    /// ESC d 4, then GS V 1: four lines on, which takes the last line past
    /// the tear bar, and a partial cut, which a printer with no cutter
    /// ignores.
    private static readonly byte[] FeedAndCut = { 0x1B, 0x64, 0x04, 0x1D, 0x56, 0x01 };

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

    /// <summary>
    /// A label and its amount, the amount ending at AmountEnd. A label too
    /// long to sit beside it wraps, the amount on its last line: cut, a
    /// scheme's name lost the end that said which scheme.
    /// </summary>
    private static IEnumerable<string> AmountLines(string label, decimal value,
                                                   CultureInfo culture, int end)
    {
        var amount = value.ToString("N2", culture);
        var room = end - amount.Length - 1;
        var pieces = Wrap(label, room, room);

        foreach (var piece in pieces.SkipLast(1)) yield return piece;

        yield return pieces[^1].PadRight(room + 1) + amount;
    }

    /// <summary>
    /// Splits text at spaces: the first piece fits the first width, the
    /// rest fit the second. A word too long for either is split where it
    /// has to be.
    /// </summary>
    internal static IReadOnlyList<string> Wrap(string value, int first,
                                               int rest) =>
        SlipLayout.Wrap(value, first, rest);

    /// What a reprint says, above the sale and again at the foot.
    internal const string CopyMark = "*** COPY - REPRINTED ***";

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
