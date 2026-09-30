using System.Globalization;
using ClosedXML.Excel;
using CsvHelper;
using PharmaX.Application.Reporting;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PharmaX.Reporting.Exporters;

public sealed class ExcelExporter : IReportExporter, IWorkbookWriter
{
    private readonly IReportBranding _branding;

    public ExcelExporter(IReportBranding branding) => _branding = branding;

    public ExportFormat Format => ExportFormat.Excel;

    public string Write(ReportResult report, string directory)
    {
        ArgumentNullException.ThrowIfNull(report);

        var path = Path.Combine(directory, FileName(report, "xlsx"));
        WriteTo(report, path);

        return path;
    }

    /// <summary>
    /// The same workbook, written where the caller says.
    /// </summary>
    public void WriteTo(ReportResult report, string path)
    {
        ArgumentNullException.ThrowIfNull(report);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SheetName(report.Title));

        var top = AddLogo(sheet);

        // Whose report it is, in words as well as the logo: the logo
        // alone left a workbook forwarded to an accountant naming nobody
        // (ADM-OBS-06).
        if (!string.IsNullOrWhiteSpace(_branding.TradingName))
        {
            sheet.Cell(top, 1).Value = _branding.TradingName;
            sheet.Cell(top, 1).Style.Font.Bold = true;
            sheet.Cell(top, 1).Style.Font.FontSize = 13;
            top++;
        }

        // Provenance first, so an exported file can still answer
        // "what was this?" a month later (RPT-012).
        sheet.Cell(top, 1).Value = report.Title;
        sheet.Cell(top, 1).Style.Font.Bold = true;
        sheet.Cell(top + 1, 1).Value = report.Provenance.ParameterSummary;
        sheet.Cell(top + 2, 1).Value = string.Format(
            CultureInfo.CurrentCulture,
            "Run by {0} at {1:dd/MM/yyyy HH:mm} - {2}",
            report.Provenance.RunByUsername, report.Provenance.RunAtLocal,
            report.Provenance.DataCurrency);

        var headerRow = top + 4;

        for (var c = 0; c < report.Columns.Count; c++)
        {
            var cell = sheet.Cell(headerRow, c + 1);
            cell.Value = report.Columns[c].Header;
            cell.Style.Font.Bold = true;
        }

        for (var i = 0; i < report.Rows.Count; i++)
        {
            for (var c = 0; c < report.Columns.Count; c++)
            {
                var column = report.Columns[c];
                report.Rows[i].TryGetValue(column.Key, out var value);
                var cell = sheet.Cell(headerRow + 1 + i, c + 1);

                cell.Value = value switch
                {
                    null => Blank.Value,
                    decimal d => d,
                    DateTime dt => dt,
                    int n => n,
                    bool flag => flag,
                    _ => value.ToString()
                };

                if (column.Format is not null)
                    cell.Style.NumberFormat.Format = column.Format;
            }
        }

        sheet.Columns().AdjustToContents();

        workbook.SaveAs(path);
    }

    /// <summary>
    /// Puts the pharmacy's logo at the top of the sheet and returns the
    /// first row the report itself may use.
    /// </summary>
    /// <remarks>
    /// Anchored to a cell and sized in rows rather than dropped at a
    /// pixel offset, so the provenance lines below it are never written
    /// underneath the picture. An unreadable or missing file costs the
    /// export nothing: the sheet starts at row one and the figures are
    /// the same either way.
    /// </remarks>
    private int AddLogo(IXLWorksheet sheet)
    {
        const int firstRowWithoutLogo = 1;

        var path = _branding.LogoPath;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return firstRowWithoutLogo;

        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));

            var picture = sheet.AddPicture(stream)
                               .MoveTo(sheet.Cell(1, 1));

            // Four rows of roughly the default height, which is enough
            // for a wordmark without pushing the data off the screen.
            const int rows = 4;
            const int heightPixels = 68;

            var scale = (double)heightPixels / picture.OriginalHeight;

            if (scale < 1d)
                picture.Scale(scale);

            return rows + 2;
        }
#pragma warning disable CA1031 // Deliberately broad: see the remarks.
        catch (Exception)
#pragma warning restore CA1031
        {
            return firstRowWithoutLogo;
        }
    }

    private static string SheetName(string title)
    {
        // Excel refuses a sheet name over 31 characters or containing
        // any of : \ / ? * [ ], and fails the whole save when it does.
        var cleaned = new string(title
            .Where(ch => !"\\/?*[]:".Contains(ch, StringComparison.Ordinal))
            .ToArray());

        return cleaned.Length <= 31 ? cleaned : cleaned[..31];
    }

    public static string FileName(ReportResult report, string extension) =>
        string.Format(CultureInfo.InvariantCulture, "{0}-{1:yyyyMMdd-HHmm}.{2}",
            Sanitise(report.Title), DateTime.Now, extension);

    internal static string Sanitise(string name) =>
        string.Concat(name.Where(ch =>
            !Path.GetInvalidFileNameChars().Contains(ch) && ch != ':'));
}

