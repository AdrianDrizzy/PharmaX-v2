using PharmaX.Application.Abstractions;

namespace PharmaX.Desktop.Mvp;

/// <summary>
/// The pharmacy's logo, held once for the whole application.
/// </summary>
/// <remarks>
/// A singleton with a plain <see cref="Load"/> rather than a service
/// that reads the database on demand: the shell, the sign-in window and
/// every receipt want the same image, and reopening a file from disk on
/// each paint is both slower and a way to get a locked file. Program
/// loads it once at start-up and system setup reloads it when the
/// operator changes it, so a new logo appears without a restart.
/// </remarks>
public sealed class Branding : IDisposable
{
    private readonly object _gate = new();

    /// <summary>
    /// Where PharmaX keeps its own copy of the logo. Its own copy
    /// because a path into someone's Downloads folder stops resolving
    /// the first time that folder is tidied.
    /// </summary>
    public static string Folder { get; } = Path.Combine(
        Environment.ExpandEnvironmentVariables("%PROGRAMDATA%"),
        "PharmaX", "branding");

    public string? LogoPath { get; private set; }

    /// <summary>
    /// The logo, or null. Loaded through a copy in memory so the file
    /// itself is not held open - a locked PNG cannot be replaced, and
    /// replacing it is exactly what the setup screen does next.
    /// </summary>
    public Image? Logo { get; private set; }

    public bool HasLogo => Logo is not null;

    /// <summary>
    /// The pharmacy's logo, the same for every till and every user: read
    /// from the shared settings and kept as a file on this machine. Run
    /// before every sign-in, so a logo saved at one till reaches the
    /// others at their next sign-in. Returns the pharmacy's palette.
    /// </summary>
    /// <param name="installedLogo">
    /// The logo chosen when PharmaX was set up on this computer, taken
    /// where the pharmacy has never had one.
    /// </param>
    public async Task<string?> RefreshAsync(IPharmacySettings pharmacy,
                                            IWorkstationSettings workstation,
                                            string? installedLogo = null)
    {
        ArgumentNullException.ThrowIfNull(pharmacy);
        ArgumentNullException.ThrowIfNull(workstation);

        var image = await pharmacy.GetAsync(PharmacySettingKey.LogoImage).ConfigureAwait(false);
        var palette = await pharmacy.GetAsync(PharmacySettingKey.Palette).ConfigureAwait(false);

        // A till set up when the logo and colours were its own: what it
        // had becomes the pharmacy's, and so every other till's. Failing
        // that, the one chosen at setup. Never once removed: that is blank.
        if (image is null
            && (Encode(await workstation.GetAsync(WorkstationSettingKey.LogoPath).ConfigureAwait(false))
                ?? Encode(installedLogo)) is { } promoted)
            await pharmacy.SetAsync(PharmacySettingKey.LogoImage, image = promoted).ConfigureAwait(false);

        if (palette is null
            && await workstation.GetAsync(WorkstationSettingKey.Palette).ConfigureAwait(false)
                is { } own)
            await pharmacy.SetAsync(PharmacySettingKey.Palette, palette = own).ConfigureAwait(false);

        Load(Cache(image));
        return palette;
    }

