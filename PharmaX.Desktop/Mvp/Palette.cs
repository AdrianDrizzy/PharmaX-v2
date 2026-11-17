namespace PharmaX.Desktop.Mvp;

/// <summary>
/// The two colours a set of windows is built from: the primary, which
/// carries the buttons, titles and selections, and the highlight, which
/// draws the rules and outlines. PhxTheme works out every other shade.
/// </summary>
public sealed record Palette(string Id, string Name, Color Primary, Color Highlight)
{
    /// The setting's value for "work it out from the logo", the default.
    public const string FromLogoId = "logo";

    /// The BethRapha logo's own navy and orange.
    public static Palette NavyAndOrange { get; } = new("navy-orange",
        "Navy and orange", Color.FromArgb(33, 56, 134), Color.FromArgb(243, 106, 34));

    /// <summary>The palettes on offer besides the logo's own.</summary>
    /// <remarks>
    /// Each primary reads white text at 4.5:1 or better, so a palette can
    /// never make a button's caption hard to read.
    /// </remarks>
    public static IReadOnlyList<Palette> Presets { get; } = new[]
    {
        NavyAndOrange,
        new Palette("teal-amber", "Teal and amber",
            Color.FromArgb(15, 118, 110), Color.FromArgb(245, 158, 11)),
        new Palette("royal-gold", "Royal blue and gold",
            Color.FromArgb(29, 78, 216), Color.FromArgb(234, 179, 8)),
        new Palette("forest-lime", "Forest and lime",
            Color.FromArgb(22, 101, 52), Color.FromArgb(132, 204, 22)),
        new Palette("plum-pink", "Plum and pink",
            Color.FromArgb(107, 33, 168), Color.FromArgb(236, 72, 153)),
        new Palette("slate-sky", "Slate and sky",
            Color.FromArgb(51, 65, 85), Color.FromArgb(14, 165, 233)),

        // From the Windows 11 / WinUI (Fluent) accent colours.
        new Palette("fluent-blue", "Fluent blue and sky",
            Color.FromArgb(0, 95, 184), Color.FromArgb(96, 205, 255)),
        new Palette("crimson-tangerine", "Crimson and tangerine",
            Color.FromArgb(197, 15, 31), Color.FromArgb(247, 99, 12)),
        new Palette("orchid-magenta", "Orchid and magenta",
            Color.FromArgb(136, 23, 152), Color.FromArgb(227, 0, 140)),
        new Palette("indigo-lavender", "Indigo and lavender",
            Color.FromArgb(79, 70, 229), Color.FromArgb(167, 139, 250)),
        new Palette("graphite-mint", "Graphite and mint",
            Color.FromArgb(76, 74, 72), Color.FromArgb(0, 204, 106)),
        new Palette("mahogany-sand", "Mahogany and sand",
            Color.FromArgb(142, 86, 46), Color.FromArgb(242, 198, 97))
    };

    /// <summary>
    /// The palette a setting names: the logo's when it says so or says
    /// nothing, a preset by its id, and navy and orange when the logo has
    /// no colour to give or the id is unknown.
    /// </summary>
    public static Palette Resolve(string? id, Image? logo) =>
        string.IsNullOrWhiteSpace(id) || id == FromLogoId
            ? FromLogo(logo) ?? NavyAndOrange
            : Presets.FirstOrDefault(p => p.Id == id) ?? NavyAndOrange;

    /// <summary>
    /// The logo's two main colours, or null where it has none - a black
    /// and white logo, or no logo at all.
    /// </summary>
    /// <remarks>
    /// Counted over a small copy of the image in 15-bit colour, ignoring
    /// white, near-black and greys, so the text and the background do not
    /// outvote the brand. The most common colour is one; the next one
    /// whose hue is at least 30 degrees away is the other. The darker of
    /// the two becomes the primary, because it is the one white text can
    /// sit on; a logo of one colour gets a lighter shade of it as its
    /// highlight.
    /// </remarks>
    public static Palette? FromLogo(Image? logo)
    {
        if (logo is null || logo.Width == 0 || logo.Height == 0) return null;

        var scale = Math.Min(1f, 160f / Math.Max(logo.Width, logo.Height));
        using var small = new Bitmap(logo,
            Math.Max(1, (int)(logo.Width * scale)),
            Math.Max(1, (int)(logo.Height * scale)));

        var cells = new Dictionary<int, (int Count, long R, long G, long B)>();

        for (var y = 0; y < small.Height; y++)
        {
            for (var x = 0; x < small.Width; x++)
            {
                var pixel = small.GetPixel(x, y);

                if (pixel.A < 128 || pixel.GetSaturation() < 0.25f
                    || pixel.GetBrightness() is > 0.92f or < 0.08f)
                    continue;

                var cell = (pixel.R >> 3) << 10 | (pixel.G >> 3) << 5 | pixel.B >> 3;
                cells.TryGetValue(cell, out var sum);
                cells[cell] = (sum.Count + 1, sum.R + pixel.R, sum.G + pixel.G,
                               sum.B + pixel.B);
            }
        }

        if (cells.Count == 0) return null;

        static Color Mean((int Count, long R, long G, long B) c) => Color.FromArgb(
            (int)(c.R / c.Count), (int)(c.G / c.Count), (int)(c.B / c.Count));

        var ranked = cells.Values.OrderByDescending(c => c.Count).ToList();
        var total = ranked.Sum(c => c.Count);
        var first = Mean(ranked[0]);

        Color? second = null;

        foreach (var cell in ranked.Skip(1))
        {
            if (cell.Count < total * 0.03) break;

            var colour = Mean(cell);

            if (HueDistance(first, colour) >= 30f)
            {
                second = colour;
                break;
            }
        }

        var (primary, highlight) = second is Color other
            ? Luminance(first) <= Luminance(other) ? (first, other) : (other, first)
            : (first, Lighter(first));

        return new Palette(FromLogoId, "From the logo", primary, highlight);
    }

