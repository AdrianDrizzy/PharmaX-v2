using System.Drawing.Printing;
using System.Globalization;
using Microsoft.Extensions.Logging;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Dispensing.Dtos;
using PharmaX.Application.Sales;
using PharmaX.Application.Sales.Dtos;

namespace PharmaX.Adapters.Printing;

public sealed class ReceiptPrinter : IReceiptPrinter
{
    private readonly ILogger<ReceiptPrinter> _log;
    private readonly IWorkstationSettings _settings;
    private readonly IPharmacySettings? _pharmacy;

    public ReceiptPrinter(ILogger<ReceiptPrinter> log,
                          IWorkstationSettings settings,
                          IPharmacySettings pharmacy)
        : this(log, settings) => _pharmacy = pharmacy;

    public ReceiptPrinter(ILogger<ReceiptPrinter> log,
                          IWorkstationSettings settings)
    {
        _log = log;
        _settings = settings;
    }

    /// <summary>
    /// The administrator's Receipt Template, pharmacy-wide. A template
    /// that cannot be read prints the full slip rather than none.
    /// </summary>
    private async Task<ReceiptTemplate> TemplateAsync(CancellationToken ct)
    {
        if (_pharmacy is null) return ReceiptTemplate.Default;

        try
        {
            return ReceiptTemplate.Parse(
                await _pharmacy.GetAsync(ReceiptTemplate.SettingKey, ct).ConfigureAwait(false));
        }
#pragma warning disable CA1031 // A slip must still print.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _log.LogWarning(ex, "Could not read the receipt template; printing the full slip.");
            return ReceiptTemplate.Default;
        }
    }

    public bool IsAvailable => PrinterSettings.InstalledPrinters.Count > 0;

    /// <remarks>
    /// Never throws: the sale is already committed by the time this is
    /// called, and a printer fault must degrade rather than stop the next
    /// customer (POS-014). What it returns says honestly what happened.
    /// </remarks>
    public async Task<PrintResult> PrintAsync(SaleDto sale,
        ReceiptHeaderDto header, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sale);
        ArgumentNullException.ThrowIfNull(header);

        if (header.Template is null)
            header = header with { Template = await TemplateAsync(ct).ConfigureAwait(false) };

        using var doc = new ReceiptDocument(sale, header);

        return await Spooler.PrintAsync(doc, _settings,
            WorkstationSettingKey.ReceiptPrinter, "receipt",
            $"Receipt {sale.DocumentNumber}", _log, ct).ConfigureAwait(false);
    }
}

/// <summary>
/// Prints the plain-text documents - X-reads, Z-reads, quotations, goods
/// received notes, purchase orders - on the till's receipt printer.
/// </summary>
public sealed class DocumentPrinter : IDocumentPrinter
{
    private readonly ILogger<DocumentPrinter> _log;
    private readonly IWorkstationSettings _settings;

    public DocumentPrinter(ILogger<DocumentPrinter> log,
                           IWorkstationSettings settings)
    {
        _log = log;
        _settings = settings;
    }

    public async Task<PrintResult> PrintAsync(PrintableDocument document,
        ReceiptHeaderDto header, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        // Thirty-two across prints as the receipt does (the cash-up slip).
        using PrintDocument doc = document.Columns == ReceiptDocument.CharactersPerLine
            ? new SlipDocument(document, header)
            : new PlainDocument(document, header);

        return await Spooler.PrintAsync(doc, _settings,
            WorkstationSettingKey.ReceiptPrinter, "receipt",
            document.Title, _log, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The document laid out on the roll as PrintAsync prints it, row for
    /// row, for a preview on screen before it goes to the printer.
    /// </summary>
    /// <returns>The rows, and how many characters across they are laid out in.</returns>
    public static (IReadOnlyList<ReceiptRow> Rows, int Columns) Layout(
        PrintableDocument document, ReceiptHeaderDto header)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(header);

        if (document.Columns == ReceiptDocument.CharactersPerLine)
        {
            using var slip = new SlipDocument(document, header);
            return (slip.Rows(), ReceiptDocument.CharactersPerLine);
        }

        using var plain = new PlainDocument(document, header);
        return (plain.Rows(), SlipLayout.Width);
    }
}

/// <summary>
/// The one route every document takes to the spooler: choose the printer,
/// check it, send the job, and say what happened.
/// </summary>
internal static class Spooler
{
    /// The driver Windows names "Generic / Text Only": it prints what it
    /// is sent as characters, in the printer's own type.
    private const string TextOnlyDriver = "Generic / Text Only";