    /// <summary>
    /// Copies a chosen image into <see cref="Folder"/> in place of any logo
    /// kept there before, and returns the path to the copy.
    /// </summary>
    public static string Keep(string chosen)
    {
        Directory.CreateDirectory(Folder);

        var destination = Path.Combine(Folder,
            "logo" + Path.GetExtension(chosen).ToUpperInvariant()
                         .ToLowerInvariant());

        foreach (var stale in Directory.GetFiles(Folder, "logo.*"))
        {
            if (!string.Equals(stale, destination,
                               StringComparison.OrdinalIgnoreCase))
                TryDelete(stale);
        }

        if (!string.Equals(Path.GetFullPath(chosen), destination,
                           StringComparison.OrdinalIgnoreCase))
            File.Copy(chosen, destination, overwrite: true);

        return destination;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // An old logo still open somewhere is not worth failing a
            // save over; it is overwritten on the next change anyway.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// An image file as text the shared settings can hold; null for none.
    public static string? Encode(string? path) =>
        string.IsNullOrWhiteSpace(path) || !File.Exists(path)
            ? null
            : Convert.ToBase64String(File.ReadAllBytes(path));

    /// Where the shared logo is kept on this machine; a test points it elsewhere.
    public string CacheFolder { get; init; } = Folder;

    /// The shared logo as a file on this machine, rewritten only when it changed.
    private string? Cache(string? image)
    {
        if (string.IsNullOrEmpty(image)) return null;

        try
        {
            var bytes = Convert.FromBase64String(image);
            var path = Path.Combine(CacheFolder, "pharmacy-logo.img");

            Directory.CreateDirectory(CacheFolder);
            if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
                File.WriteAllBytes(path, bytes);

            return path;
        }
        catch (Exception ex) when (ex is FormatException or IOException
                                       or UnauthorizedAccessException)
        {
            // No logo is better than no sign-in.
            return null;
        }
    }

    public void Load(string? path)
    {
        lock (_gate)
        {
            var previous = Logo;

            Logo = Read(path);
            LogoPath = Logo is null ? null : path;

            previous?.Dispose();
            Forget();
        }
    }

    // The logo as last made for a page, and what it was made for.
    private Bitmap? _onPage;
    private (Color Page, bool Dark) _madeFor;

    private void Forget()
    {
        _onPage?.Dispose();
        _onPage = null;
    }

    private static Image? Read(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            return Image.FromStream(stream);
        }
#pragma warning disable CA1031 // Deliberately broad: see below.
        catch (Exception)
#pragma warning restore CA1031
        {
            // A corrupt or unreadable image is a cosmetic problem. The
            // till opens without a logo rather than not at all.
            return null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            Logo?.Dispose();
            Logo = null;
            Forget();
        }
    }

