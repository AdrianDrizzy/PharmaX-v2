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
    private readonly Font _title = new("Consolas", 11f, FontStyle.Bold);
    private int _next;
    private bool _headed;

    public PlainDocument(PrintableDocument document, ReceiptHeaderDto header)
    {
        _document = document;
        _header = header;

        // The till's 76 mm roll, as the receipt.
        DefaultPageSettings.PaperSize = new PaperSize("Roll", 299, 1200);
        DefaultPageSettings.Margins = new Margins(5, 5, 10, 10);
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
            Line(_header.TradingName, _title);

            foreach (var part in (_header.Address ?? "").Split('\n')
                         .Select(p => p.Trim()).Where(p => p.Length > 0))
                Line(part, body);

            if (!string.IsNullOrWhiteSpace(_header.VatNumber))
                Line($"VAT {_header.VatNumber}", body);

            // A quotation or a goods received note is taken away and
            // asked about later, like the receipt and the label.
            if (!string.IsNullOrWhiteSpace(_header.Telephone))
                Line($"Tel: {_header.Telephone.Trim()}", body);

            Line($"{_header.BranchAndTill}  {_header.OperatorName}", body);
            Line(_header.PrintedAtLocal.ToString("dd/MM/yyyy HH:mm",
                CultureInfo.CurrentCulture), body);
            Line(new string('-', SlipLayout.Width), body);
            Line(_document.Title, _title);
            Line(new string('-', SlipLayout.Width), body);

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
                && y + Height(_document.Lines[_next], body) > bounds.Bottom)
                return true;

            Line(_document.Lines[_next++], body);
        }

        return false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _title.Dispose();


        base.Dispose(disposing);
    }
}

/// <summary>
/// A dispensing label on the pharmacy's continuous dot-matrix stock: 102
/// by 36 millimetres (4 by 1 7/16 inches) on the Epson LX-350, printed as
/// 48 characters by 8 lines - 12 to the inch, the grid the previous
/// system printed in.
/// </summary>
/// <remarks>
/// Laid out row for row as the pharmacy's own labels, which go on the
/// printed dispensing bag:
/// <code>
/// CO-CODAMOL 30MG TAB (12)
/// TAKE 2 TABLETS AFTER MEALS THREE TIMES DAILY FOR
/// PAIN MAY CAUSE DROWSINESS
///
/// EXPIRY: 30/06/2030                BATCH NO: 0874
///                             PACKED ON:11/10/2026
/// Bethrapha Pharmacy, 28 Woodstock Road
/// Thorngroove, Bulawayo
/// </code>
/// The medicine and how many; the directions as they were typed with the
/// warnings run on after them, in capitals; whose it is on the fourth
/// row, which a pre-pack made up for the shelf leaves empty; the expiry
/// against the batch; the date; and the pharmacy. "Keep out of reach of
/// children" is printed on the bag.
/// </remarks>
public sealed class LabelDocument : PrintDocument
{
    /// The characters across the stock.
    public const int Columns = 48;

    /// The lines down it.
    public const int LineCount = 8;

    /// The rows above the expiry: the medicine, how to take it, and
    /// whose it is on the last of them.
    private const int Head = 4;

    private readonly LabelDto _label;
    private readonly PharmacyImprint? _pharmacy;

    /// <param name="pharmacy">
    /// The pharmacy the medicine came from - its name and its address -
    /// which every dispensed label has to carry. The label printed without
    /// it, so a container out of its bag said nothing of where it was
    /// dispensed (ADM-DEF-16). Its telephone number is not on the label,
    /// as it is not on the pharmacy's own.
    /// </param>
    public LabelDocument(LabelDto label, PharmacyImprint? pharmacy = null)
    {
        _label = label;
        _pharmacy = pharmacy;

        DefaultPageSettings.PaperSize = new PaperSize("Label", 400, 144);
        DefaultPageSettings.Margins = new Margins(4, 4, 4, 4);
    }

    protected override void OnPrintPage(PrintPageEventArgs e)
    {
        base.OnPrintPage(e);
        ArgumentNullException.ThrowIfNull(e);

        RenderPage(e.Graphics!, FixedPitch.Area(e));
        e.HasMorePages = false;
    }

