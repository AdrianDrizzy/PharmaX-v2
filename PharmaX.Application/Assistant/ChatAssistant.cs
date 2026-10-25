namespace PharmaX.Application.Assistant;

/// <summary>One question and the answer it got, kept so a follow-up has context.</summary>
public sealed record ChatTurn(string Question, string Answer);

/// <summary>
/// A general question-and-answer assistant, open from every screen, run by
/// the same offline model as the reports assistant.
/// </summary>
/// <remarks>
/// Unlike the reports assistant it writes free text, so it can be wrong -
/// about medicines as about anything else - and every answer carries
/// <see cref="ChatDisclaimer.Text"/>. Nothing leaves the computer, which
/// also means it knows only what the model learned, not today's news.
/// </remarks>
public interface IChatAssistant
{
    /// <summary>False where no model is installed here.</summary>
    bool IsAvailable { get; }

    /// <summary>True once the model is loaded, after which answers start in seconds.</summary>
    bool IsReady { get; }

    /// <summary>Loads the model ahead of the first question.</summary>
    Task WarmUpAsync(CancellationToken ct = default);

    /// <summary>
    /// The answer to a question, given the conversation so far, reported
    /// to <paramref name="writing"/> a few words at a time as it is made.
    /// </summary>
    Task<string> AskAsync(IReadOnlyList<ChatTurn> earlier, string question,
        IProgress<string> writing, CancellationToken ct = default);
}

public static class ChatDisclaimer
{
    public const string Text =
        "AI answer from an offline model - it can be wrong. Check medicine information "
        + "against an approved reference or with a pharmacist before acting on it.";
}
