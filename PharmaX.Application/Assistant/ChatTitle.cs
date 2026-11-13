using System.Text.RegularExpressions;

namespace PharmaX.Application.Assistant;

/// <summary>
/// A chat's name in a few words, from its first question: "Can you please
/// tell me what the side effects of amlodipine are?" is "Side effects of
/// amlodipine".
/// </summary>
public static partial class ChatTitle
{
    private const int MostWords = 6;

    public static string Summarise(string question)
    {
        var text = Regex.Replace((question ?? "").Trim(), @"\s+", " ").TrimEnd('?', '.', '!', ' ');

        // Openings go one after another: "can you please tell me what the...".
        string before;
        do
        {
            before = text;
            text = Opening().Replace(text, "").Trim();
        } while (text != before);

        text = Closing().Replace(text, "").Trim();
        if (text.Length == 0) return (question ?? "").Trim();

        var words = text.Split(' ');
        text = words.Length > MostWords ? string.Join(' ', words.Take(MostWords)) + "..." : text;
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    [GeneratedRegex(@"^(please|kindly|hi|hello|hey|can you|could you|would you|will you|can i|could i|i want to|i would like to|i'd like to|i need to|i want|i need|help me( to)?|tell me( about)?|show me|give me|explain|let me know|do you know|how do i|how do we|how can i|how can we|how to|how should i|where do i|where can i|what is|what are|what's|what were|what was|who is|is there|are there|do we have|what|which|do|a|an|the|me|to|about)\b[,:]?\s*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Opening();

    [GeneratedRegex(@"\s+(please|for me|thanks|thank you|are|is)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Closing();
}
