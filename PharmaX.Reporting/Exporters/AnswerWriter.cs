using System.Globalization;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using PharmaX.Application.Assistant;
using PharmaX.Application.Reporting;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using W = DocumentFormat.OpenXml.Wordprocessing;
using Dw = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using Pic = DocumentFormat.OpenXml.Drawing.Pictures;

namespace PharmaX.Reporting.Exporters;

/// <summary>
/// An answer from Ask PharmaX as a document: its text a line at a time,
/// bold where the conversation shows bold, and its pictures where they sat.
/// </summary>
public sealed class AnswerWriter : IAnswerWriter
{
    private const string Accent = "#2F6DB5";
    private const string Muted = "#6B7280";

    private readonly IReportBranding _branding;

    public AnswerWriter(IReportBranding branding) => _branding = branding;

    /// One line of the answer: its runs of text, or a picture.
    internal sealed record Line(IReadOnlyList<(string Text, bool Bold)> Runs, byte[]? Picture)
    {
        public string Text => string.Concat(Runs.Select(r => r.Text));
    }

    internal static IReadOnlyList<Line> Lines(IReadOnlyList<AnswerPiece> answer)
    {
        var lines = new List<Line>();
        var runs = new List<(string, bool)>();

        foreach (var piece in answer)
        {
            if (piece.Picture is not null)
            {
                if (runs.Count > 0) lines.Add(new Line(runs.ToArray(), null));
                runs.Clear();
                lines.Add(new Line(Array.Empty<(string, bool)>(), piece.Picture));
                continue;
            }

            var parts = piece.Text.Replace("\r", "", StringComparison.Ordinal).Split('\n');
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                {
                    lines.Add(new Line(runs.ToArray(), null));
                    runs.Clear();
                }

                if (parts[i].Length > 0) runs.Add((parts[i], piece.Bold));
            }
        }

        if (runs.Count > 0) lines.Add(new Line(runs.ToArray(), null));

        // No run of blank lines, nor any at either end.
        var kept = new List<Line>();
        foreach (var line in lines)
            if (!Blank(line) || (kept.Count > 0 && !Blank(kept[^1])))
                kept.Add(line);
        while (kept.Count > 0 && Blank(kept[^1])) kept.RemoveAt(kept.Count - 1);

