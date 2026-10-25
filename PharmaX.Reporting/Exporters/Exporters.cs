using System.Globalization;
using ClosedXML.Excel;
using CsvHelper;
using PharmaX.Application.Reporting;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PharmaX.Reporting.Exporters;

/// <summary>
/// The pharmacy's particulars as the head of an export prints them: the
/// address a line at a time, then the VAT number and telephone together.
/// </summary>
internal static class Letterhead
{
    public static IReadOnlyList<string> Lines(IReportBranding branding)
    {
        var lines = (branding.Address ?? "")
            .Split('\n')
            .Select(l => l.Trim().TrimEnd(','))
            .Where(l => l.Length > 0)
            .ToList();

        var numbers = new List<string>();
        if (!string.IsNullOrWhiteSpace(branding.VatNumber))
            numbers.Add($"VAT No: {branding.VatNumber.Trim()}");
        if (!string.IsNullOrWhiteSpace(branding.Telephone))
            numbers.Add($"Tel: {branding.Telephone.Trim()}");
        if (numbers.Count > 0) lines.Add(string.Join("   |   ", numbers));

        return lines;
    }

    public static string RunBy(ReportResult report) => string.Format(
        CultureInfo.CurrentCulture, "Run by {0} at {1:dd/MM/yyyy HH:mm} - {2}",
        report.Provenance.RunByUsername, report.Provenance.RunAtLocal,
        report.Provenance.DataCurrency);

    /// The main table and every section after it, as one list.
    public static IEnumerable<ReportSection> Tables(ReportResult report) =>
        new[] { new ReportSection("", report.Columns, report.Rows) }
            .Concat(report.Sections);
}

public sealed class ExcelExporter : IReportExporter, IWorkbookWriter
{
    private static readonly XLColor HeaderFill = XLColor.FromHtml("#DCE6F2");
    private static readonly XLColor Banding = XLColor.FromHtml("#F5F7FA");

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
    /// <remarks>
    /// The pharmacy's letterhead, the report's provenance (RPT-012), its
    /// headline figures, then each table under a coloured header row. A
    /// chart goes on a sheet of its own, so it never covers a figure.
    /// </remarks>
    public void WriteTo(ReportResult report, string path)
    {
        ArgumentNullException.ThrowIfNull(report);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SheetName(report.Title));
        sheet.Style.Font.FontName = "Calibri";
        sheet.Style.Font.FontSize = 10;

        var row = AddLogo(sheet);

        // Whose report it is, in words as well as the logo: the logo
        // alone left a workbook forwarded to an accountant naming nobody
        // (ADM-OBS-06).
        if (!string.IsNullOrWhiteSpace(_branding.TradingName))
        {
            sheet.Cell(row, 1).Value = _branding.TradingName;
            sheet.Cell(row, 1).Style.Font.Bold = true;
            sheet.Cell(row, 1).Style.Font.FontSize = 14;
            row++;
        }

        foreach (var line in Letterhead.Lines(_branding))
        {
            sheet.Cell(row, 1).Value = line;
            sheet.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml("#4B5563");
            row++;
        }

        row++;

        // Provenance first, so an exported file can still answer
        // "what was this?" a month later (RPT-012).
        sheet.Cell(row, 1).Value = report.Title;
        sheet.Cell(row, 1).Style.Font.Bold = true;
        sheet.Cell(row, 1).Style.Font.FontSize = 16;
        sheet.Cell(row + 1, 1).Value = report.Provenance.ParameterSummary;
        sheet.Cell(row + 2, 1).Value = Letterhead.RunBy(report);
        sheet.Range(row + 1, 1, row + 2, 1).Style.Font.FontColor = XLColor.FromHtml("#6B7280");
        row += 4;

        foreach (var figure in report.Figures)
        {
            sheet.Cell(row, 1).Value = figure.Label;
            sheet.Cell(row, 1).Style.Font.Bold = true;
            sheet.Cell(row, 2).Value = figure.Value;
            sheet.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            row++;
        }

        if (report.Figures.Count > 0) row++;

        var first = true;
        var widest = 1;

