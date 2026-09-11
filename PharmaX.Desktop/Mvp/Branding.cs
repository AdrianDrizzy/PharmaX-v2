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

    public void Load(string? path)
    {
        lock (_gate)
        {
            var previous = Logo;

            Logo = Read(path);
            LogoPath = Logo is null ? null : path;

            previous?.Dispose();
        }
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

            graphics.DrawImage(Logo,
                box.X + (box.Width - width) / 2,
                box.Y + (box.Height - height) / 2,
                width, height);
        }
    }
}