    /// <param name="orDefault">
    /// Whether the Windows default will do when no printer is chosen for
    /// this purpose (PrinterResolver.Resolve).
    /// </param>
    public static async Task<PrintResult> PrintAsync(PrintDocument doc,
        IWorkstationSettings settings, string settingKey, string purpose,
        string jobName, ILogger log, CancellationToken ct,
        bool orDefault = true)
    {
        string? chosen;

        try
        {
            // Awaited, not waited on. This read used to be blocked on
            // from the till's own thread, and its continuation needed
            // that same thread to finish - so the sale finalised, the
            // hourglass came up, and the window never repainted again.
            chosen = await settings.GetAsync(settingKey, ct)
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Deliberately broad: see below.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // Being unable to read the choice is not a reason not to
            // print: the Windows default is still worth trying.
            log.LogWarning(ex, "Could not read the {Purpose} printer setting.",
                purpose);
            chosen = null;
        }

        var choice = PrinterResolver.Resolve(chosen, purpose, orDefault);

        if (!choice.CanPrint)
        {
            log.LogWarning("{Job} not printed: {Problem}", jobName,
                choice.Problem);

            return choice.Printer is null
                ? PrintResult.NoPrinter(choice.Problem!)
                : PrintResult.Failed(choice.Printer, choice.Problem!);
        }

        var printer = choice.Printer!;
        Action print;

        // A till printer on Windows' "Generic / Text Only" driver - as the
        // pharmacy's previous system set it up - is sent the receipt and
        // the till-roll documents as text, in the printer's own type.
        // Drawn, the slip came out on half the roll, cut short at the side
        // and at the foot.
        Func<byte[]>? asText = doc switch
        {
            ReceiptDocument receipt => receipt.AsText,
            PlainDocument plain => plain.AsText,
            SlipDocument slip => slip.AsText,
            _ => null
        };

        if (asText is not null
            && string.Equals(Devices.RawPrinter.DriverOf(printer), TextOnlyDriver,
                             StringComparison.OrdinalIgnoreCase))
        {
            print = () => Devices.RawPrinter.Send(printer, asText(), jobName);
        }
        else
        {
            doc.DocumentName = jobName;
            doc.PrinterSettings.PrinterName = printer;

            // The driver's own paper is put back when the printer is named.
            // A receipt on that paper is a short form and stops mid-slip.
            switch (doc)
            {
                case ReceiptDocument receipt:
                    receipt.FitRoll();
                    break;
                case PlainDocument plain:
                    plain.FitRoll();
                    break;
                case SlipDocument slip:
                    slip.FitRoll();
                    break;
            }

            if (!doc.PrinterSettings.IsValid)
            {
                var problem = $"Windows could not open the {purpose} printer "
                              + $"'{printer}'.";
                log.LogWarning("{Job} not printed: {Problem}", jobName, problem);
                return PrintResult.Failed(printer, problem);
            }

            print = doc.Print;
        }

        try
        {
            // Off the caller's thread. Handing a document to the spooler
            // takes as long as the driver takes, and a driver that stops
            // to ask where to save a PDF takes as long as the operator
            // does - neither is a reason for the screen to stop redrawing.
            await Task.Run(print, ct).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Deliberately broad: the driver stack can throw almost anything.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            log.LogWarning(ex, "{Job} failed on {Printer}", jobName,
                choice.Printer);

            return PrintResult.Failed(choice.Printer,
                $"The {purpose} printer '{choice.Printer}' refused the job: "
                + ex.Message);
        }

        if (choice.Note is not null)
            log.LogWarning("{Job}: {Note}", jobName, choice.Note);

        return PrintResult.Ok(choice.Printer!, choice.Note);
    }
}

/// <summary>
/// A plain-text document on the receipt roll: the pharmacy's header, a
/// title, and the lines, over as many pages as they take.
/// </summary>
public sealed class PlainDocument : PrintDocument
{
    private readonly PrintableDocument _document;
    private readonly ReceiptHeaderDto _header;
    private readonly Font _title = new("Consolas", 11f, FontStyle.Bold);
    private int _next;
    private bool _headed;

