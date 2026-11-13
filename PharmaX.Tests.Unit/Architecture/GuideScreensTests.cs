using FluentAssertions;
using PharmaX.Application.Assistant;
using PharmaX.Desktop.Features.Assistant;

namespace PharmaX.Tests.Unit.Architecture;

public class GuideScreensTests
{
    [Fact]
    public void A_section_is_cut_after_each_line_that_names_something_on_its_screen()
    {
        const string section = "Dispense and issue\n\n**Dispensary > Dispensing** (F2).\n\n"
                               + "1. **Find script**. Pick the patient.\n"
                               + "2. The lines show.\n"
                               + "3. **Issue** and **Find script** again. Labels print.\n"
                               + "4. **Nowhere** on this screen.";

        var pieces = GuideScreens.Split(section, c => c is "Find script" or "Issue");

        pieces.Should().HaveCount(3);
        pieces[0].Captions.Should().Equal("Find script");
        pieces[1].Captions.Should().Equal("Issue", "Find script");
        pieces[2].Captions.Should().BeNull();
        pieces[0].Text.Should().EndWith("1. **Find script**. Pick the patient.\n");
        string.Concat(pieces.Select(p => p.Text)).Should().Be(section, "nothing of the guide is lost");
    }

    [Fact]
    public void A_section_naming_nothing_on_its_screen_shows_the_screen_once_under_its_menu_path()
    {
        const string section = "Merge\n\n**Dispensary > Merge Patient Files**.\n\n1. Pick both.";

        var pieces = GuideScreens.Split(section, _ => false);

        pieces.Should().HaveCount(2);
        pieces[0].Captions.Should().BeEmpty();
        pieces[0].Text.Should().EndWith("**Dispensary > Merge Patient Files**.\n");
        pieces[1].Captions.Should().BeNull();
    }

    [Theory]
    [InlineData("F2 Find script", "find script")]
    [InlineData("&Issue", "issue")]
    [InlineData("X-read Ctrl+X", "x-read")]
    [InlineData("Cash up and close (Ctrl+Z)", "cash up and close")]
    [InlineData("Money out...", "money out")]
    [InlineData("Esc Cancel", "cancel")]
    public void Captions_compare_as_the_guide_writes_them(string caption, string expected) =>
        GuideScreens.Normalise(caption).Should().Be(expected);

    [Theory]
    [InlineData("how do I dispense?", "dispensing")]
    [InlineData("hold a sale", "pos")]
    [InlineData("money out", "money-out")]
    [InlineData("stocktake", "stocktake")]
    [InlineData("role permissions", "role-permissions")]
    [InlineData("change my password", "sign-in")]
    public void A_section_is_about_its_menu_path_or_the_screen_named_for_its_title(string question, string key) =>
        GuideScreens.ScreenOf(HowTo.Answer(question)!).Should().Be(key);

    [Fact]
    public void Every_window_the_guide_can_show_is_drawn_and_steps_are_outlined_on_the_right_one()
    {
        var failed = new List<string>();

        OnUiThread(() =>
        {
            foreach (var key in GuideScreens.Keys)
            {
                using var screen = GuideScreens.Draw(key);
                if (screen?.Count != GuideScreens.WindowsOf(key)) failed.Add(key);
            }

            using var dispensing = GuideScreens.Draw("dispensing")!;
            dispensing.Has("Find script").Should().BeTrue();
            dispensing.Has("Issue").Should().BeTrue();
            RedIn(dispensing, "Find script").Should().BeGreaterThan(20, "the button the step names is outlined");

            // Tender is on the payment window Pay opens, not on Sell itself.
            using var sell = GuideScreens.Draw("pos")!;
            sell.Has("Tender").Should().BeTrue();
            using var tender = sell.Outlined(new[] { "Tender" });
            using var pay = sell.Outlined(new[] { "Pay" });
            tender.Size.Should().NotBe(pay.Size, "the two are on different windows");
        });

        failed.Should().BeEmpty();
    }

    private static int RedIn(ScreenDrawing drawing, string caption)
    {
        using var picture = drawing.Outlined(new[] { caption });
        var red = 0;
        for (var x = 0; x < picture.Width; x += 2)
        for (var y = 0; y < picture.Height; y += 2)
            if (picture.GetPixel(x, y).ToArgb() == Color.Red.ToArgb()) red++;
        return red;
    }

    private static void OnUiThread(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new InvalidOperationException("On the UI thread", error);
    }
}
