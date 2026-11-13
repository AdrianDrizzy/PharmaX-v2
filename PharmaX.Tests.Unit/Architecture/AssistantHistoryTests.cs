using FluentAssertions;
using PharmaX.Desktop.Features.Assistant;

namespace PharmaX.Tests.Unit.Architecture;

public sealed class AssistantHistoryTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "phx-history-" + Guid.NewGuid().ToString("N"));

    public AssistantHistoryTests() => AssistantHistory.Folder = _folder;

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void Todays_chats_are_listed_newest_first_for_their_user_only_and_other_days_are_deleted()
    {
        var me = Guid.NewGuid();
        var someoneElse = Guid.NewGuid();
        var today = new DateOnly(2026, 11, 13);
        var (first, second) = (Guid.NewGuid(), Guid.NewGuid());

        AssistantHistory.Save(me, today.AddDays(-1), Guid.NewGuid(),
            new AssistantHistory.Chat("Old", DateTime.Now.AddDays(-1), "{\\rtf1 yesterday}", new()));
        AssistantHistory.Save(me, today, first,
            new AssistantHistory.Chat("Cash up", DateTime.Now.AddHours(-2), "{\\rtf1 cash up}", new() { new(5, new byte[] { 1, 2, 3 }) }));
        AssistantHistory.Save(me, today, second,
            new AssistantHistory.Chat("Sales for March", DateTime.Now, "{\\rtf1 sales}", new()));

        AssistantHistory.Today(me, today).Select(c => (c.Id, c.Title)).Should()
            .Equal((second, "Sales for March"), (first, "Cash up"));
        AssistantHistory.Load(me, today, first)!.Pictures.Single().Png.Should().Equal(1, 2, 3);
        AssistantHistory.Today(someoneElse, today).Should().BeEmpty();
        AssistantHistory.Load(someoneElse, today, first).Should().BeNull();

        var files = Directory.EnumerateFiles(_folder, "*", SearchOption.AllDirectories).ToList();
        files.Should().HaveCount(2, "yesterday's is gone");
        files.Should().AllSatisfy(f => File.ReadAllText(f).Should().NotContain("cash up", "it is encrypted on disk"));

        AssistantHistory.Delete(me, today, first);
        AssistantHistory.Today(me, today).Select(c => c.Id).Should().Equal(second);
    }

    [Theory]
    [InlineData("Can you please tell me what the side effects of amlodipine are?", "Side effects of amlodipine")]
    [InlineData("How do I do a cash up?", "Cash up")]
    [InlineData("please give me the sales report for March 2026", "Sales report for March 2026")]
    [InlineData("What dose of amoxicillin should a child of 6 take with food", "Dose of amoxicillin should a child...")]
    [InlineData("Panado", "Panado")]
    [InlineData("How?", "How")]
    public void A_chat_is_named_in_a_few_words_from_its_first_question(string question, string title) =>
        PharmaX.Application.Assistant.ChatTitle.Summarise(question).Should().Be(title);
}