    public PlainDocument(PrintableDocument document, ReceiptHeaderDto header)
    {
        _document = document;
        _header = header;

        ReceiptDocument.ApplyRoll(DefaultPageSettings, 40);
    }

    /// <summary>
    /// The same 75 mm roll as a receipt, as long as this document.
    /// </summary>
    internal void FitRoll()
    {
        var lines = 12 + _document.Lines.Count;
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
        ReceiptDocument.ApplyRoll(e.PageSettings,
            Math.Max(1, 12 + _document.Lines.Count - _next));
    }

    protected override void OnBeginPrint(PrintEventArgs e)
    {
        base.OnBeginPrint(e);
        _next = 0;
        _headed = false;
        FitRoll();
    }

    protected override void OnPrintPage(PrintPageEventArgs e)
    {
        base.OnPrintPage(e);
        ArgumentNullException.ThrowIfNull(e);

        e.HasMorePages = RenderPage(e.Graphics!, FixedPitch.Area(e));
    }

    /// <summary>
    /// Draws one page and says whether there is more. Separate from
    /// OnPrintPage so a test can render it to a bitmap and read it.
    /// </summary>
    internal bool RenderPage(Graphics g, Rectangle bounds)
    {
        ArgumentNullException.ThrowIfNull(g);

        float y = bounds.Top;

        // The documents are laid out in SlipLayout.Width columns; the
        // type is sized so that many fit whatever the roll is.
        using var body = new Font(FixedPitch.Family,
            FixedPitch.Size(g, SlipLayout.Width, bounds.Width, 8f));
        using var exact = FixedPitch.Format();

        // Wrapped to the roll. Lines were drawn as they came, so a line
        // wider than the paper - a quotation's notes, its own footer -
        // ran off the edge, which a till printer simply loses.
        float Height(string text, Font font) =>
            Math.Max(g.MeasureString(text, font, bounds.Width, exact).Height,
                     font.GetHeight(g));

        void Line(string text, Font font)
        {
            var height = Height(text, font);

            g.DrawString(text, font, Brushes.Black,
                new RectangleF(bounds.Left, y, bounds.Width, height), exact);
            y += height;
        }

        // Once, on the first page - not on every page that has yet to
        // reach the first line, which a header filling a page would be.
        if (!_headed)
        {
            _headed = true;

            foreach (var row in Head())
                Line(row.Text, row.Style == ReceiptStyle.Body ? body : _title);

            if (_next == 0) _next = FirstLine();
        }

        while (_next < _document.Lines.Count)
        {
            // A line that will not fit goes to the next page - unless
            // nothing is on this one yet, or it would never be printed.
            if (y > bounds.Top
                && y + Height(_document.Lines[_next], body) > bounds.Bottom)
                return true;

            Line(_document.Lines[_next++], body);
        }

        return false;
    }

    /// <summary>
    /// The document as ESC/POS text, for a till printer on Windows'
    /// "Generic / Text Only" driver: drawn through it, it came out on half
    /// the roll, as the receipt did (ReceiptDocument.AsText). A line wider
    /// than the roll wraps; the rest go as they are, their spacing kept.
    /// </summary>
    internal byte[] AsText() => ReceiptDocument.AsText(Rows());

    /// <summary>
    /// The document top to bottom as the text printer is sent it: the head,
    /// then the lines, each wider than the roll wrapped.
    /// </summary>
    public IReadOnlyList<ReceiptRow> Rows() =>
        Head()
            .Concat(_document.Lines.Skip(FirstLine())
                .Select(line => new ReceiptRow(line, ReceiptStyle.Body, Centred: false)))
            .SelectMany(row => row.Text.Length <= SlipLayout.Width
                ? new[] { row }
                : SlipLayout.Wrap(row.Text, SlipLayout.Width, SlipLayout.Width)
                    .Select(piece => row with { Text = piece }))
            .ToList();

