using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Assistant;

public interface IAssistantView : IView
{
    string QuestionText { get; }
    void ClearQuestion();

    /// No model on this computer: says so and closes the way in.
    void ShowUnavailable(string message);

    void ShowQuestion(string question);

    /// Starts an answer in the conversation, to be written into as it comes.
    void BeginAnswer();
    void AppendAnswer(string text);

    /// A section of the how-to guide, with pictures of the screen it is
    /// about where the window can draw them.
    void AppendGuide(string section);

    /// Ends the answer, with the disclaimer under it.
    void EndAnswer(string disclaimer);

    /// Buttons under the answer, one per screen it names. Clicking one
    /// opens that screen; several can be open at once.
    void ShowOpens(IReadOnlyList<(string Key, string Caption)> opens);

    /// The menu key of a screen button the operator clicked.
    event Action<string>? OpenRequested;

    /// The line under the question box: what the assistant is doing.
    void ShowStatus(string message);

    /// <summary>
    /// A second person's own sign-in for a permission, on the usual
    /// authorisation box; their id, or null where nobody signed in.
    /// </summary>
    Guid? PromptAuthorisation(string reason, string permissionCode);

    /// The files attached to the next question, shown under it once asked.
    IReadOnlyList<string> AttachedFiles { get; }
    void ClearAttachments();

    /// The last answer as the conversation shows it, pictures and all.
    IReadOnlyList<Application.Assistant.AnswerPiece> LastAnswer { get; }

    /// Where to save the answer, from the save box; null where cancelled.
    string? AskSavePath(Application.Assistant.AnswerFormat format, string suggestedName);

    /// A better name for the chat than its first question, once: a guide section's title, a report's.
    void NameChat(string title);

    /// A picture in the answer, as PNG: a report's graph.
    void AppendPicture(byte[] png);

    /// Just saved: asks whether to open it, and opens it if so.
    void OfferToOpen(string path);

    /// Save answer, and the format chosen.
    event Action<Application.Assistant.AnswerFormat>? SaveRequested;

    event EventHandler? AskRequested;    // Enter, or Ask
    event EventHandler? CloseRequested;
}
