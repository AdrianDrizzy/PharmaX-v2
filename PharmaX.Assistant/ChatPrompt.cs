using System.Text;
using PharmaX.Application.Assistant;

namespace PharmaX.Assistant;

/// <summary>
/// A conversation in Qwen's chat format: short instructions, the last few
/// turns, and the new question, with thinking switched off.
/// </summary>
/// <remarks>
/// Only the last few turns go in, and long answers are cut: the model
/// reads the whole conversation again for every question, and on a
/// pharmacy PC each extra hundred words is seconds of waiting.
/// </remarks>
public static class ChatPrompt
{
    public const int KeptTurns = 3;
    private const int MaxQuestionLength = 1000;
    private const int MaxEarlierAnswerLength = 800;

    public const string Instructions =
        "You are PharmaX Assistant, helping the staff of a pharmacy. Answer the question "
        + "directly in plain English, in a short paragraph or a few bullet points, as plain "
        + "text without Markdown. "
        + "If you do not know, say so rather than guess.";

    public static string Build(IReadOnlyList<ChatTurn> earlier, string question)
    {
        ArgumentNullException.ThrowIfNull(earlier);

        var sb = new StringBuilder();
        sb.Append("<|im_start|>system\n").Append(Instructions).Append("<|im_end|>\n");

        foreach (var turn in earlier.Skip(Math.Max(0, earlier.Count - KeptTurns)))
        {
            sb.Append("<|im_start|>user\n").Append(Clean(turn.Question, MaxQuestionLength))
              .Append("<|im_end|>\n");
            sb.Append("<|im_start|>assistant\n<think>\n\n</think>\n\n")
              .Append(Clean(turn.Answer, MaxEarlierAnswerLength)).Append("<|im_end|>\n");
        }

        sb.Append("<|im_start|>user\n").Append(Clean(question, MaxQuestionLength))
          .Append("<|im_end|>\n");
        sb.Append("<|im_start|>assistant\n<think>\n\n</think>\n\n");
        return sb.ToString();
    }

    /// <remarks>
    /// The chat is read with its control tokens switched on, so one typed
    /// into a question would end the turn early.
    /// </remarks>
    private static string Clean(string text, int max)
    {
        var cleaned = (text ?? "").Replace("<|", "< |", StringComparison.Ordinal).Trim();
        return cleaned.Length > max ? cleaned[..max] : cleaned;
    }
}