    /// <summary>
    /// Draws the logo into a box, keeping its proportions and never
    /// enlarging it past its own size.
    /// </summary>
    public void Draw(Graphics graphics, Rectangle box)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        lock (_gate)
        {
            if (Logo is null) return;

            var scale = Math.Min(
                Math.Min((float)box.Width / Logo.Width,
                         (float)box.Height / Logo.Height), 1f);

            var width = (int)(Logo.Width * scale);
            var height = (int)(Logo.Height * scale);

            graphics.InterpolationMode = System.Drawing.Drawing2D
                .InterpolationMode.HighQualityBicubic;

            var at = new Rectangle(box.X + (box.Width - width) / 2,
                box.Y + (box.Height - height) / 2, width, height);

            if (width <= 0 || height <= 0) return;

            // On the card it sits on - the home header, the sign-in panel -
            // without the white it came on. That white used to be kept as a
            // plate on a dark page, which read as a block pasted on.
            var made = (PhxTheme.Card, PhxTheme.Mode.IsDark);

            if (_onPage is null || _madeFor != made)
            {
                Forget();
                _onPage = ForPage(Logo, PhxTheme.Card, PhxTheme.Mode.IsDark);
                _madeFor = made;
            }

            graphics.PixelOffsetMode = System.Drawing.Drawing2D
                .PixelOffsetMode.HighQuality;
            graphics.DrawImage(_onPage, at);
        }
    }

    /// <summary>
    /// The logo made for the page it sits on: the white it was drawn on
    /// taken away, and on a dark page every colour of it too dark to see
    /// there lightened until it reads at 3:1.
    /// </summary>
    /// <remarks>
    /// The paper is the white joined to the edge of the image and the small
    /// patches of it inside letters; a larger white shape inside the logo is
    /// part of it and is kept. The soft edge between the two is un-mixed
    /// from the white, so a letter keeps its smooth outline on any page
    /// instead of a pale halo.
    /// A logo with no white round it - a full-bleed block of colour - is
    /// its own background and is left exactly as it is.
    ///
    /// Made at the logo's own size - up to 1200 pixels across - and only
    /// then shrunk to fit where it is drawn. Shrunk first, the hole in a
    /// small letter blurs to grey and is no longer recognisably paper: the
    /// tagline came out speckled with white.
    /// </remarks>
    internal static Bitmap ForPage(Image logo, Color page, bool dark)
    {
        ArgumentNullException.ThrowIfNull(logo);

        var scale = Math.Min(1.0, 1200.0 / Math.Max(logo.Width, logo.Height));
        var size = new Size(Math.Max(1, (int)(logo.Width * scale)),
                            Math.Max(1, (int)(logo.Height * scale)));

        var bitmap = new Bitmap(size.Width, size.Height,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D
                .InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D
                .PixelOffsetMode.HighQuality;
            graphics.DrawImage(logo, new Rectangle(Point.Empty, size));
        }

        var data = bitmap.LockBits(new Rectangle(Point.Empty, size),
            System.Drawing.Imaging.ImageLockMode.ReadWrite,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        try
        {
            // 32 bits a pixel, so each row is exactly its width: no padding.
            var pixels = new int[size.Width * size.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

            if (TakeAwayPaper(pixels, size.Width, size.Height) && dark)
                Lighten(pixels, page);

            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    /// <summary>
    /// Clears the near-white joined to the image's edge, and un-mixes the
    /// two pixels inside it from white. False where there was none.
    /// </summary>
    private static bool TakeAwayPaper(int[] pixels, int width, int height)
    {
        static bool Paper(int argb)
        {
            var c = Color.FromArgb(argb);
            var low = Math.Min(c.R, Math.Min(c.G, c.B));
            var high = Math.Max(c.R, Math.Max(c.G, c.B));
            return c.A < 16 || (low >= 215 && high - low <= 25);
        }

        // How far each pixel is from the paper: 0 on it, then 1 and 2 for
        // the soft edge next to it; -1 for anything further in.
        var reach = new int[pixels.Length];
        Array.Fill(reach, -1);

        IEnumerable<int> Beside(int i)
        {
            var (x, y) = (i % width, i / width);
            if (x > 0) yield return i - 1;
            if (x < width - 1) yield return i + 1;
            if (y > 0) yield return i - width;
            if (y < height - 1) yield return i + width;
        }

        // The paper first, all of it, from the edge inwards...
        var paper = new List<int>();

        void Seed(int i)
        {
            if (reach[i] != -1 || !Paper(pixels[i])) return;
            reach[i] = 0;
            paper.Add(i);
        }

        for (var x = 0; x < width; x++) { Seed(x); Seed((height - 1) * width + x); }
        for (var y = 0; y < height; y++) { Seed(y * width); Seed(y * width + width - 1); }

        if (paper.Count == 0) return false;

        for (var next = 0; next < paper.Count; next++)
            foreach (var n in Beside(paper[next])) Seed(n);

        // ...and the white inside letters - the hole in an e, an a, a P -
        // which is the same paper seen through the letter. A larger white
        // shape inside the logo, a cross on a badge, is part of the design
        // and stays.
        var hole = Math.Max(16, pixels.Length / 100);

        for (var start = 0; start < pixels.Length; start++)
        {
            if (reach[start] != -1 || !Paper(pixels[start])) continue;

            var region = new List<int> { start };
            reach[start] = -2;

            for (var next = 0; next < region.Count; next++)
                foreach (var n in Beside(region[next]))
                    if (reach[n] == -1 && Paper(pixels[n]))
                    {
                        reach[n] = -2;
                        region.Add(n);
                    }

            var seenThrough = region.Count <= hole;
            foreach (var i in region) reach[i] = seenThrough ? 0 : -3;
            if (seenThrough) paper.AddRange(region);
        }

        // ...then the soft edge, two pixels deep.
        var ring = paper;

        for (var depth = 1; depth <= 2; depth++)
        {
            var outer = new List<int>();

            foreach (var i in ring)
                foreach (var n in Beside(i))
                    if (reach[n] == -1)
                    {
                        reach[n] = depth;
                        outer.Add(n);
                    }

            ring = outer;
        }

        for (var i = 0; i < pixels.Length; i++)
        {
            if (reach[i] == 0) pixels[i] = 0;
            else if (reach[i] > 0) pixels[i] = Unwhite(pixels[i]);
        }

        return true;
    }

    /// <summary>
    /// A pixel drawn over white, as the colour and opacity that would have
    /// made it: what is left of a letter's edge once the paper is gone.
    /// </summary>
    private static int Unwhite(int argb)
    {
        var c = Color.FromArgb(argb);
        var opacity = (255 - Math.Min(c.R, Math.Min(c.G, c.B))) / 255.0;

        if (opacity < 0.02) return 0;

        int Channel(byte value) => Math.Clamp(
            (int)Math.Round((value - 255 * (1 - opacity)) / opacity), 0, 255);

        return Color.FromArgb((int)Math.Round(c.A * opacity),
            Channel(c.R), Channel(c.G), Channel(c.B)).ToArgb();
    }

    /// Every colour that does not read on the page at 3:1 - navy lettering
    /// on a navy page - lightened until it does; its opacity kept.
    private static void Lighten(int[] pixels, Color page)
    {
        var done = new Dictionary<int, int>();

        for (var i = 0; i < pixels.Length; i++)
        {
            var alpha = pixels[i] & unchecked((int)0xFF000000);
            if (alpha == 0) continue;

            var solid = pixels[i] | unchecked((int)0xFF000000);

            if (!done.TryGetValue(solid, out var lifted))
            {
                var colour = Color.FromArgb(solid);
                lifted = Palette.Contrast(colour, page) >= 3.0
                    ? solid
                    : Palette.Readable(colour, page, 3.0).ToArgb();
                done[solid] = lifted;
            }

            pixels[i] = (lifted & 0x00FFFFFF) | alpha;
        }
    }

    /// <summary>
    /// The logo as a watermark: its white ground left out, as large as fits
    /// in the box and centred in it. Watermark fades it.
    /// </summary>
    /// <param name="tint">
    /// Draws it as a glow of this colour - the dark parts lit, the light
    /// parts gone - rather than in its own colours, for a dark page.
    /// </param>
    public void DrawWatermark(Graphics graphics, Rectangle box, Color? tint)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        lock (_gate)
        {
            if (Logo is null || box.Width <= 0 || box.Height <= 0) return;

            var scale = Math.Min((float)box.Width / Logo.Width,
                                 (float)box.Height / Logo.Height);
            var width = (int)(Logo.Width * scale);
            var height = (int)(Logo.Height * scale);

            var at = new Rectangle(box.X + (box.Width - width) / 2,
                box.Y + (box.Height - height) / 2, width, height);

            using var attributes = new System.Drawing.Imaging.ImageAttributes();
            attributes.SetColorKey(Color.FromArgb(230, 230, 230), Color.White);
            if (tint is Color glow) attributes.SetColorMatrix(Glow(glow));

            graphics.InterpolationMode = System.Drawing.Drawing2D
                .InterpolationMode.HighQualityBicubic;

            graphics.DrawImage(Logo, at, 0, 0, Logo.Width, Logo.Height,
                GraphicsUnit.Pixel, attributes);
        }
    }

    /// Each pixel the tint in proportion to how dark it was.
    private static System.Drawing.Imaging.ColorMatrix Glow(Color t)
    {
        float[] weight = { 0.3f, 0.59f, 0.11f };
        float[] glow = { t.R / 255f, t.G / 255f, t.B / 255f };
        var rows = new float[5][];

        for (var i = 0; i < 5; i++) rows[i] = new float[5];

        for (var channel = 0; channel < 3; channel++)
        {
            for (var source = 0; source < 3; source++)
                rows[source][channel] = -weight[source] * glow[channel];

            rows[4][channel] = glow[channel];
        }

        rows[3][3] = 1f;
        return new System.Drawing.Imaging.ColorMatrix(rows);
    }
}
