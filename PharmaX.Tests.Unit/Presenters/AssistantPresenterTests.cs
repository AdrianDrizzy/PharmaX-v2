using FluentAssertions;
using NSubstitute;
using PharmaX.Application.Assistant;
using PharmaX.Desktop.Features.Assistant;

namespace PharmaX.Tests.Unit.Presenters;

/// <summary>
/// Ask PharmaX answers whatever is asked, and every answer carries the
/// disclaimer.
/// </summary>
public class AssistantPresenterTests
{
    private readonly IChatAssistant _chat = Substitute.For<IChatAssistant>();
    private readonly IAssistantView _view = Substitute.For<IAssistantView>();

    public AssistantPresenterTests()
    {
        _chat.IsAvailable.Returns(true);
        _chat.IsReady.Returns(true);
    }

    [Fact]
    public async Task Every_answer_ends_with_the_disclaimer()
    {
        Answers("Common side effects of amlodipine include ankle swelling.");
        _view.QuestionText.Returns("What are the side effects of amlodipine?");

        await OpenAsync();
        _view.AskRequested += Raise.EventWith(this, EventArgs.Empty);

        Received.InOrder(() =>
        {
            _view.ShowQuestion("What are the side effects of amlodipine?");
            _view.BeginAnswer();
            _view.EndAnswer(ChatDisclaimer.Text);
        });
        ChatDisclaimer.Text.Should().Contain("can be wrong");
    }

    [Fact]
    public async Task A_failed_answer_still_gets_the_disclaimer()
    {
        _chat.AskAsync(Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(),
                Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<string>(new InvalidOperationException("The assistant stopped unexpectedly.")));
        _view.QuestionText.Returns("Anything");

        await OpenAsync();
        _view.AskRequested += Raise.EventWith(this, EventArgs.Empty);

        _view.Received().EndAnswer(ChatDisclaimer.Text);
        _view.Received().ShowError(Arg.Any<string>());
    }

    [Fact]
    public async Task A_follow_up_question_carries_the_conversation_so_far()
    {
        Answers("Paracetamol is an analgesic.");
        _view.QuestionText.Returns("What is paracetamol?", "And the usual adult dose?");

        await OpenAsync();
        _view.AskRequested += Raise.EventWith(this, EventArgs.Empty);
        _view.AskRequested += Raise.EventWith(this, EventArgs.Empty);

        await _chat.Received().AskAsync(
            Arg.Is<IReadOnlyList<ChatTurn>>(t => t.Count == 1 && t[0].Question == "What is paracetamol?"),
            "And the usual adult dose?", Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Without_a_model_there_is_nothing_to_ask()
    {
        _chat.IsAvailable.Returns(false);

        await OpenAsync();

        _view.Received().ShowUnavailable(Arg.Is<string>(m => m.Contains("not installed", StringComparison.Ordinal)));
        await _chat.DidNotReceive().WarmUpAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_empty_question_is_not_sent()
    {
        _view.QuestionText.Returns("   ");

        await OpenAsync();
        _view.AskRequested += Raise.EventWith(this, EventArgs.Empty);

        await _chat.DidNotReceive().AskAsync(Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(),
            Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
    }

    private void Answers(string answer) =>
        _chat.AskAsync(Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(),
                Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>())
            .Returns(answer);

    private async Task OpenAsync()
    {
        var presenter = new AssistantPresenter(_chat);
        presenter.Attach(_view);
        await presenter.InitialiseAsync();
    }
}
