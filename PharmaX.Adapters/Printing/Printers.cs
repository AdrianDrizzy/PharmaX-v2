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

    public ReceiptPrinter(ILogger<ReceiptPrinter> log,
                          IWorkstationSettings settings)
    {
        _log = log;
        _settings = settings;
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

        using var doc = new PlainDocument(document, header);

        return await Spooler.PrintAsync(doc, _settings,
            WorkstationSettingKey.ReceiptPrinter, "receipt",
            document.Title, _log, ct).ConfigureAwait(false);
    }
}

/// <summary>
/// The one route every document takes to the spooler: choose the printer,
/// check it, send the job, and say what happened.
/// </summary>
internal static class Spooler
{
    public static async Task<PrintResult> PrintAsync(PrintDocument doc,
        IWorkstationSettings settings, string settingKey, string purpose,
        string jobName, ILogger log, CancellationToken ct)
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

        var choice = PrinterResolver.Resolve(chosen, purpose);

        if (!choice.CanPrint)
        {
            log.LogWarning("{Job} not printed: {Problem}", jobName,
                choice.Problem);

            return choice.Printer is null
                ? PrintResult.NoPrinter(choice.Problem!)
                : PrintResult.Failed(choice.Printer, choice.Problem!);
        }

        doc.DocumentName = jobName;
        doc.PrinterSettings.PrinterName = choice.Printer!;

        if (!doc.PrinterSettings.IsValid)
        {
            var problem = $"Windows could not open the {purpose} printer "
                          + $"'{choice.Printer}'.";
            log.LogWarning("{Job} not printed: {Problem}", jobName, problem);
            return PrintResult.Failed(choice.Printer, problem);
        }