    /// <summary>
    /// The pharmacy, who printed it and when, and the title, at the head
    /// of the first page.
    /// </summary>
    private IEnumerable<ReceiptRow> Head()
    {
        static ReceiptRow Body(string text) => new(text, ReceiptStyle.Body, Centred: false);
        var rule = Body(new string('-', SlipLayout.Width));

        yield return new ReceiptRow(_header.TradingName, ReceiptStyle.Title, Centred: false);

        foreach (var part in (_header.Address ?? "").Split('\n')
                     .Select(p => p.Trim()).Where(p => p.Length > 0))
            yield return Body(part);

        if (!string.IsNullOrWhiteSpace(_header.VatNumber))
            yield return Body($"VAT {_header.VatNumber}");

        // A quotation or a goods received note is taken away and asked
        // about later, like the receipt and the label.
        if (!string.IsNullOrWhiteSpace(_header.Telephone))
            yield return Body($"Tel: {_header.Telephone.Trim()}");

        yield return Body($"{_header.BranchAndTill}  {_header.OperatorName}");
        yield return Body(_header.PrintedAtLocal.ToString("dd/MM/yyyy HH:mm",
            CultureInfo.CurrentCulture));
        yield return rule;
        yield return new ReceiptRow(_document.Title, ReceiptStyle.Title, Centred: false);
        yield return rule;
    }

    /// <summary>
    /// Where the document's own lines start. The documents open with their
    /// own title as their first line as well, so it printed twice
    /// (ADM-OBS-07).
    /// </summary>
    private int FirstLine() =>
        _document.Lines.Count > 0
        && string.Equals(_document.Lines[0].Trim(), _document.Title.Trim(),
                         StringComparison.Ordinal)
            ? 1
            : 0;

    protected override void Dispose(bool disposing)
    {
        if (disposing) _title.Dispose();


        base.Dispose(disposing);
    }
}

/// <summary>
/// A dispensing label on the pharmacy's 77 mm by 39 mm stickers, one
/// sticker to a page so that each label starts where its sticker does:
/// at 37 mm a page, every label crept up its sticker until the last line
/// fell off. Forty-two columns at fifteen to the inch, the rows spread
/// down what the printer reaches.
/// </summary>
/// <remarks>
/// A pre-pack or a template sticker, stuck on the printed dispensing bag
/// wherever it lands, is laid out row for row as the pharmacy's own
/// labels are, the first row and the price and date underlined:
/// <code>
/// PARACETAMOL 500MG TAB (20)
/// TAKE 1-2 TABLETS AFTER MEALS ORALLY 3 TIMES
/// DAILY AFTER FOOD WHEN NECESSARY
/// EXPIRY: 30/06/2028          BATCH NO: 2076
/// 0.50                  PACKED ON:19/10/2026
/// Bethrapha Pharmacy, 28 Woodstock Road
/// Thorngrove, Bulawayo
/// </code>
/// The medicine and how many; the directions as they were typed with as
/// many of the warnings as fit two lines run on after them, in capitals;
/// the expiry against the batch, on every label; the price, where there
/// is one, against the date; and the pharmacy with its address. A
/// patient's label is laid out differently (PatientRows). "Keep out of
/// reach of children" is printed on the bag.
/// </remarks>
public sealed class LabelDocument : PrintDocument
{
    /// The characters across the sticker, fifteen to the inch.
    public const int Columns = 42;

    /// The lines down it.
    public const int LineCount = 8;

    /// 77 mm, in hundredths of an inch.
    public const int WidthHundredths = 303;

    /// 39 mm, in hundredths of an inch: the distance from one sticker to
    /// the next on the roll.
    public const int HeightHundredths = 154;

    /// Written where nothing records the expiry - a medicine not kept by
    /// batch - for it to be filled in by hand.
    internal const string NoExpiry = "EXPIRY: __/__/____";

    /// How many rows the directions and warnings have before the type
    /// steps down.
    private const int DirectionLines = 2;

    private readonly LabelDto _label;
    private readonly PharmacyImprint? _pharmacy;
    private readonly int _copies;
    private int _printed;

    /// <param name="pharmacy">
    /// The pharmacy the medicine came from - its name and its address -
    /// which every dispensed label has to carry. The label printed without
    /// it, so a container out of its bag said nothing of where it was
    /// dispensed (ADM-DEF-16).
    /// </param>
    /// <param name="copies">
    /// How many of the same label, one after another on the stock: the
    /// stickers for a run of pre-packs.
    /// </param>
    public LabelDocument(LabelDto label, PharmacyImprint? pharmacy = null,
                         int copies = 1)
    {
        _label = label;
        _pharmacy = pharmacy;
        _copies = Math.Max(1, copies);

        ApplyItemStock(DefaultPageSettings);
    }

