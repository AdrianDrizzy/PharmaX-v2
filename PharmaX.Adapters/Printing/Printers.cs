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
/// A dispensing label on a 50 by 70 millimetre stock. Everything a
/// patient needs to take the medicine correctly, and nothing else.
/// </summary>
public sealed class LabelDocument : PrintDocument
{
    private readonly LabelDto _label;
    private readonly string? _dispensedBy;
    private readonly Font _body = new("Segoe UI", 8f);
    private readonly Font _small = new("Segoe UI", 7f);
    private readonly Font _bold = new("Segoe UI", 9f, FontStyle.Bold);
    private readonly Font _directions = new("Segoe UI", 10f, FontStyle.Bold);

    /// <param name="dispensedBy">
    /// The pharmacy the medicine came from - its name and address - which
    /// every dispensed label has to carry. The label printed without it,
    /// so a container out of its bag said nothing of where it was
    /// dispensed or who to call about it (ADM-DEF-16).
    /// </param>
    public LabelDocument(LabelDto label, string? dispensedBy = null)
    {
        _label = label;
        _dispensedBy = string.IsNullOrWhiteSpace(dispensedBy)
            ? null
            : dispensedBy.Trim();

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
    /// The lines the label carries, top to bottom, as they are printed.
    /// Public so a test can check the content without a printer.
    /// </summary>
    public IReadOnlyList<string> Lines()
    {
        var culture = CultureInfo.CurrentCulture;
        var lines = new List<string>
        {
            _label.PatientName,
            string.Format(culture, "{0}  x{1:0.##}",
                _label.ProductDescription, _label.Quantity),
            _label.Directions
        };

        if (_label.BatchNumber is not null)
            lines.Add(string.Format(culture, "Batch {0}   Exp {1:MM/yyyy}",
                _label.BatchNumber, _label.Expiry));

        lines.AddRange(_label.Cautions);

        lines.Add(string.Format(culture, "{0:dd/MM/yyyy}   {1}",
            _label.DispensedOn, _label.PharmacistName));
        lines.Add(string.Format(culture, "Script {0}", _label.ScriptNumber));

        // Last, and in the smallest type: the patient reads the top of a
        // label, the regulation reads the foot.
        if (_dispensedBy is not null) lines.Add(_dispensedBy);

        return lines;
    }

    /// <summary>
    /// The pharmacy as a label names it: trading name, then the address
    /// on one line.
    /// </summary>
    public static string DispensedBy(string tradingName, string? address) =>
        string.Join(", ", new[] { tradingName }
            .Concat((address ?? "").Split('\n')
                .Select(part => part.Trim())
                .Where(part => part.Length > 0))
            .Where(part => !string.IsNullOrWhiteSpace(part)));

    internal void RenderPage(Graphics g, Rectangle bounds)
    {
        ArgumentNullException.ThrowIfNull(g);

        float y = bounds.Top;

        // Each line advances by the height it actually wrapped to. It used
        // to advance by one line whatever it wrapped to, so long directions
        // and a three-batch line overprinted the lines under them on the
        // label - the one place a patient reads how to take the medicine.
        void Line(string text, Font font)
        {
            var height = g.MeasureString(text, font, bounds.Width).Height;

            g.DrawString(text, font, Brushes.Black,
                new RectangleF(bounds.Left, y, bounds.Width, height));
            y += Math.Max(height, font.GetHeight(g));
        }

        var lines = Lines();

        Line(lines[0], _bold);
        Line(lines[1], _body);

        y += 4;

        // The directions are the point of the label, so they get the
        // largest type and the room to wrap onto a second line.
        Line(lines[2], _directions);

        y += 4;

        foreach (var line in lines.Skip(3)
                     .Take(lines.Count - 3 - (_dispensedBy is null ? 0 : 1)))
            Line(line, _body);

        if (_dispensedBy is not null)
        {
            y += 2;
            Line(_dispensedBy, _small);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _body.Dispose();
            _small.Dispose();
            _bold.Dispose();
            _directions.Dispose();
        }

        base.Dispose(disposing);
    }
}

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
            : LabelDocument.DispensedBy(_pharmacy.TradingName,
                                        _pharmacy.Address));

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
                                        _pharmacy.Address));

        return await Spooler.PrintAsync(doc, _settings,
            WorkstationSettingKey.LabelPrinter, "label",
            $"Trailer label {trailer.ScriptNumber}", _log, ct)
            .ConfigureAwait(false);
    }
}
