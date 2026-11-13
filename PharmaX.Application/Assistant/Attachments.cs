using System.Text;
using System.Text.RegularExpressions;

namespace PharmaX.Application.Assistant;

/// <summary>
/// The text of a file attached to a question: read from a PDF, workbook,
/// Word document or text file, and from a picture by Windows' own text
/// recognition.
/// </summary>
public interface IAttachmentReader
{
    /// <exception cref="NotSupportedException">A file of a kind not attachable.</exception>
    Task<string> ReadAsync(string path, CancellationToken ct = default);
}

/// <summary>What may be attached to a question, and how the model is asked about it.</summary>
public static class Attachments
{
    public static readonly IReadOnlyList<string> Pictures =
        new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };

    public static readonly IReadOnlyList<string> Documents =
        new[] { ".pdf", ".xlsx", ".xlsm", ".csv", ".docx", ".txt", ".md" };

    /// <summary>
    /// Most of all the files' text put to the model: its whole memory is
    /// 8192 tokens, shared with the conversation and the answer.
    /// </summary>
    public const int MostCharacters = 9000;

    public static bool IsPicture(string path) => Is(path, Pictures);

    /// Not a video, or anything else nothing here can read.
    public static bool CanAttach(string path) => IsPicture(path) || Is(path, Documents);

    private static bool Is(string path, IReadOnlyList<string> kinds) =>
        kinds.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The question as the model is asked it: each file's text, cut to fit
    /// between them, then what was asked of it.
    /// </summary>
    public static string Question(string question, IReadOnlyList<(string Name, string Text)> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var each = files.Count == 0 ? 0 : MostCharacters / files.Count;
        var asked = new StringBuilder();

        foreach (var (name, text) in files)
        {
            var body = text.Trim();
            if (body.Length > each) body = body[..each] + "\n[... the rest of the file was cut to fit]";

            asked.Append("Attached file \"").Append(name).AppendLine("\":")
                 .AppendLine("<<<").AppendLine(body).AppendLine(">>>").AppendLine();
        }

        asked.Append(string.IsNullOrWhiteSpace(question)
            ? "Summarise what the attached file says, briefly."
            : question.Trim());

        return asked.ToString();
    }
}

public enum AnswerFormat { Pdf, Excel, Word }

/// <summary>
/// A run of an answer as the conversation shows it: text, bold or not, or
/// a picture as PNG.
/// </summary>
public sealed record AnswerPiece(string Text, bool Bold = false, byte[]? Picture = null);

/// <summary>An answer saved as a document, at a path the operator chose.</summary>
public interface IAnswerWriter
{
    void WriteTo(string title, IReadOnlyList<AnswerPiece> answer, AnswerFormat format, string path);
}

/// <summary>"Save that as a PDF", "give me this in Excel": an answer wanted as a file.</summary>
public static partial class AnswerExport
{
    /// The format asked for, or null where none is.
    public static AnswerFormat? Requested(string question)
    {
        // "How do I export to Excel?" is a question about PharmaX.
        if (How().IsMatch(question) || !Verb().IsMatch(question)) return null;

        var format = Format().Match(question);
        if (!format.Success) return null;

        return format.Value.ToUpperInvariant() switch
        {
            "PDF" => AnswerFormat.Pdf,
            "WORD" or "DOCX" or "DOC" => AnswerFormat.Word,
            _ => AnswerFormat.Excel
        };
    }

    /// <summary>
    /// True where the question asks only for the last answer as a file -
    /// "save that as a PDF" - rather than for something new to be written
    /// as one.
    /// </summary>
    public static bool OnlyAsksToSave(string question) =>
        Requested(question) is not null
        && Leftover().Replace(Format().Replace(question, " "), " ").Trim().Length == 0;

    [GeneratedRegex(@"^\s*(how|where|why|what)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex How();

    [GeneratedRegex(@"\b(save|export|download|output|give|send|put|turn|make|convert|have|write)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Verb();

    [GeneratedRegex(@"\b(pdf|excel|xlsx|spreadsheet|workbook|word|docx|doc)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Format();

    [GeneratedRegex(@"\b(please|now|can|could|you|me|i|want|need|save|export|download|output|give|send|put|"
                    + @"turn|make|convert|have|write|it|that|this|the|last|above|answer|reply|conversation|chat|as|a|an|"
                    + @"into|in|to|file|document|format|for|of|one)\b|[^\w]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Leftover();
}