    /// <summary>
    /// 77 mm by 39 mm, put back on after the printer is chosen. Choosing
    /// the printer resets the page to the driver's own paper, and a driver
    /// then scales the sticker so one line of type comes out sheared.
    /// </summary>
    internal static void ApplyStock(PageSettings page)
    {
        ArgumentNullException.ThrowIfNull(page);

        page.Landscape = false;
        page.PaperSize = new PaperSize("Label 77x39 mm",
            WidthHundredths, HeightHundredths);
        page.Margins = new Margins(6, 6, 4, 4);
    }

    /// <summary>
    /// The item labels use the whole sticker. The trailer keeps the
    /// inset above, which already fits its address.
    /// </summary>
    private static void ApplyItemStock(PageSettings page)
    {
        ApplyStock(page);
        page.Margins = new Margins(0, 0, 0, 0);
    }

    protected override void OnQueryPageSettings(QueryPageSettingsEventArgs e)
    {
        base.OnQueryPageSettings(e);
        ApplyItemStock(e.PageSettings);
    }

    protected override void OnBeginPrint(PrintEventArgs e)
    {
        base.OnBeginPrint(e);
        ApplyItemStock(DefaultPageSettings);
        _printed = 0;
    }

    protected override void OnPrintPage(PrintPageEventArgs e)
    {
        base.OnPrintPage(e);
        ArgumentNullException.ThrowIfNull(e);

        e.HasMorePages = RenderPage(e.Graphics!, FixedPitch.Area(e));
    }

    /// <summary>
    /// The label row by row, each no wider than the stock, exactly as it
    /// is printed. Public so a test, and the Labels screen's preview, can
    /// read what goes on the container without a printer.
    /// </summary>
    public IReadOnlyList<LabelRow> Rows()
    {
        if (!string.IsNullOrWhiteSpace(_label.PatientName))
            return PatientRows();

        var culture = CultureInfo.CurrentCulture;
        var rows = new List<LabelRow>();

        // What it is and how many: "PARACETAMOL 500MG TAB (20)". No medicine
        // is the quantity on its own, still in brackets: "(20)".
        var name = _label.ProductDescription.Trim();
        var product = name.Length == 0
            ? string.Format(culture, "({0:0.##})", _label.Quantity)
            : string.Format(culture, "{0} ({1:0.##})", name, _label.Quantity);
        Add(rows, product.ToUpperInvariant(), LabelStyle.Product, underlined: true);

        var omit = _label.Omit;

        // How to take it, then straight on to the expiry: no blank row,
        // which pushed the address off the sticker.
        if (Directions() is string directions)
            Add(rows, directions, LabelStyle.Directions);

        // The expiry on every label, a patient's and a pre-pack's alike;
        // where nothing records it, a space to write it in. A template
        // can leave the expiry, the batch, or both off.
        var expiryText = omit?.Expiry == true
            ? ""
            : _label.Expiry is DateOnly expiry
                ? string.Format(culture, "EXPIRY: {0:dd/MM/yyyy}", expiry)
                : NoExpiry;
        var batchText = omit?.Batch == true || string.IsNullOrWhiteSpace(_label.BatchNumber)
            ? ""
            : $"BATCH NO: {_label.BatchNumber.Trim()}";

        if (expiryText.Length > 0 || batchText.Length > 0)
            Spread(rows, expiryText, batchText);

        var priceText = omit?.Price == true || _label.Price is not decimal price
            ? ""
            : price.ToString("N2", culture);
        var packedText = omit?.PackedOn == true
            ? ""
            : string.Format(culture, "PACKED ON:{0:dd/MM/yyyy}", _label.DispensedOn);

        if (priceText.Length > 0 || packedText.Length > 0)
            Spread(rows, priceText, packedText, underlined: true);

        // The pharmacy and its street, then the rest of the address.
        // From here down the type is smaller, so the year and the
        // address stay on the sticker.
        if (_pharmacy is not null && (omit?.Pharmacy != true || omit?.Address != true))
        {
            var address = AddressLines(_pharmacy.Address).ToList();
            var named = omit?.Pharmacy == true
                ? ""
                : (_pharmacy.TradingName ?? "").Trim();
            var street = omit?.Address == true ? null : address.FirstOrDefault();
            var rest = omit?.Address == true
                ? ""
                : string.Join(", ", address.Skip(string.IsNullOrEmpty(street) ? 0 : 1));

            Add(rows, DispensedBy(named, street), LabelStyle.Imprint);
            Add(rows, rest, LabelStyle.Imprint);
        }

        return rows;
    }

