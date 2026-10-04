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
/// Laid out as the pharmacy's tax invoice slip: the pharmacy's name,
/// address and numbers; the customer's own registrations, which a
/// customer reclaiming the VAT writes in at the counter; the invoice and
/// who rang it up; a code-and-description line over a
/// quantity-price-discount-total line for each item; then the totals,
/// the tenders and the change, in the trading currency. No logo: a
/// picture costs roll, time and toner on every sale, and the name says
/// who the pharmacy is.
/// </remarks>
public sealed class ReceiptDocument : PrintDocument
{
    private const int CharactersPerLine = 42;

    /// Where every amount ends: the totals line up under the Total
    /// column of the items, which is the right edge of the roll.
    private const int AmountEnd = CharactersPerLine;

    /// The code column, which the description starts after and the
    /// item's figures sit under.
    private const int CodeWidth = 10;

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
            Line($"{label,-10}: {value}");

        void Amount(string label, decimal value,
                    ReceiptStyle style = ReceiptStyle.Body) =>
            Line(AmountLine(label, value, culture), style);

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

        // The customer's own registrations. Printed as empty fields
        // rather than left off: a customer reclaiming the VAT on a
        // purchase writes them onto the invoice at the counter, and an
        // invoice with nowhere to write them is no use to them.
        Line("Customer TIN    :");
        Line("Customer VAT No :");

        Rule();

        // The heading says what the paper is and how it was paid, which
        // is the first thing anybody looks for on it.
        Centre(Heading(), ReceiptStyle.Bold);

        Field("Cashier", _header.OperatorName);
        Field("Date&Time", string.Format(culture, "{0:dd/MM/yyyy HH:mm}  "
            + "Till No: {1}", _header.PrintedAtLocal, Till()));
        Field("Inv RefNo", _sale.DocumentNumber);

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

        // How it was paid, and how much was put down. A receipt that
        // does not say whether it was cash or card cannot settle the
        // commonest query at the counter.
        Field("Pay Method", string.Format(culture, "{0}  Tendered: {1:N2}",
            PayMethod(), Tendered()));

        if (currency.Length > 0)
            Line(string.Format(culture, "Tendered: {0} = {1:N2}", currency,
                Tendered()));

        Centre("CUSTOMER COPY");
        Centre(_header.IsCopy ? CopyMark : "ORIGINAL",
               _header.IsCopy ? ReceiptStyle.Bold : ReceiptStyle.Body);

        Rule();
        Centre("Description");
        Line(string.Format(culture, "{0,-10}{1,5}{2,10}{3,9}{4,8}",
            "Code", "Qty", "UnitPrice", "DiscPerc", "Total"));
        Rule();

        var sold = _sale.Lines.Where(x => !x.IsVoided).ToList();

        foreach (var l in sold)
        {
            // The code and then the whole description, over as many
            // lines as it takes. It was cut short once, which took
            // "60s x 30" off every collected script - the only place the
            // receipt says how much of the medicine was paid for.
            var described = l.VatRatePercent == 0m
                ? l.Description + " NV"
                : l.Description;

            var wrapped = Wrap(described, CharactersPerLine - CodeWidth,
                               CharactersPerLine - CodeWidth);

            var code = (l.ProductCode ?? "").Trim();
            var indent = new string(' ', CodeWidth);

            // A code longer than its column takes a line of its own: it
            // used to push the description along instead, off the edge of
            // the roll and out of the receipt.
            if (code.Length < CodeWidth)
            {
                Line(code.PadRight(CodeWidth) + wrapped[0]);

                foreach (var more in wrapped.Skip(1)) Line(indent + more);
            }
            else
            {
                Line(code);

                foreach (var piece in wrapped) Line(indent + piece);
            }

            Line(Figures(l, culture));
        }

        Rule();
        Amount("SubTotal", _sale.Totals.Subtotal);

        if (_sale.Totals.Discount > 0m)
            Amount("Discount", _sale.Totals.Discount);

        foreach (var (label, vat) in VatLines(sold, currency, culture))
            Amount(label, vat);

        Rule();
        Amount("Total(Incl VAT)", _sale.Totals.Total, ReceiptStyle.Bold);

        foreach (var payment in _sale.Payments ?? Array.Empty<PaymentDto>())
            Amount(TenderLabel(payment.TenderType)
                   + (currency.Length > 0 ? " " + currency : ""),
                   payment.Amount);

        // What was put down against what was owed, in words rather than
        // as another right-aligned figure: it is the line the customer
        // checks before they walk away from the counter.
        if (Tendered() > 0m)
        {
            Line("");
            Line(string.Format(culture,
                "Change for Tendered {0:N2} = {1:N2}", Tendered(),
                _sale.Totals.Change));
        }

        Line("");
        Line(string.Format(culture, "Total Items: {0:N2}",
            sold.Sum(l => l.Quantity)));

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
    /// What the paper is, and how it was paid, in brackets after it.
    /// </summary>
    private string Heading() => $"TAX INVOICE ({PayMethod()})";

    /// <summary>
    /// How the sale was paid, as the heading and the Pay Method line
    /// name it. A sale settled two ways names both.
    /// </summary>
    private string PayMethod()
    {
        var tenders = (_sale.Payments ?? Array.Empty<PaymentDto>())
            .Select(p => TenderLabel(p.TenderType).ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return tenders.Count == 0 ? "CASH" : string.Join("/", tenders);
    }

    /// <summary>
    /// What was put down. The tendered figure where the till recorded
    /// one, and otherwise what was paid: a card is not "tendered" at the
    /// till, and a slip reading nothing tendered against a paid total
    /// reads as though the customer walked out without paying.
    /// </summary>
    private decimal Tendered() =>
        _sale.Totals.Tendered > 0m
            ? _sale.Totals.Tendered
            : (_sale.Payments ?? Array.Empty<PaymentDto>())
                .Sum(p => p.Amount);

    private string Till() =>
        string.IsNullOrWhiteSpace(_header.TillNumber)
            ? _header.BranchAndTill
            : _header.TillNumber.Trim();

    /// <summary>
    /// The figures of an item, under its code and description: how many,
    /// at what each, less what discount, for what.
    /// </summary>
    private static string Figures(SaleLineDto line, CultureInfo culture) =>
        string.Format(culture, "{0,-10}{1,5}{2,10}{3,9}{4,8}", "",
            line.Quantity.ToString("0.###", culture),
            line.UnitPrice.ToString("N2", culture),
            DiscountPercent(line).ToString("N2", culture),
            line.LineTotal.ToString("N2", culture));

    /// <summary>
    /// The discount as the column states it: what was taken off, against
    /// what the line would have come to without it.
    /// </summary>
    private static decimal DiscountPercent(SaleLineDto line)
    {
        var gross = line.Quantity * line.UnitPrice;

        return gross == 0m
            ? 0m
            : Math.Round(line.Discount / gross * 100m, 2,
                         MidpointRounding.AwayFromZero);
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
        var label = Labelled("Vat", currency);

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
