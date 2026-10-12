using FluentAssertions;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Tests.Unit.Adapters;

/// <summary>
/// Whichever mode a till chooses, the words stay readable - on the page, in
/// a box, on a row flagged for attention or a problem - and a change of
/// mode reaches the colours an open window already holds.
/// </summary>
/// <remarks>
/// In one collection with PaletteTests: the theme is static, and two
/// classes changing it at once would read each other's colours.
/// </remarks>
[Collection("PhxTheme")]
public class ThemeModeTests
{
    private static readonly Color Amber = Color.FromArgb(255, 244, 214);

    public static TheoryData<string> Modes()
    {
        var modes = new TheoryData<string>();
        foreach (var mode in ThemeMode.All) modes.Add(mode.Id);
        return modes;
    }

    private static void Reset() =>
        PhxTheme.Use(Palette.NavyAndOrange, ThemeMode.Light);

    [Theory]
    [MemberData(nameof(Modes))]
    public void Every_mode_keeps_its_words_readable(string id)
    {
        PhxTheme.Use(Palette.NavyAndOrange, ThemeMode.Resolve(id));

        try
        {
            foreach (var (ground, name) in new[]
            {
                (PhxTheme.Surface, "page"), (PhxTheme.Field, "box"),
                (PhxTheme.Attention, "attention row"),
                (PhxTheme.Problem, "problem row"),
                (PhxTheme.Focus, "box being typed in")
            })
                Palette.Contrast(PhxTheme.Ink, ground).Should()
                    .BeGreaterOrEqualTo(4.5, $"ink on the {name} in {id}");

            Palette.Contrast(PhxTheme.ProblemText, PhxTheme.Surface).Should()
                .BeGreaterOrEqualTo(4.5, $"an error on the page in {id}");

            // A tile's name and icon, and the quieter words under it: on a
            // dark page these are meant to be bright, not merely legible.
            var bright = ThemeMode.Resolve(id).IsDark ? 7.0 : 4.5;

            Palette.Contrast(PhxTheme.Title, PhxTheme.Card).Should()
                .BeGreaterOrEqualTo(bright, $"a title on a card in {id}");
            Palette.Contrast(PhxTheme.Title, PhxTheme.Surface).Should()
                .BeGreaterOrEqualTo(4.5, $"a title on the page in {id}");
            Palette.Contrast(PhxTheme.Muted, PhxTheme.Surface).Should()
                .BeGreaterOrEqualTo(4.5, $"muted words on the page in {id}");
            PhxTheme.Title.Should().NotBe(PhxTheme.Brand,
                "Recolour tells words from fills by their colour");
        }
        finally
        {
            Reset();
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("neon-pink")]
    public void An_unknown_saved_mode_falls_back_to_light(string? id) =>
        ThemeMode.Resolve(id).Should().Be(ThemeMode.Light);

    [Fact]
    public void Changing_mode_swaps_the_flag_colours_and_the_designers_system_colours()
    {
        Reset();
        var attention = PhxTheme.Attention;
        var problemText = PhxTheme.ProblemText;

        IReadOnlyDictionary<Color, Color>? seen = null;
        void Watch(object? sender, IReadOnlyDictionary<Color, Color> changes) =>
            seen = changes;

        PhxTheme.Changed += Watch;

        try
        {
            PhxTheme.Use(Palette.NavyAndOrange, ThemeMode.Midnight);

            seen.Should().NotBeNull();
            seen![attention].Should().Be(PhxTheme.Attention);
            seen[problemText].Should().Be(PhxTheme.ProblemText);
            seen[SystemColors.GrayText].Should().Be(PhxTheme.Muted,
                "a caption greyed in a designer would be unreadable on navy");
            seen[SystemColors.WindowText].Should().Be(PhxTheme.Ink);
        }
        finally
        {
            PhxTheme.Changed -= Watch;
            Reset();
        }
    }

    [Fact]
    public void A_logo_on_a_dark_page_loses_its_white_and_its_navy_is_lifted()
    {
        var navy = Color.FromArgb(33, 56, 134);
        var orange = Color.FromArgb(243, 106, 34);
        var page = ThemeMode.Midnight.Paper;

        // White paper, a navy block with a white window cut in it, an
        // orange block.
        using var logo = new Bitmap(120, 60);
        using (var graphics = Graphics.FromImage(logo))
        {
            graphics.Clear(Color.White);
            using var dark = new SolidBrush(navy);
            using var bright = new SolidBrush(orange);
            graphics.FillRectangle(dark, 10, 10, 50, 40);
            graphics.FillRectangle(Brushes.White, 25, 22, 20, 16);
            graphics.FillRectangle(Brushes.White, 14, 42, 3, 3);   // the hole in an "e"
            graphics.FillRectangle(bright, 70, 10, 40, 40);
        }

        using var made = Branding.ForPage(logo, page, dark: true);

        made.GetPixel(2, 2).A.Should().Be(0, "the paper round the logo is gone");
        made.GetPixel(35, 30).Should().Be(Color.FromArgb(255, 255, 255),
            "a white shape inside the logo is part of it");
        made.GetPixel(15, 43).A.Should().Be(0,
            "the hole in a letter is the paper seen through it");
        made.GetPixel(90, 30).ToArgb().Should().Be(orange.ToArgb(),
            "a colour that already reads is left alone");
        Palette.Contrast(made.GetPixel(15, 15), page).Should().BeGreaterOrEqualTo(3.0,
            "navy lettering on a navy page is lightened until it reads");

        using var light = Branding.ForPage(logo, Color.White, dark: false);
        light.GetPixel(15, 15).ToArgb().Should().Be(navy.ToArgb(),
            "on a light page only the paper goes");
    }

    [Fact]
    public void A_control_left_out_of_the_theme_keeps_its_colours()
    {
        using var window = new Panel();
        using var warning = new Panel { BackColor = Amber };
        using var row = new Panel { BackColor = Amber };
        window.Controls.Add(warning);
        window.Controls.Add(row);

        PhxTheme.Exclude(warning);

        var dark = Color.FromArgb(70, 60, 30);
        PhxTheme.Recolour(window, new Dictionary<Color, Color> { [Amber] = dark });

        warning.BackColor.Should().Be(Amber, "the test database's bar must stay yellow");
        row.BackColor.Should().Be(dark);
    }
}
