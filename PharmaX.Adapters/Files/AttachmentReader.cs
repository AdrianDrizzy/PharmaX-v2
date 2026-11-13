using System.Text;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using PharmaX.Application.Assistant;
using Tesseract;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

namespace PharmaX.Adapters.Files;

/// <summary>
/// The text of a file attached to a question, read here on this computer:
/// nothing is sent anywhere.
/// </summary>
/// <remarks>
/// Read only as far as the model could take - a thousand-page PDF stops
/// at the first pages that fill <see cref="Attachments.MostCharacters"/>.
/// </remarks>
public sealed class AttachmentReader : IAttachmentReader
{
    public async Task<string> ReadAsync(string path, CancellationToken ct = default)
    {
        if (!Attachments.CanAttach(path))
            throw new NotSupportedException($"{Path.GetFileName(path)} is not a kind of file Ask PharmaX can read.");

        if (Attachments.IsPicture(path)) return await PictureAsync(path);

        return Path.GetExtension(path).ToUpperInvariant() switch
        {
            ".PDF" => await Task.Run(() => Pdf(path), ct),
            ".XLSX" or ".XLSM" => await Task.Run(() => Workbook(path), ct),
            ".DOCX" => await Task.Run(() => Word(path), ct),
            _ => await File.ReadAllTextAsync(path, ct)
        };
    }

    private static string Pdf(string path)
    {
        using var pdf = PdfDocument.Open(path);
        var text = new StringBuilder();

        foreach (var page in pdf.GetPages())
        {
            // FormKC: a PDF's "ti" and "fi" ligatures back to two letters each.
            text.AppendLine(ContentOrderTextExtractor.GetText(page).Normalize(NormalizationForm.FormKC)).AppendLine();
            if (text.Length > Attachments.MostCharacters) break;
        }

        return text.ToString().Trim().Length > 0
            ? text.ToString()
            : "(This PDF has no text in it - it may be a scanned page. Attach it as a picture to have it read.)";
    }

    /// Every sheet a row at a time, its cells as Excel shows them, tab apart.
    private static string Workbook(string path)
    {
        using var workbook = new XLWorkbook(path);
        var text = new StringBuilder();

        foreach (var sheet in workbook.Worksheets)
        {
            text.Append("Sheet: ").AppendLine(sheet.Name);

            foreach (var row in sheet.RowsUsed())
            {
                text.AppendLine(string.Join('\t', row.Cells(1, row.LastCellUsed().Address.ColumnNumber)
                                                     .Select(c => c.GetFormattedString())));
                if (text.Length > Attachments.MostCharacters) return text.ToString();
            }

            text.AppendLine();
        }

        return text.ToString();
    }

    private static string Word(string path)
    {
        using var document = WordprocessingDocument.Open(path, false);
        var body = document.MainDocumentPart?.Document.Body;

        return body is null
            ? ""
            : string.Join(Environment.NewLine,
                body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>().Select(p => p.InnerText));
    }

    /// <summary>
    /// The words in a picture, by the recognition Windows 10 and 11 carry
    /// where it is installed, and otherwise by the Tesseract shipped with PharmaX.
    /// </summary>
    private static async Task<string> PictureAsync(string path)
    {
        // English (South Africa) and others come without it; any language that has it will do.
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
                     ?? OcrEngine.AvailableRecognizerLanguages.Select(OcrEngine.TryCreateFromLanguage)
                                 .FirstOrDefault(e => e is not null);
        if (engine is null) return await Task.Run(() => Shipped(path));

        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
        using var stream = await file.OpenAsync(FileAccessMode.Read);
        var decoder = await BitmapDecoder.CreateAsync(stream);

        // A phone photo can be bigger than the recognition takes.
        var scale = Math.Min(1d, (double)OcrEngine.MaxImageDimension
                                 / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)(decoder.PixelWidth * scale),
            ScaledHeight = (uint)(decoder.PixelHeight * scale)
        };

        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
        var result = await engine.RecognizeAsync(bitmap);

        return Words(string.Join(Environment.NewLine, result.Lines.Select(l => l.Text)));
    }

    /// Tesseract's English, from the tessdata folder beside PharmaX.
    private static string Shipped(string path)
    {
        using var engine = new TesseractEngine(Path.Combine(AppContext.BaseDirectory, "tessdata"), "eng",
                                               EngineMode.Default);
        using var picture = Pix.LoadFromFile(path);
        using var page = engine.Process(picture);
        return Words(page.GetText());
    }

    private static string Words(string text) =>
        text.Trim().Length == 0 ? "(No words could be read in this picture.)" : text;
}
