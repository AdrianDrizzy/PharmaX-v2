using PharmaX.Application.Assistant;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Assistant;

/// <summary>
/// Ask PharmaX: a question in, an answer out, from the offline model, with
/// the disclaimer under every answer. Open from any screen with
/// <see cref="PhxFormBase.AssistantKey"/>.
/// </summary>
public sealed class AssistantPresenter : PresenterBase<IAssistantView>
{
    private readonly IChatAssistant _chat;

    /// The conversation so far, so a follow-up question has its context.
    private readonly List<ChatTurn> _turns = new();

    public AssistantPresenter(IChatAssistant chat) => _chat = chat;

    protected override void Subscribe()
    {
        View.AskRequested += (_, _) => Enqueue(AskAsync);
        View.CloseRequested += (_, _) => View.CloseView(true);
    }

    public override Task InitialiseAsync()
    {
        if (!_chat.IsAvailable)
        {
            View.ShowUnavailable("The assistant's model is not installed on this computer, so "
                                 + "there is nothing to ask yet.");
            return Task.CompletedTask;
        }

        // Loading the model takes seconds; begun now, it is usually done
        // by the time the question is typed.
        View.ShowStatus("Starting the assistant...");
        _ = WarmUpQuietlyAsync();
        return Task.CompletedTask;
    }

    private async Task WarmUpQuietlyAsync()
    {
        try
        {
            await _chat.WarmUpAsync();
            View.ShowStatus("");
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // The window closed first.
        }
#pragma warning disable CA1031 // Logged; the question itself reports any failure.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ErrorHandler.Log(ex);
            View.ShowStatus("");
        }
    }

    private Task AskAsync() => GuardAsync(async () =>
    {
        var question = View.QuestionText.Trim();
        if (question.Length == 0) return;

        View.ClearQuestion();
        View.ShowQuestion(question);
        View.BeginAnswer();
        View.ShowStatus(_chat.IsReady
            ? "Thinking..."
            : "Starting the assistant - the first answer takes a little longer...");

        // Created here, on the screen's thread, so each piece of the
        // answer is written on it too.
        var writing = new Progress<string>(View.AppendAnswer);

        try
        {
            var answer = await _chat.AskAsync(_turns.ToArray(), question, writing);
            _turns.Add(new ChatTurn(question, answer));
        }
        finally
        {
            View.EndAnswer(ChatDisclaimer.Text);
            View.ShowStatus("");
        }
    });
}