        foreach (var table in Letterhead.Tables(report))
        {
            if (!first && table.Rows.Count == 0) continue;

            if (table.Title.Length > 0)
            {
                sheet.Cell(row, 1).Value = table.Title;
                sheet.Cell(row, 1).Style.Font.Bold = true;
                sheet.Cell(row, 1).Style.Font.FontSize = 12;
                row++;
            }

            var headerRow = row;
            row = WriteTable(sheet, table, row);
            widest = Math.Max(widest, table.Columns.Count);

            // The main table can be filtered and stays in view as it
            // scrolls; with sections below, a filter would hide them.
            if (first && report.Sections.Count == 0 && table.Rows.Count > 0)
            {
                sheet.Range(headerRow, 1, row - 1, table.Columns.Count).SetAutoFilter();
                sheet.SheetView.FreezeRows(headerRow);
            }

            first = false;
            row += 2;
        }

        sheet.Columns(1, widest).AdjustToContents(1, row);

        foreach (var column in sheet.Columns(1, widest))
            column.Width = Math.Min(Math.Max(column.Width, 9), 60);

        if (report.Chart is ReportChart chart) AddChart(workbook, chart);

        sheet.PageSetup.PageOrientation = widest > 6
            ? XLPageOrientation.Landscape
            : XLPageOrientation.Portrait;
        sheet.PageSetup.FitToPages(1, 0);