public sealed class CsvExporter : IReportExporter
{
    public ExportFormat Format => ExportFormat.Csv;

    public string Write(ReportResult report, string directory)
    {
        ArgumentNullException.ThrowIfNull(report);

        var path = Path.Combine(directory,
            ExcelExporter.FileName(report, "csv"));

        using var writer = new StreamWriter(path);
        using var csv = new CsvWriter(writer, CultureInfo.CurrentCulture);

        foreach (var column in report.Columns)
            csv.WriteField(column.Header);

        csv.NextRecord();

        foreach (var row in report.Rows)
        {
            foreach (var column in report.Columns)
            {
                row.TryGetValue(column.Key, out var value);
                csv.WriteField(value);
            }

            csv.NextRecord();
        }

        return path;
    }
}

public sealed class PdfExporter : IReportExporter
{
    private readonly IReportBranding _branding;

    public PdfExporter() : this(new NoReportBranding()) { }

    public PdfExporter(IReportBranding branding) => _branding = branding;

    public ExportFormat Format => ExportFormat.Pdf;

    public string Write(ReportResult report, string directory)
    {
        ArgumentNullException.ThrowIfNull(report);

        QuestPDF.Settings.License = LicenseType.Community;

        var path = Path.Combine(directory,
            ExcelExporter.FileName(report, "pdf"));

        Document.Create(container =>
        {
            container.Page(page =>
            {
                // Landscape, because a report with eight columns on
                // portrait A4 is a report nobody reads on paper.
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(8));

                page.Header().Column(header =>
                {
                    if (!string.IsNullOrWhiteSpace(_branding.TradingName))
                        header.Item().Text(_branding.TradingName)
                            .FontSize(11).Bold();

                    header.Item().Text(report.Title).FontSize(14).Bold();
                    header.Item().Text(report.Provenance.ParameterSummary);

                    // RPT-012: every page says what this is and how
                    // current it is, because pages get separated.
                    header.Item().Text(string.Format(
                        CultureInfo.CurrentCulture,
                        "Run by {0} at {1:dd/MM/yyyy HH:mm} - {2}",
                        report.Provenance.RunByUsername,
                        report.Provenance.RunAtLocal,
                        report.Provenance.DataCurrency)).FontSize(7);
                });

                page.Content().PaddingVertical(6).Table(table =>
                {
                    // Each column as wide as what it holds, within limits:
                    // shared out equally, fifteen columns left an item's
                    // name wrapping a word to a line beside a date column
                    // with room to spare.
                    var widths = Widths(report);

                    table.ColumnsDefinition(columns =>
                    {
                        foreach (var width in widths)
                            columns.RelativeColumn(width);
                    });

                    table.Header(h =>
                    {
                        foreach (var column in report.Columns)
                            h.Cell().BorderBottom(1).PaddingBottom(2)
                             .Text(column.Header).Bold();
                    });

                    foreach (var row in report.Rows)
                    {
                        foreach (var column in report.Columns)
                        {
                            row.TryGetValue(column.Key, out var value);

                            table.Cell().PaddingVertical(1)
                                 .AlignRight(column.RightAlign)
                                 .Text(Render(value, column.Format));
                        }
                    }
                });

                page.Footer().AlignRight().Text(text =>
                {
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
        }).GeneratePdf(path);

        return path;
    }

    /// <summary>
    /// How much of the page each column gets: the longest of its header
    /// and its values, read from the first few hundred rows, between a
    /// short number's width and a long name's.
    /// </summary>
    public static IReadOnlyList<float> Widths(ReportResult report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var sample = report.Rows.Take(400).ToList();

        return report.Columns
            .Select(column =>
            {
                var longest = sample
                    .Select(row => row.TryGetValue(column.Key, out var value)
                        ? Render(value, column.Format).Length
                        : 0)
                    .DefaultIfEmpty(0)
                    .Max();

                return (float)Math.Clamp(
                    Math.Max(column.Header.Length, longest), 5, 40);
            })
            .ToList();
    }

    private static string Render(object? value, string? format) =>
        value switch
        {
            null => "",
            IFormattable f when format is not null =>
                f.ToString(format, CultureInfo.CurrentCulture),
            _ => value.ToString() ?? ""
        };
}

internal static class PdfLayoutExtensions
{
    /// Right-alignment as a condition rather than a branch at every call
    /// site, so the table loop above stays one statement per cell.
    public static IContainer AlignRight(this IContainer container,
                                        bool rightAlign) =>
        rightAlign ? container.AlignRight() : container;
}
