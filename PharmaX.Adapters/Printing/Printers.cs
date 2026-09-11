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

    public PrintOutcome Print(SaleDto sale, ReceiptHeaderDto header)
    {
        if (!IsAvailable)
        {
            _log.LogWarning("No printer installed; receipt not printed.");
            return PrintOutcome.NoPrinter;
        }

        try
        {
            using var doc = new ReceiptDocument(sale, header);

            // Blocking on the setting read is deliberate: printing is
            // already synchronous, and the alternative is an async
            // interface for one string lookup against a local file.
            var chosen = _settings
                .GetAsync(WorkstationSettingKey.ReceiptPrinter)
                .GetAwaiter().GetResult();

            if (!string.IsNullOrWhiteSpace(chosen))
                doc.PrinterSettings.PrinterName = chosen;

            doc.Print();
            return PrintOutcome.Printed;
        }
#pragma warning disable CA1031 // Deliberately broad: see the comment below.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // The sale is already committed. A printer fault must never
            // roll it back or block the next customer (POS-014), and the
            // driver stack can throw almost anything.
            _log.LogWarning(ex, "Receipt print failed for {Document}",
                sale?.DocumentNumber);
            return PrintOutcome.Failed;
        }
    }
}

/// <summary>
/// A dispensing label on a 50 by 70 millimetre stock. Everything a
/// patient needs to take the medicine correctly, and nothing else.
/// </summary>
public sealed class LabelDocument : PrintDocument
{
    private readonly LabelDto _label;
    private readonly Font _body = new("Segoe UI", 8f);
    private readonly Font _bold = new("Segoe UI", 9f, FontStyle.Bold);
    private readonly Font _directions = new("Segoe UI", 10f, FontStyle.Bold);

    public LabelDocument(LabelDto label)
    {
        _label = label;

        DefaultPageSettings.PaperSize = new PaperSize("Label", 276, 197);
        DefaultPageSettings.Margins = new Margins(8, 8, 8, 8);
    }

    protected override void OnPrintPage(PrintPageEventArgs e)
    {
        base.OnPrintPage(e);
        ArgumentNullException.ThrowIfNull(e);

        var g = e.Graphics!;
        var culture = CultureInfo.CurrentCulture;
        var bounds = e.MarginBounds;
        float y = bounds.Top;

        void Line(string text, Font font)
        {
            g.DrawString(text, font, Brushes.Black,
                new RectangleF(bounds.Left, y, bounds.Width,
                               font.GetHeight(g) * 3));
            y += font.GetHeight(g);
        }

        Line(_label.PatientName, _bold);
        Line(string.Format(culture, "{0}  x{1:0.##}",
            _label.ProductDescription, _label.Quantity), _body);

        y += 4;

        // The directions are the point of the label, so they get the
        // largest type and the room to wrap onto a second line.
        Line(_label.Directions, _directions);

        y += 4;

        if (_label.BatchNumber is not null)
            Line(string.Format(culture, "Batch {0}   Exp {1:MM/yyyy}",
                _label.BatchNumber, _label.Expiry), _body);

        foreach (var caution in _label.Cautions)
            Line(caution, _body);

        Line(string.Format(culture, "{0:dd/MM/yyyy}   {1}",
            _label.DispensedOn, _label.PharmacistName), _body);

        Line(string.Format(culture, "Script {0}", _label.ScriptNumber), _body);

        e.HasMorePages = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _body.Dispose();
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

    public LabelPrinter(ILogger<LabelPrinter> log,
                        IWorkstationSettings settings)
    {
        _log = log;
        _settings = settings;
    }

    public bool IsAvailable => PrinterSettings.InstalledPrinters.Count > 0;

    public void Print(LabelDto label)
    {
        if (!IsAvailable)
        {
            _log.LogWarning("No printer installed; label not printed.");
            return;
        }

        try
        {
            using var doc = new LabelDocument(label);

            var chosen = _settings
                .GetAsync(WorkstationSettingKey.LabelPrinter)
                .GetAwaiter().GetResult();

            if (!string.IsNullOrWhiteSpace(chosen))
                doc.PrinterSettings.PrinterName = chosen;

            doc.Print();
        }
#pragma warning disable CA1031 // Deliberately broad: see the comment below.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // The medicine has been issued and the stock has moved. A
            // failed label is reprinted from the dispensing screen; it is
            // never a reason to unwind the issue.
            _log.LogWarning(ex, "Label print failed for {Script}",
                label?.ScriptNumber);
        }
    }
}