    /// <summary>
    /// A patient's label, laid out as the dispensing label the pharmacy
    /// asked to follow (photographed on a bag, 21 October 2026): every
    /// row in capitals and as full as it will go, the quantity, the expiry
    /// and the date against the right edge, and no blank row, so the whole
    /// address is on the sticker.
    /// <code>
    /// LEVONORGESTREL BACKUP 1.5MG                (1)
    /// TAKE AT ONCE USE ONLY AS DIRECTED.
    /// Rx:SC-001-000023                EXP:__/__/____
    /// NOBUHLE MASUKU / CIMAS              21/10/2026
    /// BETHRAPHA PHARMACY, 28 WOODSTOCK ROAD,
    /// THORNGROVE, BULAWAYO, (ALONG LUVEVE ROAD)
    /// </code>
    /// Every row is in the smaller face, the right edge of a full row is
    /// where the label printer stops: on the trailer the year lost its
    /// last digit there. No more than eight rows where the directions
    /// allow it: a ninth is too short for the label printer's own face,
    /// and the whole label came out tiny.
    /// </summary>
    private List<LabelRow> PatientRows()
    {
        var culture = CultureInfo.CurrentCulture;
        var rows = new List<LabelRow>();

        IEnumerable<LabelRow> As(IEnumerable<string> pieces, LabelStyle style) =>
            pieces.Where(piece => piece.Length > 0)
                .Select(piece => new LabelRow(piece, style, Compact: true));

        void Put(IEnumerable<string> pieces, LabelStyle style) =>
            rows.AddRange(As(pieces, style));

        Put(EndingWith(_label.ProductDescription.Trim().ToUpperInvariant(),
                       string.Format(culture, "({0:0.##})", _label.Quantity)),
            LabelStyle.Product);

        // The directions go under the medicine, but are worked out last,
        // from the rows the rest of the label leaves.
        var under = rows.Count;

        // The script, the batch and the expiry: on one row where they fit,
        // else the script above the batch and the expiry. No dispenser's
        // initials (the pharmacy asked, 22 October 2026): they pushed the
        // batch onto a row of its own.
        var rx = $"Rx:{_label.ScriptNumber.Trim()}";
        var batch = string.IsNullOrWhiteSpace(_label.BatchNumber)
            ? ""
            : $"BN:{_label.BatchNumber.Trim()}";
        var expiry = _label.Expiry is DateOnly date
            ? string.Format(culture, "EXP:{0:dd/MM/yyyy}", date)
            : "EXP:__/__/____";

        if (Joined(rx, batch).Length + 1 + expiry.Length <= Columns)
        {
            Put(EndingWith(Joined(rx, batch), expiry), LabelStyle.Detail);
        }
        else
        {
            Put(Wrap(rx), LabelStyle.Detail);
            Put(EndingWith(batch, expiry), LabelStyle.Detail);
        }

        // Whose it is and their medical aid, with the date. The scheme is
        // cut short to stay on the row; the name never is.
        var dated = _label.DispensedOn.ToString("dd/MM/yyyy", culture);
        var who = _label.PatientName.Trim().ToUpperInvariant();
        var room = Columns - dated.Length - 1;

        if (!string.IsNullOrWhiteSpace(_label.SchemeName) && who.Length + 5 < room)
        {
            who = $"{who} / {_label.SchemeName.Trim().ToUpperInvariant()}";
            who = who[..Math.Min(who.Length, room)].TrimEnd();
        }

        Put(EndingWith(who, dated), LabelStyle.Patient);

        // The pharmacy and its address as one run, over as many rows as
        // it takes.
        if (_pharmacy is not null)
            Put(Wrap(DispensedBy(_pharmacy.TradingName ?? "", _pharmacy.Address)
                         .ToUpperInvariant()),
                LabelStyle.Imprint);

        if (Directions(LineCount - rows.Count) is string directions)
            rows.InsertRange(under, As(Wrap(directions), LabelStyle.Directions));

        return rows;
    }