        try
        {
            // Off the caller's thread. Handing a document to the spooler
            // takes as long as the driver takes, and a driver that stops
            // to ask where to save a PDF takes as long as the operator
            // does - neither is a reason for the screen to stop redrawing.
            await Task.Run(doc.Print, ct).ConfigureAwait(false);
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
    private readonly Font _body = new("Consolas", 8f);
    private readonly Font _title = new("Consolas", 11f, FontStyle.Bold);
    private int _next;
    private bool _headed;

    public PlainDocument(PrintableDocument document, ReceiptHeaderDto header)
    {
        _document = document;
        _header = header;

        DefaultPageSettings.PaperSize = new PaperSize("Roll", 315, 1200);
        DefaultPageSettings.Margins = new Margins(10, 10, 10, 10);
    }

    protected override void OnBeginPrint(PrintEventArgs e)
    {
        base.OnBeginPrint(e);
        _next = 0;
        _headed = false;
    }

    protected override void OnPrintPage(PrintPageEventArgs e)
    {
        base.OnPrintPage(e);
        ArgumentNullException.ThrowIfNull(e);

        e.HasMorePages = RenderPage(e.Graphics!, e.MarginBounds);
    }

    /// <summary>
    /// Draws one page and says whether there is more. Separate from
    /// OnPrintPage so a test can render it to a bitmap and read it.
    /// </summary>
    internal bool RenderPage(Graphics g, Rectangle bounds)
    {
        ArgumentNullException.ThrowIfNull(g);

        float y = bounds.Top;

        // Wrapped to the roll. Lines were drawn as they came, so a line
        // wider than the paper - a quotation's notes, its own footer -
        // ran off the edge, which a till printer simply loses.
        float Height(string text, Font font) =>
            Math.Max(g.MeasureString(text, font, bounds.Width).Height,
                     font.GetHeight(g));

        void Line(string text, Font font)
        {
            var height = Height(text, font);

            g.DrawString(text, font, Brushes.Black,
                new RectangleF(bounds.Left, y, bounds.Width, height));
            y += height;
        }

        // Once, on the first page - not on every page that has yet to
        // reach the first line, which a header filling a page would be.
        if (!_headed)
        {
            _headed = true;
            Line(_header.TradingName, _title);

            foreach (var part in (_header.Address ?? "").Split('\n')
                         .Select(p => p.Trim()).Where(p => p.Length > 0))
                Line(part, _body);

            if (!string.IsNullOrWhiteSpace(_header.VatNumber))
                Line($"VAT {_header.VatNumber}", _body);

            // A quotation or a goods received note is taken away and
            // asked about later, like the receipt and the label.
            if (!string.IsNullOrWhiteSpace(_header.Telephone))
                Line($"Tel: {_header.Telephone.Trim()}", _body);

            Line($"{_header.BranchAndTill}  {_header.OperatorName}", _body);
            Line(_header.PrintedAtLocal.ToString("dd/MM/yyyy HH:mm",
                CultureInfo.CurrentCulture), _body);
            Line(new string('-', 42), _body);
            Line(_document.Title, _title);
            Line(new string('-', 42), _body);

            // The documents open with their own title as their first line
            // as well, so it printed twice (ADM-OBS-07).
            if (_next == 0 && _document.Lines.Count > 0
                && string.Equals(_document.Lines[0].Trim(),
                                 _document.Title.Trim(), StringComparison.Ordinal))
                _next = 1;
        }

        while (_next < _document.Lines.Count)
        {
            // A line that will not fit goes to the next page - unless
            // nothing is on this one yet, or it would never be printed.
            if (y > bounds.Top
                && y + Height(_document.Lines[_next], _body) > bounds.Bottom)
                return true;

            Line(_document.Lines[_next++], _body);
        }

        return false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _body.Dispose();
            _title.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// A dispensing label on a 50 by 70 millimetre stock, laid out as the
/// pharmacy's own labels are.
/// </summary>
/// <remarks>
/// Every label carries the patient's name; the medicine and its
/// strength; the directions in plain words; the quantity and the date;
/// the warnings for the medicine and "Keep out of reach of children";
/// and the pharmacy's name, address and telephone number at the foot.
///
/// Whose it is comes first, so the right bag reaches the right person.
/// The medicine and how to take it follow in the largest type, over a
/// rule; then the particulars that settle a query at the counter - the
/// quantity and date, the batch and its expiry, who dispensed it, who
/// prescribed it, and the script and item numbers; then the warnings in
/// bold; and the pharmacy itself at the foot with the number to ring.
/// </remarks>
public sealed class LabelDocument : PrintDocument
{
    private readonly LabelDto _label;
    private readonly PharmacyImprint? _pharmacy;

    // Sized to the stock rather than to taste: a label with two batches
    // and directions that wrap has to end inside the frame, and the two
    // rows the patient actually reads keep the largest type on it.
    private readonly Font _product = new("Segoe UI", 9f, FontStyle.Bold);
    private readonly Font _directions = new("Segoe UI", 8f, FontStyle.Bold);
    private readonly Font _detail = new("Segoe UI", 6.5f);
    private readonly Font _name = new("Segoe UI", 7f, FontStyle.Bold);
    private readonly Font _small = new("Segoe UI", 6f);
    private readonly Font _warning = new("Segoe UI", 6.5f, FontStyle.Bold);

    /// <param name="pharmacy">
    /// The pharmacy the medicine came from - its name, its address and
    /// its telephone number - which every dispensed label has to carry.
    /// The label printed without it, so a container out of its bag said
    /// nothing of where it was dispensed or who to call about it
    /// (ADM-DEF-16).
    /// </param>
    public LabelDocument(LabelDto label, PharmacyImprint? pharmacy = null)
    {
        _label = label;
        _pharmacy = pharmacy;

        DefaultPageSettings.PaperSize = new PaperSize("Label", 276, 197);
        DefaultPageSettings.Margins = new Margins(8, 8, 8, 8);
    }

    protected override void OnPrintPage(PrintPageEventArgs e)
    {
        base.OnPrintPage(e);
        ArgumentNullException.ThrowIfNull(e);

        RenderPage(e.Graphics!, e.MarginBounds);
        e.HasMorePages = false;
    }

    /// <summary>
    /// The label row by row, as it is printed. Public so a test can read
    /// what goes on the container without a printer.
    /// </summary>
    public IReadOnlyList<LabelRow> Rows()
    {
        var culture = CultureInfo.CurrentCulture;
        var rows = new List<LabelRow>();

        void Row(string? text, LabelStyle style)
        {
            if (!string.IsNullOrWhiteSpace(text))
                rows.Add(new LabelRow(text!.Trim(), style));
        }

        // Whose it is: a pre-pack made up for the shelf has nobody's name.
        Row(_label.PatientName, LabelStyle.Patient);

        // The medicine and how to take it: what the patient reads, and
        // the only part of the label that has to be read across a room.
        // The directions in plain words, the dose and how often in
        // capitals, however they were typed.
        Row(_label.ProductDescription, LabelStyle.Product);
        Row(LabelWording.Directions(_label.Directions), LabelStyle.Directions);

        rows.Add(new LabelRow("", LabelStyle.Rule));

        // The quantity and the date spelt out: "x21" beside the name was
        // read as part of the pack size.
        Row(string.Format(culture, "Qty: {0:0.##}   Date: {1:dd/MM/yyyy}",
            _label.Quantity, _label.DispensedOn), LabelStyle.Detail);

        if (_label.BatchNumber is not null)
            Row(string.Format(culture, "Batch: {0}   Exp: {1:dd/MM/yyyy}",
                _label.BatchNumber, _label.Expiry), LabelStyle.Detail);

        Row(string.IsNullOrWhiteSpace(_label.PharmacistName)
            ? null
            : $"Disp by: {_label.PharmacistName.Trim()}", LabelStyle.Detail);

        Row(_label.PrescriberName is null
            ? null
            : $"Doc. {_label.PrescriberName.Trim()}", LabelStyle.Detail);

        Row(Reference(culture), LabelStyle.Detail);

        // The warnings the caller gave, or those that go with the
        // medicine; never none. Every label was printed without any.
        var warnings = _label.Cautions.Count > 0
            ? _label.Cautions
            : LabelWording.Warnings(_label.ProductDescription);

        foreach (var warning in warnings)
            Row(warning, LabelStyle.Warning);

        Row(LabelWording.KeepOutOfReach, LabelStyle.Warning);

        // The pharmacy last and smallest: the patient reads the top of a
        // label, the regulation reads the foot.
        if (_pharmacy is not null)
        {
            rows.Add(new LabelRow("", LabelStyle.Gap));
            Row(_pharmacy.TradingName, LabelStyle.Pharmacy);
            Row(string.Join(", ", AddressLines(_pharmacy.Address)),
                LabelStyle.Imprint);
            Row(string.IsNullOrWhiteSpace(_pharmacy.Telephone)
                ? null
                : $"Tel: {_pharmacy.Telephone.Trim()}", LabelStyle.Imprint);
        }

        return rows;
    }

    /// <summary>
    /// The lines the label carries, top to bottom. Public so a test can
    /// check the content without a printer.
    /// </summary>
    public IReadOnlyList<string> Lines() =>
        Rows().Where(row => row.Text.Length > 0)
              .Select(row => row.Text)
              .ToList();

    /// <summary>
    /// The script, which of its items this label is, and the
    /// prescriber's own number: what the pharmacy is asked for when
    /// anybody rings about the medicine.
    /// </summary>
    private string Reference(CultureInfo culture)
    {
        var parts = new List<string>
        {
            string.Format(culture, "RxNo: {0}", _label.ScriptNumber),

            // "Item: 2 of 3" only where there is more than one: a single
            // item counted out of one reads as though something is
            // missing from the bag.
            _label.ItemCount > 1
                ? string.Format(culture, "Item: {0} of {1}",
                    _label.ItemNumber, _label.ItemCount)
                : string.Format(culture, "Item: {0}", _label.ItemNumber)
        };

        if (!string.IsNullOrWhiteSpace(_label.PrescriberNumber))
            parts.Add($"Prof# {_label.PrescriberNumber!.Trim()}");

        return string.Join("  ", parts);
    }

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

    internal void RenderPage(Graphics g, Rectangle bounds)
    {
        ArgumentNullException.ThrowIfNull(g);

        // The printed area is boxed, as the pharmacy's own stationery
        // is, so a label that has crept on the roll is obvious.
        g.DrawRectangle(Pens.Black, bounds.Left, bounds.Top,
                        bounds.Width - 1, bounds.Height - 1);

        var area = Rectangle.Inflate(bounds, -4, -4);
        var rows = Rows();

        // Measured before anything is drawn, and the whole label stepped
        // down where it does not fit. The stock is 70 by 50 millimetres
        // and does not grow: a label that ran off the bottom of it lost
        // the pharmacy's own name and telephone number, which is the
        // half a patient needs hours later. Smaller type they can still
        // read beats particulars that are not on the container at all.
        var height = Lay(g, rows, area, 1f, draw: false);

        var scale = height <= area.Height
            ? 1f
            : Math.Max(MinimumScale, area.Height / height);

        Lay(g, rows, area, scale, draw: true);
    }

    /// <summary>
    /// Lays the rows out at a scale, drawing them or only measuring, and
    /// says how tall they came to.
    /// </summary>
    private float Lay(Graphics g, IReadOnlyList<LabelRow> rows,
                      Rectangle area, float scale, bool draw)
    {
        var scaled = new Dictionary<LabelStyle, Font>();

        Font For(LabelStyle style)
        {
            if (scaled.TryGetValue(style, out var found)) return found;

            var basis = FontFor(style);

            var font = scale >= 1f
                ? basis
                : new Font(basis.FontFamily, basis.Size * scale, basis.Style);

            scaled[style] = font;
            return font;
        }

        try
        {
            float y = area.Top;

            foreach (var row in rows)
            {
                if (row.Style == LabelStyle.Gap)
                {
                    y += 4 * scale;
                    continue;
                }

                if (row.Style == LabelStyle.Rule)
                {
                    y += 2 * scale;

                    if (draw)
                        g.DrawLine(Pens.Black, area.Left, y, area.Right, y);

                    y += 3 * scale;
                    continue;
                }

                var font = For(row.Style);

                // Each row advances by the height it actually wrapped to.
                // It used to advance by one line whatever it wrapped to,
                // so long directions and a three-batch line overprinted
                // the rows under them - the one place a patient reads how
                // to take the medicine.
                var height = Math.Max(
                    g.MeasureString(row.Text, font, area.Width).Height,
                    font.GetHeight(g));

                if (draw)
                    g.DrawString(row.Text, font, Brushes.Black,
                        new RectangleF(area.Left, y, area.Width, height));

                y += height;
            }

            return y - area.Top;
        }
        finally
        {
            // Only the scaled ones are this method's to dispose; at full
            // size they are the document's own.
            if (scale < 1f)
            {
                foreach (var font in scaled.Values) font.Dispose();
            }
        }
    }

    /// <summary>
    /// How far the type may be stepped down to make a label fit. Below
    /// this it stops being a label anybody can read, and a label nobody
    /// can read is worse than one that has been shortened.
    /// </summary>
    private const float MinimumScale = 0.6f;

    private Font FontFor(LabelStyle style) => style switch
    {
        LabelStyle.Product => _product,
        LabelStyle.Directions => _directions,
        LabelStyle.Pharmacy => _name,
        LabelStyle.Patient => _name,
        LabelStyle.Imprint => _small,
        LabelStyle.Warning => _warning,
        _ => _detail
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _product.Dispose();
            _directions.Dispose();
            _detail.Dispose();
            _name.Dispose();
            _small.Dispose();
            _warning.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>How a row of a label is printed.</summary>
public enum LabelStyle
{
    /// Whose medicine it is, at the top.
    Patient,

    /// The medicine, in the largest type on the label.
    Product,

    /// How to take it, under the medicine.
    Directions,

    /// The particulars: batch, patient, dispenser, prescriber, script.
    Detail,

    /// The warnings and "Keep out of reach of children", in bold.
    Warning,

    /// The pharmacy's own name at the foot.
    Pharmacy,

    /// Its address and telephone number, in the smallest type.
    Imprint,

    /// The rule under the medicine and its directions.
    Rule,

    /// The space between the particulars and the pharmacy.
    Gap
}

/// <summary>One printed row of a label.</summary>
public sealed record LabelRow(string Text, LabelStyle Style);

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
    public async Task<PrintResult> PrintAsync(LabelDto label,
                                              CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(label);

        using var doc = new LabelDocument(label, _pharmacy is null
            ? null
            : new PharmacyImprint(_pharmacy.TradingName, _pharmacy.Address,
                                  _pharmacy.Telephone));

        return await Spooler.PrintAsync(doc, _settings,
            WorkstationSettingKey.LabelPrinter, "label",
            $"Label {label.ScriptNumber} {label.ProductDescription}", _log, ct)
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
            $"Trailer label {trailer.ScriptNumber}", _log, ct)
            .ConfigureAwait(false);
    }
}