    /// <summary>
    /// The label row by row, each no wider than the stock, exactly as it
    /// is printed. Public so a test, and the Labels screen's preview, can
    /// read what goes on the container without a printer.
    /// </summary>
    public IReadOnlyList<LabelRow> Rows()
    {
        var culture = CultureInfo.CurrentCulture;
        var rows = new List<LabelRow>();

        // What it is and how many: "CO-CODAMOL 30MG TAB (12)".
        Add(rows, string.Format(culture, "{0} ({1:0.##})",
                _label.ProductDescription.Trim(), _label.Quantity)
                .ToUpperInvariant(),
            LabelStyle.Product);

        // How to take it, with the warnings the caller gave - or those
        // that go with the medicine, never none - run on after it.
        var warnings = _label.Cautions.Count > 0
            ? _label.Cautions
            : LabelWording.Warnings(_label.ProductDescription);

        Add(rows, string.Join(" ", warnings.Select(w => w.Trim())
                                           .Prepend(LabelWording.Directions(_label.Directions)))
                  .ToUpperInvariant(),
            LabelStyle.Directions);

        // Whose it is, on the last row of the head. A pre-pack made up for
        // the shelf has nobody's name, and the row stays empty.
        while (rows.Count < Head - 1)
            rows.Add(new LabelRow("", LabelStyle.Directions));

        if (!string.IsNullOrWhiteSpace(_label.PatientName))
            Add(rows, _label.PatientName, LabelStyle.Patient);
        else if (rows.Count < Head)
            rows.Add(new LabelRow("", LabelStyle.Patient));

        Spread(rows,
            _label.Expiry is DateOnly expiry
                ? string.Format(culture, "EXPIRY: {0:dd/MM/yyyy}", expiry)
                : "",
            string.IsNullOrWhiteSpace(_label.BatchNumber)
                ? ""
                : $"BATCH NO: {_label.BatchNumber.Trim()}");

        Spread(rows, "", string.Format(culture, "PACKED ON:{0:dd/MM/yyyy}",
                                       _label.DispensedOn));

        // The pharmacy and its street, then the rest of the address.
        if (_pharmacy is not null)
        {
            var address = AddressLines(_pharmacy.Address).ToList();

            Add(rows, DispensedBy(_pharmacy.TradingName, address.FirstOrDefault()),
                LabelStyle.Imprint);
            Add(rows, string.Join(", ", address.Skip(1)), LabelStyle.Imprint);
        }

        return rows;
    }

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

    private static void Add(List<LabelRow> rows, string? text, LabelStyle style) =>
        rows.AddRange(Wrap(text).Select(piece => new LabelRow(piece, style)));

    /// <summary>
    /// A row with one text at its start and the other against its end -
    /// the expiry and the batch - or each on rows of its own where both
    /// will not fit.
    /// </summary>
    private static void Spread(List<LabelRow> rows, string left, string right)
    {
        if (left.Length + 1 + right.Length <= Columns)
        {
            rows.Add(new LabelRow(
                (left.PadRight(Columns - right.Length) + right).TrimEnd(),
                LabelStyle.Detail));
            return;
        }

        Add(rows, left, LabelStyle.Detail);
        rows.AddRange(Wrap(right).Select(piece =>
            new LabelRow(piece.PadLeft(Columns), LabelStyle.Detail)));
    }

    internal void RenderPage(Graphics g, Rectangle bounds)
    {
        ArgumentNullException.ThrowIfNull(g);

        var rows = Rows();

        // Forty-eight across and eight down. Where what has to go on runs
        // past eight lines - long directions with a patient's name - the
        // type steps down rather than the pharmacy's name falling off the
        // bottom of the stock.
        var size = FixedPitch.Size(g, Columns, bounds.Width, 12f,
                                   Math.Max(LineCount, rows.Count), bounds.Height);

        using var font = new Font(FixedPitch.Family, size);
        using var exact = FixedPitch.Format();

        var height = font.GetHeight(g);
        float y = bounds.Top;

        foreach (var row in rows)
        {
            g.DrawString(row.Text, font, Brushes.Black, bounds.Left, y, exact);
            y += height;
        }
    }
}

/// <summary>Which part of a label a row is.</summary>
public enum LabelStyle
{
    /// The medicine and how many.
    Product,

    /// How to take it, and the warnings.
    Directions,

    /// Whose medicine it is, on the fourth row.
    Patient,

    /// The expiry, the batch and the date.
    Detail,

    /// The pharmacy and its address, at the foot.
    Imprint
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