        return kept;
    }

    private static bool Blank(Line line) => line.Picture is null && line.Text.Trim().Length == 0;

    public void WriteTo(string title, IReadOnlyList<AnswerPiece> answer, AnswerFormat format, string path)
    {
        ArgumentNullException.ThrowIfNull(answer);

        var lines = Lines(answer);
        var made = string.Format(CultureInfo.CurrentCulture, "Ask PharmaX - {0:dd/MM/yyyy HH:mm}{1}",
            DateTime.Now, string.IsNullOrWhiteSpace(_branding.TradingName) ? "" : " - " + _branding.TradingName);

        switch (format)
        {
            case AnswerFormat.Pdf: Pdf(title, made, lines, path); break;
            case AnswerFormat.Excel: Excel(title, made, lines, path); break;
            default: Word(title, made, lines, path); break;
        }
    }

    private static void Pdf(string title, string made, IReadOnlyList<Line> lines, string path)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(1.8f, Unit.Centimetre);
            page.DefaultTextStyle(x => x.FontSize(10.5f).FontColor("#1F2937"));

            page.Header().Column(head =>
            {
                head.Item().Text(title).FontSize(15).Bold().FontColor(Accent);
                head.Item().Text(made).FontSize(8).FontColor(Muted);
                head.Item().PaddingTop(6).PaddingBottom(10).LineHorizontal(1).LineColor(Accent);
            });

            page.Content().Column(body =>
            {
                body.Spacing(4);

                foreach (var line in lines)
                {
                    if (line.Picture is not null)
                    {
                        // As big as on screen, smaller only to fit the page: stretched, it blurs.
                        var (width, _) = PngSize(line.Picture);
                        body.Item().PaddingVertical(6).AlignLeft().MaxWidth(width * 72f / 96f)
                            .Border(0.5f).BorderColor("#D1D5DB").Image(line.Picture).FitWidth();
                        continue;
                    }

                    body.Item().Text(text =>
                    {
                        foreach (var (run, bold) in line.Runs)
                        {
                            var span = text.Span(run);
                            if (bold) span.Bold();
                        }
                    });
                }
            });

            page.Footer().AlignRight().Text(text =>
            {
                text.Span("Page ").FontColor(Muted);
                text.CurrentPageNumber().FontColor(Muted);
                text.Span(" of ").FontColor(Muted);
                text.TotalPages().FontColor(Muted);
            });
        })).GeneratePdf(path);
    }

    /// <summary>
    /// A line to a row; a table the answer wrote with | between its cells
    /// keeps them in cells of their own, to be sorted and added up.
    /// </summary>
    private static void Excel(string title, string made, IReadOnlyList<Line> lines, string path)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Answer");
        sheet.Style.Font.FontName = "Calibri";
        sheet.Style.Font.FontSize = 11;

        sheet.Cell(1, 1).Value = title;
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;
        sheet.Cell(2, 1).Value = made;
        sheet.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml(Muted);

        var row = 4;
        var widest = 1;

        foreach (var line in lines)
        {
            if (line.Picture is not null)
            {
                using var png = new MemoryStream(line.Picture);
                var picture = sheet.AddPicture(png).MoveTo(sheet.Cell(row, 1));
                if (picture.Width > 900) picture.Scale(900d / picture.Width);
                row += (int)Math.Ceiling(picture.Height / 20d) + 1;
                continue;
            }

            var text = line.Text.Trim();

            if (text.StartsWith('|') && text.EndsWith('|'))
            {
                // The line under a table's header, |---|---|, is not a row.
                if (text.Trim('|', '-', ':', ' ').Length == 0) continue;

                var cells = text.Trim('|').Split('|');
                for (var c = 0; c < cells.Length; c++)
                {
                    var cell = sheet.Cell(row, c + 1);
                    var value = cells[c].Trim().Replace("**", "", StringComparison.Ordinal);
                    if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out var number))
                        cell.Value = number;
                    else cell.Value = value;
                }

                widest = Math.Max(widest, cells.Length);
            }
            else
            {
                sheet.Cell(row, 1).Value = line.Text;
                sheet.Cell(row, 1).Style.Font.Bold = line.Runs.Count > 0 && line.Runs.All(r => r.Bold);
            }

            row++;
        }

        if (widest > 1) sheet.Columns(1, widest).AdjustToContents(4, row);
        else sheet.Column(1).Width = 110;
        sheet.Column(1).Style.Alignment.WrapText = widest == 1;

        workbook.SaveAs(path);
    }

    private static void Word(string title, string made, IReadOnlyList<Line> lines, string path)
    {
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = document.AddMainDocumentPart();
        var body = new W.Body();
        main.Document = new W.Document(body);

        body.Append(Paragraph((title, true), 32, Accent.TrimStart('#')));
        body.Append(Paragraph((made, false), 16, Muted.TrimStart('#')));

        uint id = 1;
        foreach (var line in lines)
        {
            if (line.Picture is null)
            {
                body.Append(Paragraph(line.Runs.ToArray()));
                continue;
            }

            var image = main.AddImagePart(ImagePartType.Png);
            using (var png = new MemoryStream(line.Picture)) image.FeedData(png);
            body.Append(new W.Paragraph(new W.Run(Picture(main.GetIdOfPart(image), line.Picture, id++))));
        }

        // Narrow margins, so a screen's picture is not shrunk to nothing.
        body.Append(new W.SectionProperties(new W.PageMargin
        {
            Top = 1020, Bottom = 1020, Left = (UInt32Value)1020U, Right = (UInt32Value)1020U
        }));
        main.Document.Save();
    }

    private static W.Paragraph Paragraph(params (string Text, bool Bold)[] runs) => Paragraph(runs, 0, null);

    private static W.Paragraph Paragraph((string Text, bool Bold) run, int halfPoints, string colour) =>
        Paragraph(new[] { run }, halfPoints, colour);

    private static W.Paragraph Paragraph((string Text, bool Bold)[] runs, int halfPoints, string? colour)
    {
        var paragraph = new W.Paragraph();

        foreach (var (text, bold) in runs)
        {
            var properties = new W.RunProperties();
            if (bold) properties.Append(new W.Bold());
            if (colour is not null) properties.Append(new W.Color { Val = colour });
            if (halfPoints > 0) properties.Append(new W.FontSize { Val = halfPoints.ToString(CultureInfo.InvariantCulture) });

            paragraph.Append(new W.Run(properties, new W.Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        }

        return paragraph;
    }

    /// A PNG's size is the first thing in it, after its 16-byte signature and header.
    private static (int Width, int Height) PngSize(byte[] png) =>
        (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)),
         System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));

    /// A picture inline, at most the page's width (17 cm).
    private static W.Drawing Picture(string relationship, byte[] png, uint id)
    {
        var (width, height) = PngSize(png);

        const long EmuPerPixel = 9525;
        const long Widest = 6120000;
        var cx = Math.Min(width * EmuPerPixel, Widest);
        var cy = height * EmuPerPixel * cx / (width * EmuPerPixel);
        var name = "Picture " + id.ToString(CultureInfo.InvariantCulture);

        return new W.Drawing(new Dw.Inline(
            new Dw.Extent { Cx = cx, Cy = cy },
            new Dw.DocProperties { Id = id, Name = name },
            new A.Graphic(new A.GraphicData(
                new Pic.Picture(
                    new Pic.NonVisualPictureProperties(
                        new Pic.NonVisualDrawingProperties { Id = id, Name = name },
                        new Pic.NonVisualPictureDrawingProperties()),
                    new Pic.BlipFill(new A.Blip { Embed = relationship }, new A.Stretch(new A.FillRectangle())),
                    new Pic.ShapeProperties(
                        new A.Transform2D(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = cx, Cy = cy }),
                        new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
            { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
        {
            DistanceFromTop = 0, DistanceFromBottom = 0, DistanceFromLeft = 0, DistanceFromRight = 0
        });
    }
}
