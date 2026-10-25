using System.Drawing.Printing;

namespace PharmaX.Adapters.Printing;

/// <summary>
/// Fixed-pitch type sized to the paper: so many characters across the
/// width the printer can actually reach, and on a label so many lines
/// down it.
/// </summary>
/// <remarks>
/// The sizes used to be fixed in points, chosen for an 80 mm roll and a
/// 70 by 50 mm label. Measured against the page, a line is a line
/// whatever the paper turns out to be.
/// </remarks>
internal static class FixedPitch
{
    public const string Family = "Consolas";

    /// <summary>
    /// The page inside its margins, and no wider or taller than the
    /// printer can reach: a driver whose printable area is smaller than
    /// the paper it reports loses whatever is drawn past it. On the label
    /// printer that was the last line of the address, drawn below the
    /// part of the sticker the driver prints.
    /// </summary>
    public static Rectangle Area(PrintPageEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var margins = e.MarginBounds;
        var printable = e.PageSettings.PrintableArea;
        var reach = (int)printable.Width - margins.Left;
        var depth = (int)printable.Height - margins.Top;

        return new Rectangle(margins.Left, margins.Top,
            Math.Max(1, Math.Min(reach, margins.Width)),
            Math.Max(1, Math.Min(depth, margins.Height)));
    }

    /// <summary>
    /// The size in points at which <paramref name="columns"/> characters
    /// fill <paramref name="width"/> - and, where given, at which
    /// <paramref name="lines"/> lines fill <paramref name="height"/> -
    /// never larger than <paramref name="largest"/>.
    /// </summary>
    public static float Size(Graphics g, int columns, float width,
                             float largest, int lines = 0, float height = 0f)
    {
        ArgumentNullException.ThrowIfNull(g);

        using var probe = new Font(Family, 10f);
        using var format = Format();

        var across = g.MeasureString(new string('0', columns), probe,
                                     PointF.Empty, format).Width;

        // A hair under the full width, so a line exactly as long as the
        // roll is wide is not wrapped by rounding.
        var size = 10f * width * 0.99f / across;

        if (lines > 0)
            size = Math.Min(size, 10f * height / (lines * probe.GetHeight(g)));

        return Math.Min(largest, size);
    }

    /// <summary>
    /// Lays text out exactly as measured, with none of the padding the
    /// default format adds either side, so forty characters measure and
    /// print as forty characters.
    /// </summary>
    public static StringFormat Format() =>
        (StringFormat)StringFormat.GenericTypographic.Clone();
}
