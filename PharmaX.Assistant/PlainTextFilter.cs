using System.Text;

namespace PharmaX.Assistant;

/// <summary>
/// Turns the Markdown a model writes out of habit into plain text, as the
/// answer streams in: bold markers dropped, bullets drawn as bullets,
/// heading marks taken off.
/// </summary>
/// <remarks>
/// The answer arrives a few characters at a time, so a marker can be split
/// across two pieces - "*" then "*". A trailing run of marker characters is
/// held back until the next piece shows what it was.
/// </remarks>
public sealed class PlainTextFilter
{
    private const string MarkerCharacters = "*_#";

    private string _held = "";
    private bool _lineStart = true;

    public string Push(string piece)
    {
        var text = _held + (piece ?? "");
        var keep = 0;
        while (keep < text.Length && MarkerCharacters.Contains(text[text.Length - 1 - keep])) keep++;

        _held = text[^keep..];
        return Clean(text[..^keep]);
    }

    /// <summary>Whatever was held back, once the answer has ended.</summary>
    public string Flush()
    {
        var rest = _held;
        _held = "";
        return Clean(rest).Replace("*", "", StringComparison.Ordinal);
    }

    private string Clean(string text)
    {
        var sb = new StringBuilder(text.Length);
        var i = 0;

        while (i < text.Length)
        {
            if (_lineStart)
            {
                var hashes = i;
                while (hashes < text.Length && text[hashes] == '#') hashes++;

                if (hashes > i && hashes < text.Length && text[hashes] == ' ')
                {
                    i = hashes + 1;
                    _lineStart = false;
                    continue;
                }

                if (text[i] is '*' or '-' && i + 1 < text.Length && text[i + 1] == ' ')
                {
                    sb.Append("• ");
                    i += 2;
                    _lineStart = false;
                    continue;
                }
            }

            if (i + 1 < text.Length && text[i] == text[i + 1] && text[i] is '*' or '_')
            {
                i += 2;
                continue;
            }

            sb.Append(text[i]);
            _lineStart = text[i] == '\n';
            i++;
        }

        return sb.ToString();
    }
}