    private static float HueDistance(Color a, Color b)
    {
        var d = Math.Abs(a.GetHue() - b.GetHue());
        return Math.Min(d, 360f - d);
    }

    private static Color Lighter(Color colour) =>
        Mix(colour, Color.White, 0.45);

    // ---- Colour arithmetic -----------------------------------------------

    /// Part way from one colour to another: 0 is the first, 1 the second.
    public static Color Mix(Color from, Color to, double amount) => Color.FromArgb(
        (int)Math.Round(from.R + (to.R - from.R) * amount),
        (int)Math.Round(from.G + (to.G - from.G) * amount),
        (int)Math.Round(from.B + (to.B - from.B) * amount));

    /// The WCAG relative luminance, 0 for black to 1 for white.
    public static double Luminance(Color colour)
    {
        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(colour.R) + 0.7152 * Channel(colour.G)
               + 0.0722 * Channel(colour.B);
    }

    /// The WCAG contrast ratio, 1 to 21.
    public static double Contrast(Color a, Color b)
    {
        var (light, dark) = (Luminance(a), Luminance(b));
        if (light < dark) (light, dark) = (dark, light);
        return (light + 0.05) / (dark + 0.05);
    }

    /// <summary>
    /// The colour, darkened a step at a time - lightened, on a dark
    /// background - until it reads against <paramref name="background"/>
    /// at <paramref name="ratio"/> or better.
    /// </summary>
    public static Color Readable(Color colour, Color background, double ratio = 4.5)
    {
        var toward = Luminance(background) < 0.2 ? Color.White : Color.Black;

        for (var step = 0; step < 40 && Contrast(colour, background) < ratio; step++)
            colour = Mix(colour, toward, 0.06);

        return colour;
    }
}

/// <summary>
/// How light or dark the windows are, whichever palette is in use: the
/// page the shades are mixed from, the ground of boxes and lists, and the
/// text. Chosen per till from the shell's Theme menu.
/// </summary>
/// <param name="Glow">
/// A colour that takes the palette highlight's place - the rules, outlines
/// and the home screen's lines - for a mode with a look of its own.
/// </param>
/// <param name="Warning">
/// What an attention row or banner is tinted with on a dark page, where
/// the light mode's pale amber would glare. Unused on a light page.
/// </param>
/// <param name="Error">The same for a problem, and its text.</param>
public sealed record ThemeMode(string Id, string Name, Color Paper, Color Field,
    Color Ink, bool IsDark, Color? Glow = null, Color? Warning = null,
    Color? Error = null)
{
    /// The look PharmaX has always had. Field and Ink are the system
    /// colours themselves, so a window in this mode is exactly as before.
    public static ThemeMode Light { get; } = new("light", "Light",
        Color.White, SystemColors.Window, SystemColors.ControlText, false);

    public static ThemeMode Dark { get; } = new("dark", "Dark",
        Color.FromArgb(18, 20, 24), Color.FromArgb(32, 36, 44),
        Color.FromArgb(241, 243, 245), true,
        Warning: Color.FromArgb(244, 187, 79), Error: Color.FromArgb(255, 98, 110));

    /// Deep navy with a cyan glow, and a lit grid behind the home screen.
    public static ThemeMode Midnight { get; } = new("midnight", "Midnight neon",
        Color.FromArgb(8, 11, 20), Color.FromArgb(17, 26, 43),
        Color.FromArgb(232, 244, 255), true, Color.FromArgb(0, 229, 255),
        Color.FromArgb(255, 200, 87), Color.FromArgb(255, 71, 126));

    /// Cream rather than white: easier on the eyes over a long shift.
    public static ThemeMode Warm { get; } = new("warm", "Warm paper",
        Color.FromArgb(251, 245, 232), Color.FromArgb(255, 251, 242),
        Color.FromArgb(58, 46, 32), false);

    /// Windows 11's own light look (WinUI's Mica grey page, white boxes).
    public static ThemeMode FluentLight { get; } = new("fluent-light", "Fluent light",
        Color.FromArgb(243, 243, 243), Color.White,
        Color.FromArgb(27, 27, 27), false);

    /// Windows 11's own dark look, with WinUI's caution and critical colours.
    public static ThemeMode FluentDark { get; } = new("fluent-dark", "Fluent dark",
        Color.FromArgb(32, 32, 32), Color.FromArgb(45, 45, 45),
        Color.White, true,
        Warning: Color.FromArgb(252, 225, 0), Error: Color.FromArgb(255, 153, 164));

    public static IReadOnlyList<ThemeMode> All { get; } =
        new[] { Light, Dark, Midnight, Warm, FluentLight, FluentDark };

    /// The mode a setting names, or Light.
    public static ThemeMode Resolve(string? id) =>
        All.FirstOrDefault(m => m.Id == id) ?? Light;
}
