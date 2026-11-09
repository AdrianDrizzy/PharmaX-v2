using System.Text.RegularExpressions;

namespace PharmaX.Desktop.Features.Assistant;

/// <summary>
/// Turns a how-to section into the runs the conversation box draws:
/// button names in bold, and a gap before each numbered step.
/// </summary>
internal static partial class AssistantText
{
    public static IReadOnlyList<(string Text, bool Bold)> Parts(string markdown)
    {
        // An odd number means a mark was cut in half, as a model answer
        // arrives a few words at a time. Leave that chunk as it came.
        if (markdown.Split("**").Length % 2 == 0)
            return new[] { (markdown, false) };

        var spaced = StepGap().Replace(markdown, "$1$1$2");
        var split = Bold().Split(spaced);
        var parts = new List<(string Text, bool Bold)>();

        for (var i = 0; i < split.Length; i++)
            if (split[i].Length > 0)
                parts.Add((split[i], i % 2 == 1));

        return parts;
    }

    public static string Plain(string markdown) =>
        string.Concat(Parts(markdown).Select(p => p.Text));

    [GeneratedRegex(@"(?<=\S)(\r?\n)(\d+\. )", RegexOptions.CultureInvariant)]
    private static partial Regex StepGap();

    [GeneratedRegex(@"\*\*(.+?)\*\*", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Bold();
}