    /// <summary>
    /// How to take it, in capitals, with the warnings the caller gave - or
    /// those that go with the medicine - run on after it, the most
    /// important first. That one goes on while <paramref name="rows"/>
    /// hold it - on a pre-pack always, so no sticker goes out without a
    /// warning; the rest while two lines hold them as well. The
    /// directions themselves are never cut. Null where a template leaves
    /// them off.
    /// </summary>
    private string? Directions(int rows = int.MaxValue)
    {
        var omit = _label.Omit;
        if (omit?.Directions == true) return null;

        var warnings = omit?.Warning == true
            ? Array.Empty<string>()
            : _label.Cautions.Count > 0
                ? _label.Cautions
                : LabelWording.Warnings(_label.ProductDescription);

        var directions = LabelWording.Directions(_label.Directions);
        var first = true;

        foreach (var warning in warnings)
        {
            var cleaned = LabelWording.WithoutShareNotice(warning);
            if (cleaned.Length == 0) continue;

            var longer = $"{directions} {cleaned}".Trim();
            var limit = first ? rows : Math.Min(rows, DirectionLines);
            if (Wrap(longer.ToUpperInvariant()).Count > limit) break;

            directions = longer;
            first = false;
        }

        return directions.ToUpperInvariant();
    }

    /// <summary>
    /// Text wrapped to the label with <paramref name="end"/> against the
    /// right edge of its last row, or on a row of its own where it will
    /// not fit beside it.
    /// </summary>
    private static List<string> EndingWith(string text, string end)
    {
        var pieces = Wrap(text);
        var last = pieces.Count == 0 ? "" : pieces[^1];

        if (pieces.Count > 0 && last.Length + 1 + end.Length > Columns)
        {
            pieces.Add(end.PadLeft(Columns));
            return pieces;
        }

        if (pieces.Count > 0) pieces.RemoveAt(pieces.Count - 1);
        pieces.Add(last.PadRight(Columns - end.Length) + end);
        return pieces;
    }

    private static string Joined(params string[] parts) =>
        string.Join(" ", parts.Where(part => part.Length > 0));

    /// <summary>
    /// A row with one text at its start and the other against its end -
    /// the expiry and the batch - or each on rows of its own where both
    /// will not fit.
    /// </summary>
    private static void Spread(List<LabelRow> rows, string left, string right,
                               bool underlined = false)
    {
        if (left.Length + 1 + right.Length <= Columns)
        {
            rows.Add(new LabelRow(
                (left.PadRight(Columns - right.Length) + right).TrimEnd(),
                LabelStyle.Detail, underlined, Compact: true));
            return;
        }

        Add(rows, left, LabelStyle.Detail, underlined);
        rows.AddRange(Wrap(right).Select(piece =>
            new LabelRow(piece.PadLeft(Columns), LabelStyle.Detail, underlined,
                Compact: true)));
    }

    private static void Add(List<LabelRow> rows, string? text, LabelStyle style,
                            bool underlined = false) =>
        rows.AddRange(Wrap(text).Select(piece => new LabelRow(piece, style, underlined,
            Compact: style is LabelStyle.Detail or LabelStyle.Imprint)));

    /// <summary>
    /// The lines the label carries, top to bottom. Public so a test can
    /// check the content without a printer.
    /// </summary>
    public IReadOnlyList<string> Lines() =>
        Rows().Select(row => row.Text).ToList();

    /// <summary>
    /// The pharmacy as a label names it: trading name, then the address
    /// on one line.
    /// </summary>
    public static string DispensedBy(string tradingName, string? address) =>
        string.Join(", ", new[] { tradingName }
            .Concat(AddressLines(address))
            .Where(part => !string.IsNullOrWhiteSpace(part)));

    private static IEnumerable<string> AddressLines(string? address) =>
        (address ?? "").Split('\n')
            .Select(part => part.Trim())
            .Where(part => part.Length > 0);

    private static List<string> Wrap(string? text) =>
        SlipLayout.Wrap(text, Columns, Columns)
            .Where(piece => piece.Length > 0)
            .ToList();

