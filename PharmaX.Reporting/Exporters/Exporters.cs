using System.Globalization;
using ClosedXML.Excel;
using CsvHelper;
using PharmaX.Application.Reporting;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PharmaX.Reporting.Exporters;

public sealed class ExcelExporter : IReportExporter
{
    public ExportFormat Format => ExportFormat.Excel;

    public string Write(ReportResult report, string directory)
    {
        ArgumentNullException.ThrowIfNull(report);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SheetName(report.Title));

        // Provenance first, so an exported file can still answer
        // "what was this?" a month later (RPT-012).
        sheet.Cell(1, 1).Value = report.Title;
        sheet.Cell(2, 1).Value = report.Provenance.ParameterSummary;
        sheet.Cell(3, 1).Value = string.Format(CultureInfo.CurrentCulture,
            "Run by {0} at {1:dd/MM/yyyy HH:mm} - {2}",
            report.Provenance.RunByUsername, report.Provenance.RunAtLocal,
            report.Provenance.DataCurrency);

        const int headerRow = 5;

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

        var path = Path.Combine(directory, FileName(report, "xlsx"));
        workbook.SaveAs(path);
        return path;
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

    internal static string FileName(ReportResult report, string extension) =>
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
                    table.ColumnsDefinition(columns =>
                    {
                        foreach (var _ in report.Columns)
                            columns.RelativeColumn();
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