        workbook.SaveAs(path);
    }

    /// Writes one table from <paramref name="row"/> and returns the row after it.
    private static int WriteTable(IXLWorksheet sheet, ReportSection table, int row)
    {
        for (var c = 0; c < table.Columns.Count; c++)
        {
            var cell = sheet.Cell(row, c + 1);
            cell.Value = table.Columns[c].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = HeaderFill;
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;

            if (table.Columns[c].RightAlign)
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        }

        row++;

        for (var i = 0; i < table.Rows.Count; i++, row++)
        {
            for (var c = 0; c < table.Columns.Count; c++)
            {
                var column = table.Columns[c];
                table.Rows[i].TryGetValue(column.Key, out var value);
                var cell = sheet.Cell(row, c + 1);

                cell.Value = value switch
                {
                    null => Blank.Value,
                    decimal d => d,
                    DateTime dt => dt,
                    DateOnly day => day.ToDateTime(TimeOnly.MinValue),
                    int n => n,
                    long n => n,
                    double n => n,
                    bool flag => flag,
                    _ => value.ToString()
                };

                if (ExcelFormat(column.Format) is { } format)
                    cell.Style.NumberFormat.Format = format;

                if (i % 2 == 1) cell.Style.Fill.BackgroundColor = Banding;
            }
        }

        return row;
    }

    /// <summary>
    /// A column's format in Excel's own terms.
    /// </summary>
    /// <remarks>
    /// The columns carry .NET format strings, which the preview and the
    /// PDF use as they are. Handed to Excel as they were, "N2" is not a
    /// number format, and Excel opened every report workbook by offering
    /// to repair its styles. Dates and times ("dd/MM/yyyy HH:mm") read the
    /// same in both and pass through.
    /// </remarks>
    public static string? ExcelFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format)) return null;

        if (format.Length == 2 && char.IsAsciiDigit(format[1]))
        {
            var places = format[1] - '0';
            var decimals = places == 0 ? "" : "." + new string('0', places);

            switch (char.ToUpperInvariant(format[0]))
            {
                case 'N':
                case 'C':
                    return "#,##0" + decimals;
                case 'F':
                    return "0" + decimals;
                case 'P':
                    return "0" + decimals + "%";
            }
        }

        return format;
    }

    /// <summary>The graph on a sheet of its own, as a picture.</summary>
    private static void AddChart(XLWorkbook workbook, ReportChart chart)
    {
        try
        {
            var sheet = workbook.Worksheets.Add("Graph");
            sheet.Cell(1, 1).Value = chart.Title;
            sheet.Cell(1, 1).Style.Font.Bold = true;
            sheet.Cell(1, 1).Style.Font.FontSize = 14;

            using var stream = new MemoryStream(ReportCharts.Picture(chart, 720, 360));
            sheet.AddPicture(stream).MoveTo(sheet.Cell(3, 1)).Scale(0.5);

            // The figures it was drawn from, beside it, so the graph can be
            // checked or charted again in Excel.
            sheet.Cell(3, 14).Value = "Label";
            sheet.Cell(3, 15).Value = chart.ValueLabel;
            sheet.Range(3, 14, 3, 15).Style.Font.Bold = true;
            sheet.Range(3, 14, 3, 15).Style.Fill.BackgroundColor = HeaderFill;

            for (var i = 0; i < chart.Points.Count; i++)
            {
                sheet.Cell(4 + i, 14).Value = chart.Points[i].Label;
                sheet.Cell(4 + i, 15).Value = chart.Points[i].Value;
                sheet.Cell(4 + i, 15).Style.NumberFormat.Format = "#,##0.00";
            }

            sheet.Column(14).AdjustToContents();
            sheet.Column(15).AdjustToContents();
        }
#pragma warning disable CA1031 // The figures are the report; a picture that fails costs nothing.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
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

        if (cleaned.Length == 0 || cleaned == "Graph") cleaned = "Report";

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

public sealed class PdfExporter : IReportExporter, IPdfWriter
{
    private const string Accent = "#2F6DB5";
    private const string HeaderFill = "#DCE6F2";
    private const string Banding = "#F5F7FA";
    private const string Muted = "#6B7280";

    private readonly IReportBranding _branding;

    public PdfExporter() : this(new NoReportBranding()) { }

    public PdfExporter(IReportBranding branding) => _branding = branding;

    public ExportFormat Format => ExportFormat.Pdf;

    public string Write(ReportResult report, string directory)
    {
        ArgumentNullException.ThrowIfNull(report);

        var path = Path.Combine(directory,
            ExcelExporter.FileName(report, "pdf"));

        WriteTo(report, path);
        return path;
    }

    public void WriteTo(ReportResult report, string path)
    {
        ArgumentNullException.ThrowIfNull(report);

        QuestPDF.Settings.License = LicenseType.Community;

        var widest = Letterhead.Tables(report).Max(t => t.Columns.Count);
        var logo = ReadLogo();

        Document.Create(container =>
        {
            container.Page(page =>
            {
                // Landscape where the table is wide, because a report with
                // eight columns on portrait A4 is a report nobody reads on
                // paper.
                page.Size(widest > 6 ? PageSizes.A4.Landscape() : PageSizes.A4);
                page.Margin(1.2f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(8.5f).FontColor("#1F2937"));

                page.Header().Element(header => Header(header, report, logo));

                page.Content().PaddingTop(8).Column(body =>
                {
                    body.Spacing(10);

                    if (report.Figures.Count > 0)
                        body.Item().Element(c => Figures(c, report.Figures));

                    if (report.Chart is ReportChart chart)
                    {
                        var svg = ReportCharts.Svg(chart, 720, 300);
                        body.Item().Height(210).AlignCenter().Svg(svg).FitArea();
                    }

                    foreach (var table in Letterhead.Tables(report))
                    {
                        if (table.Rows.Count == 0 && table.Title.Length > 0) continue;

                        body.Item().Column(block =>
                        {
                            if (table.Title.Length > 0)
                                block.Item().PaddingBottom(3).Text(table.Title)
                                     .FontSize(11).Bold().FontColor(Accent);

                            block.Item().Element(c => Table(c, table));
                        });
                    }
                });

                page.Footer().Row(row =>
                {
                    row.RelativeItem().Text(text =>
                    {
                        text.Span(report.Title).FontColor(Muted);
                        if (!string.IsNullOrWhiteSpace(_branding.TradingName))
                            text.Span("  -  " + _branding.TradingName).FontColor(Muted);
                    });

                    row.AutoItem().AlignRight().Text(text =>
                    {
                        text.Span("Page ").FontColor(Muted);
                        text.CurrentPageNumber().FontColor(Muted);
                        text.Span(" of ").FontColor(Muted);
                        text.TotalPages().FontColor(Muted);
                    });
                });
            });
        }).GeneratePdf(path);
    }

    /// <summary>
    /// The letterhead on every page: logo left, the pharmacy's particulars
    /// beside it, then what the report is and how current (RPT-012) -
    /// because pages get separated.
    /// </summary>
    private void Header(IContainer container, ReportResult report, byte[]? logo)
    {
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                if (logo is not null)
                    row.ConstantItem(70).Height(48).AlignMiddle().Image(logo).FitArea();

                row.RelativeItem().PaddingLeft(logo is null ? 0 : 10).Column(details =>
                {
                    if (!string.IsNullOrWhiteSpace(_branding.TradingName))
                        details.Item().Text(_branding.TradingName).FontSize(13).Bold();

                    foreach (var line in Letterhead.Lines(_branding))
                        details.Item().Text(line).FontSize(8).FontColor(Muted);
                });

                row.RelativeItem().AlignRight().Column(title =>
                {
                    title.Item().AlignRight().Text(report.Title).FontSize(15).Bold()
                         .FontColor(Accent);
                    title.Item().AlignRight().Text(report.Provenance.ParameterSummary)
                         .FontSize(7.5f).FontColor(Muted);
                    title.Item().AlignRight().Text(Letterhead.RunBy(report))
                         .FontSize(7).FontColor(Muted);
                });
            });

            column.Item().PaddingTop(6).LineHorizontal(1).LineColor(Accent);
        });
    }

    /// The headline figures as a row of tiles.
    private static void Figures(IContainer container, IReadOnlyList<ReportFigure> figures)
    {
        container.Table(table =>
        {
            var across = Math.Min(figures.Count, 5);

            table.ColumnsDefinition(columns =>
            {
                for (var i = 0; i < across; i++) columns.RelativeColumn();
            });

            foreach (var figure in figures)
                table.Cell().Padding(3).Border(0.5f).BorderColor("#D1D5DB")
                     .Background(Banding).Padding(6).Column(tile =>
                     {
                         tile.Item().Text(figure.Label).FontSize(7.5f).FontColor(Muted);
                         tile.Item().Text(figure.Value).FontSize(12).Bold();
                     });
        });
    }

    private static void Table(IContainer container, ReportSection section)
    {
        var widths = Widths(section.Columns, section.Rows);

        container.Table(table =>
        {
            // Each column as wide as what it holds, within limits:
            // shared out equally, fifteen columns left an item's
            // name wrapping a word to a line beside a date column
            // with room to spare.
            table.ColumnsDefinition(columns =>
            {
                foreach (var width in widths)
                    columns.RelativeColumn(width);
            });

            table.Header(h =>
            {
                foreach (var column in section.Columns)
                    h.Cell().Background(HeaderFill).PaddingVertical(3).PaddingHorizontal(3)
                     .AlignRight(column.RightAlign)
                     .Text(column.Header).Bold();
            });

            for (var i = 0; i < section.Rows.Count; i++)
            {
                var row = section.Rows[i];
                var fill = i % 2 == 1 ? Banding : "#FFFFFF";

                foreach (var column in section.Columns)
                {
                    row.TryGetValue(column.Key, out var value);

                    table.Cell().Background(fill).PaddingVertical(2).PaddingHorizontal(3)
                         .AlignRight(column.RightAlign)
                         .Text(Render(value, column.Format));
                }
            }
        });
    }

    private byte[]? ReadLogo()
    {
        var path = _branding.LogoPath;

        try
        {
            return string.IsNullOrWhiteSpace(path) || !File.Exists(path)
                ? null
                : File.ReadAllBytes(path);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// How much of the page each column gets: the longest of its header
    /// and its values, read from the first few hundred rows, between a
    /// short number's width and a long name's.
    /// </summary>
    public static IReadOnlyList<float> Widths(ReportResult report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return Widths(report.Columns, report.Rows);
    }

    private static List<float> Widths(IReadOnlyList<ReportColumn> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        var sample = rows.Take(400).ToList();

        return columns
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