    /// <summary>
    /// Draws one label and says whether another copy follows. Separate
    /// from OnPrintPage so a test can render it to a bitmap.
    /// </summary>
    internal bool RenderPage(Graphics g, Rectangle bounds)
    {
        ArgumentNullException.ThrowIfNull(g);

        // Forty-two across, and the rows spread down the whole of what the
        // printer reaches - over six lines at the least, so a short label
        // is not set in lines twice the usual height.
        var rows = Rows();
        LabelPrint.Draw(g, bounds, rows, Columns, lines: 6);

        return ++_printed < _copies;
    }
}

/// <summary>Which part of a label a row is.</summary>
public enum LabelStyle
{
    /// The medicine and how many.
    Product,

    /// How to take it, and the warnings.
    Directions,

    /// Whose medicine it is, with their medical aid and the date.
    Patient,

    /// The expiry, the batch, the price and the date.
    Detail,

    /// The pharmacy and its address, at the foot.
    Imprint
}

/// <summary>One printed row of a label.</summary>
/// <param name="Underlined">
/// Underlined word by word, as the medicine and the price and date are on
/// the pharmacy's own labels.
/// </param>
/// <param name="Compact">
/// The expiry, the date and the address: two pixels smaller and on a
/// shorter line, so the year and the street stay on the sticker. The
/// trailer label does not set this.
/// </param>
public sealed record LabelRow(string Text, LabelStyle Style, bool Underlined = false,
                              bool Compact = false);

/// <summary>
/// The pharmacy as it prints at the foot of a dispensing label: who
/// dispensed the medicine, where, and what to ring about it.
/// </summary>
public sealed record PharmacyImprint(string TradingName, string? Address,
                                     string? Telephone);

public sealed class LabelPrinter : ILabelPrinter
{
    private readonly ILogger<LabelPrinter> _log;
    private readonly IWorkstationSettings _settings;
    private readonly PharmacyProfile? _pharmacy;

    public LabelPrinter(ILogger<LabelPrinter> log,
                        IWorkstationSettings settings,
                        PharmacyProfile pharmacy)
        : this(log, settings) =>
        _pharmacy = pharmacy;

    public LabelPrinter(ILogger<LabelPrinter> log,
                        IWorkstationSettings settings)
    {
        _log = log;
        _settings = settings;
    }

    public bool IsAvailable => PrinterSettings.InstalledPrinters.Count > 0;

    /// <remarks>
    /// Never throws. The medicine has been issued and the stock has moved
    /// by the time a label is printed, and a failed label is reprinted
    /// from the Labels screen - but the screen is told, so it no longer
    /// says the label printed when it did not.
    /// </remarks>
    public Task<PrintResult> PrintAsync(LabelDto label,
                                        CancellationToken ct = default) =>
        PrintCopiesAsync(label, 1, ct);

    public async Task<PrintResult> PrintCopiesAsync(LabelDto label, int copies,
                                                    CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(label);

        using var doc = new LabelDocument(label, _pharmacy is null
            ? null
            : new PharmacyImprint(_pharmacy.TradingName, _pharmacy.Address,
                                  _pharmacy.Telephone), copies);

        // Only on the printer chosen for labels: the Windows default at a
        // till is the receipt printer, and labels sent there came out on
        // the receipt roll.
        return await Spooler.PrintAsync(doc, _settings,
            WorkstationSettingKey.LabelPrinter, "label",
            $"Label {label.ScriptNumber} {label.ProductDescription}"
            + (copies > 1 ? $" x{copies}" : ""), _log, ct,
            orDefault: false)
            .ConfigureAwait(false);
    }

    /// <remarks>
    /// On the label printer, straight after the item labels, so it comes
    /// off the same roll as the rest of the script.
    /// </remarks>
    public async Task<PrintResult> PrintTrailerAsync(TrailerLabelDto trailer,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(trailer);

        using var doc = new TrailerLabelDocument(trailer, _pharmacy is null
            ? null
            : LabelDocument.DispensedBy(_pharmacy.TradingName,
                                        _pharmacy.Address),
            _pharmacy?.Telephone);

        return await Spooler.PrintAsync(doc, _settings,
            WorkstationSettingKey.LabelPrinter, "label",
            $"Trailer label {trailer.ScriptNumber}", _log, ct,
            orDefault: false)
            .ConfigureAwait(false);
    }
}
