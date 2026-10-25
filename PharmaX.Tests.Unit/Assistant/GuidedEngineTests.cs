using FluentAssertions;
using PharmaX.Application.Assistant;
using PharmaX.Application.Assistant.Guided;

namespace PharmaX.Tests.Unit.Assistant;

/// <summary>
/// The guided-flow engine every Ask PharmaX job runs on: one question at a
/// time, notes carried over, a child flow handed the conversation and given
/// it back, and whatever was begun undone when the job is stopped.
/// </summary>
public class GuidedEngineTests
{
    /// Two questions, the first passing over a step with a note, then a summary.
    private sealed class Sample : GuidedFlow
    {
        public int Abandoned { get; private set; }
        public string? Name { get; private set; }

        public Sample(Func<GuidedFlow?>? child = null)
        {
            Add("name", () => "Name?", answer =>
            {
                if (answer == "child" && child?.Invoke() is { } flow)
                    return new Reply.Handover(flow, (c, _) => Task.FromResult(
                        c.Result is null ? Reply.Finish("Child stopped.") : Reply.Ok($"Back with {c.Result.Reference}.")));

                Name = answer;
                return Reply.Ok();
            });
            Add("skipped", _ =>
            {
                Say("Nothing to ask here.");
                return Task.FromResult("");
            }, (string _) => Reply.Ok());
            Add("age", () => "Age?", answer => Answers.Number(answer) is > 0m
                ? Reply.Ok()
                : Reply.Retry("A number, please."));
            AddConfirm(_ => Task.FromResult($"Name: {Name}"), "Save it?", _ =>
            {
                Result = new GuidedResult(AppTaskKind.NewPatient, Guid.Empty, Name ?? "");
                return Task.FromResult(Reply.Finish("Saved."));
            });
        }

        protected override Task<string> IntroAsync(CancellationToken ct) => Task.FromResult("Hello.");

        protected override Task AbandonAsync(CancellationToken ct)
        {
            Abandoned++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task A_question_passed_over_leaves_its_note_with_the_next()
    {
        var flow = new Sample();
        await flow.StartAsync();

        var said = await flow.ReplyAsync("Mary");

        said.Should().Be($"Nothing to ask here.{Environment.NewLine}Age?");
        flow.Waiting.Should().Be("age");
    }

    [Fact]
    public async Task A_refused_answer_says_why_and_asks_again()
    {
        var flow = new Sample();
        await flow.StartAsync();
        await flow.ReplyAsync("Mary");

        (await flow.ReplyAsync("old")).Should().Be($"A number, please.{Environment.NewLine}Age?");
        flow.Waiting.Should().Be("age");
    }

    [Fact]
    public async Task Cancel_stops_it_and_undoes_what_it_began()
    {
        var flow = new Sample();
        await flow.StartAsync();

        (await flow.ReplyAsync("cancel")).Should().Be("Stopped. Nothing was saved.");

        flow.IsFinished.Should().BeTrue();
        flow.Abandoned.Should().Be(1);
    }

    [Fact]
    public async Task No_at_the_summary_also_undoes_it()
    {
        var flow = new Sample();
        await flow.StartAsync();
        await flow.ReplyAsync("Mary");
        await flow.ReplyAsync("40");

        (await flow.ReplyAsync("no")).Should().Be("Nothing was saved.");
        flow.Abandoned.Should().Be(1);
    }

    [Fact]
    public async Task Once_done_there_is_nothing_to_undo()
    {
        var flow = new Sample();
        await flow.StartAsync();
        await flow.ReplyAsync("Mary");
        await flow.ReplyAsync("40");
        await flow.ReplyAsync("yes");

        await flow.CancelAsync();

        flow.Abandoned.Should().Be(0);
        flow.Result.Should().NotBeNull();
    }

    [Fact]
    public async Task Change_from_the_summary_asks_that_one_question_and_comes_back()
    {
        var flow = new Sample();
        await flow.StartAsync();
        await flow.ReplyAsync("Mary");
        await flow.ReplyAsync("40");

        (await flow.ReplyAsync("change the name")).Should().Be("Name?");
        (await flow.ReplyAsync("Ruth")).Should().Contain("Name: Ruth").And.EndWith("Save it?");
    }

    [Fact]
    public async Task A_child_flow_takes_the_conversation_and_hands_it_back()
    {
        var child = new Sample();
        var flow = new Sample(() => child);
        await flow.StartAsync();

        (await flow.ReplyAsync("child")).Should().Be($"Hello.{Environment.NewLine}Name?");
        flow.Waiting.Should().Be("child:name");

        await flow.ReplyAsync("Tendai");
        await flow.ReplyAsync("30");
        var back = await flow.ReplyAsync("yes");

        back.Should().Contain("Saved.").And.Contain("Back with Tendai.").And.EndWith("Age?");
        flow.Waiting.Should().Be("age");
    }

    [Theory]
    [InlineData("12", 12)]
    [InlineData("1,200 packs", 1200)]
    [InlineData("2 tablets", 2)]
    [InlineData("PN2611", null)]
    [InlineData("batch AB12", null)]
    public void A_number_is_never_read_out_of_a_code(string said, int? expected) =>
        Answers.Number(said).Should().Be(expected);

    [Theory]
    [InlineData("15", 3, true)]
    [InlineData("0", 3, true)]
    [InlineData("2", 3, false)]
    [InlineData("15", 0, false)]
    [InlineData("amoxil", 3, false)]
    public void A_number_off_the_list_is_a_slip(string said, int listed, bool offList) =>
        Answers.IsOffList(said, listed).Should().Be(offList);
}
