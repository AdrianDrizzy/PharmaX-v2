using FluentAssertions;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Tests.Unit.Adapters;

/// <summary>
/// The windows take their colours from the logo, and whatever the logo,
/// the text on them stays readable.
/// </summary>
[Collection("PhxTheme")]
public class PaletteTests
{
    private static readonly Color Navy = Color.FromArgb(33, 56, 134);
    private static readonly Color Orange = Color.FromArgb(243, 106, 34);

    /// A white logo with two blocks of colour, two parts navy to one orange.
    private static Bitmap Logo(Color main, Color? second)
    {
        var bitmap = new Bitmap(300, 120);
        using var graphics = Graphics.FromImage(bitmap);

        graphics.Clear(Color.White);
        using (var brush = new SolidBrush(main)) graphics.FillRectangle(brush, 20, 20, 160, 80);

        if (second is Color other)
        {
            using var brush = new SolidBrush(other);
            graphics.FillRectangle(brush, 200, 20, 80, 80);
        }

        return bitmap;
    }

    [Fact]
    public void The_logo_gives_its_darker_colour_to_the_buttons_and_the_other_to_the_rules()
    {
        using var logo = Logo(Orange, Navy);

        var palette = Palette.FromLogo(logo);

        palette.Should().NotBeNull();
        palette!.Primary.Should().Be(Navy, "white text sits on the primary, and navy is the darker");
        palette.Highlight.Should().Be(Orange);
        palette.Id.Should().Be(Palette.FromLogoId);
    }

    [Fact]
    public void A_logo_with_no_colour_falls_back_to_navy_and_orange()
    {
        using var logo = Logo(Color.Black, Color.Gray);

        Palette.FromLogo(logo).Should().BeNull();
        Palette.Resolve(null, logo).Should().Be(Palette.NavyAndOrange);
    }

    [Fact]
    public void A_pale_logo_still_gives_buttons_white_text_can_be_read_on()
    {
        using var logo = Logo(Color.FromArgb(255, 214, 10), second: null);

        PhxTheme.Use(Palette.Resolve(Palette.FromLogoId, logo));

        try
        {
            Palette.Contrast(Color.White, PhxTheme.Brand).Should().BeGreaterOrEqualTo(4.5);
            Palette.Contrast(PhxTheme.HighlightText, Color.White).Should().BeGreaterOrEqualTo(4.5);
            Palette.Contrast(PhxTheme.Muted, PhxTheme.Band).Should().BeGreaterOrEqualTo(4.5);
        }
        finally
        {
            PhxTheme.Use(Palette.NavyAndOrange);
        }
    }

    [Fact]
    public void A_named_palette_wins_over_the_logo()
    {
        using var logo = Logo(Navy, Orange);

        Palette.Resolve("teal-amber", logo).Name.Should().Be("Teal and amber");
        Palette.Resolve("no-such-palette", logo).Should().Be(Palette.NavyAndOrange);
    }

    [Fact]
    public void Every_named_palette_reads_white_text_on_its_buttons()
    {
        foreach (var palette in Palette.Presets)
            Palette.Contrast(Color.White, palette.Primary).Should()
                .BeGreaterOrEqualTo(4.5, palette.Name);
    }

    [Fact]
    public void Icons_are_drawn_from_the_font_compiled_in()
    {
        var icon = PhxIcons.Image(PhxIcon.Pill, 24, Navy);

        var inked = 0;
        for (var y = 0; y < icon.Height; y++)
            for (var x = 0; x < icon.Width; x++)
                if (icon.GetPixel(x, y).A > 0) inked++;

        inked.Should().BeGreaterThan(40, "a blank image means the font or its code is wrong");
        PhxIcons.AppIcon.Should().NotBeNull();
    }

    [Theory]
    [InlineData("F6 Save", PhxIcon.DeviceFloppy)]
    [InlineData("F12 Reprint labels", PhxIcon.Printer)]
    [InlineData("F7 Remove item", PhxIcon.Trash)]
    [InlineData("Esc Close", PhxIcon.X)]
    [InlineData("&Choose image...", PhxIcon.Photo)]
    public void A_caption_says_which_icon_it_carries(string caption, PhxIcon expected) =>
        PhxIcons.ForCaption(caption).Should().Be(expected);
}
