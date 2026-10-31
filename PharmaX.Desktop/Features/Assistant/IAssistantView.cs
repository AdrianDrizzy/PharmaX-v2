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

    /// Ends the answer, with the disclaimer under it.
    void EndAnswer(string disclaimer);

    /// The line under the question box: what the assistant is doing.
    void ShowStatus(string message);

    /// <summary>
    /// A second person's own sign-in for a permission, on the usual
    /// authorisation box; their id, or null where nobody signed in.
    /// </summary>
    Guid? PromptAuthorisation(string reason, string permissionCode);

    event EventHandler? AskRequested;    // Enter, or Ask
    event EventHandler? CloseRequested;
}
