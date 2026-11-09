using FluentAssertions;
using PharmaX.Application.Assistant;

namespace PharmaX.Tests.Unit.Application;

public class HowToTests
{
    [Theory]
    [InlineData("How do I cash up?", "Count cash and close")]
    [InlineData("where is goods receipt", "Sell at")]
    [InlineData("how do I change a price", "Retail price")]
    [InlineData("mass price change", "Whose prices")]
    [InlineData("why can't I see reports", "A cashier sells")]
    public void A_question_about_using_PharmaX_is_answered_from_the_guide(string question, string step) =>
        HowTo.Answer(question).Should().Contain(step);

    [Theory]
    [InlineData("What is paracetamol?")]
    [InlineData("side effects of amlodipine")]
    [InlineData("")]
    public void A_medicine_question_is_left_to_the_model(string question) =>
        HowTo.Answer(question).Should().BeNull();

    [Fact]
    public void The_answer_is_shown_without_asterisks_and_with_a_gap_between_steps()
    {
        var guide = HowTo.Answer("How do I cash up?");
        var shown = PharmaX.Desktop.Features.Assistant.AssistantText.Plain(guide!);

        shown.Should().NotContain("**");
        shown.Should().Contain("File > Cash Up");
        shown.Should().Contain("1. Day");
        shown.Should().Contain("9. Type how many");
        shown.Should().Contain(Environment.NewLine + Environment.NewLine + "2. Shift");
        PharmaX.Desktop.Features.Assistant.AssistantText.Parts(guide!)
            .Should().Contain(p => p.Bold && p.Text == "Declare");
    }

    [Fact]
    public void A_how_to_offers_every_screen_it_names_and_not_a_shorter_name_inside_one()
    {
        var cashUp = PharmaX.Desktop.Features.Assistant.AssistantOpens.In(
            HowTo.Answer("How do I cash up?")!);
        cashUp.Select(o => o.Key).Should().Contain("cash-up");
        cashUp.Select(o => o.Key).Should().NotContain("end-of-day");

        var both = PharmaX.Desktop.Features.Assistant.AssistantOpens.In(
            "Open File > Cash Up, then File > End-of-Day Cash-Up, then Dispensary > Dispensing.");
        both.Select(o => o.Key).Should().BeEquivalentTo("end-of-day", "cash-up", "dispensing");
    }
}
