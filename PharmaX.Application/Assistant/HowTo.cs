using System.Text.RegularExpressions;

namespace PharmaX.Application.Assistant;

/// <summary>
/// The how-to guide, matched on the Words line of each section and
/// returned word for word. A question about a medicine matches nothing
/// here and is left to the model.
/// </summary>
public static partial class HowTo
{
    /// The section that answers the question, or null where none does.
    public static string? Answer(string question)
    {
        if (string.IsNullOrWhiteSpace(question)) return null;

        var best = "";
        var bestWords = 0;
        var bestHits = 0;

        foreach (var section in Sections)
        {
            var hits = 0;
            var words = 0;

            foreach (var phrase in section.Phrases)
            {
                if (!Regex.IsMatch(question, @"\b" + Regex.Escape(phrase) + @"\b",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    continue;

                hits++;
                words = Math.Max(words, phrase.Split(' ').Length);
            }

            if (words > bestWords || (words == bestWords && words > 0 && hits > bestHits))
            {
                best = section.Text;
                bestWords = words;
                bestHits = hits;
            }
        }

        return bestWords == 0 ? null : best;
    }

    private static IReadOnlyList<Section> Sections { get; } = Read();

    private static List<Section> Read()
    {
        using var stream = typeof(HowTo).Assembly
            .GetManifestResourceStream("PharmaX.Application.Assistant.HowTo.md");

        if (stream is null) return new List<Section>();

        using var reader = new StreamReader(stream);
        var sections = new List<Section>();

        foreach (Match match in SectionPattern().Matches(reader.ReadToEnd()))
        {
            var phrases = match.Groups["words"].Value.Split(',')
                .Select(p => p.Trim().ToLowerInvariant())
                .Where(p => p.Length > 0)
                .Distinct()
                .ToList();

            var title = Number().Replace(match.Groups["title"].Value.Trim(), "");
            sections.Add(new Section(phrases, title + "\n\n" + match.Groups["body"].Value.Trim()));
        }

        return sections;
    }

    private sealed record Section(IReadOnlyList<string> Phrases, string Text);

    // A blank line sits between the heading and Words:, and the file is
    // saved with Windows line endings, so the gap is two CRLFs.
    [GeneratedRegex(@"^#{2,3} +(?<title>[^\r\n]+)\r?\n(?:\r?\n)*Words: (?<words>[^\r\n]+)\r?\n(?<body>[\s\S]*?)(?=\r?\n#{1,3} |\z)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex SectionPattern();

    [GeneratedRegex(@"^\d+\.\s*", RegexOptions.CultureInvariant)]
    private static partial Regex Number();
}
