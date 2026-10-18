using System.Drawing.Printing;

namespace PharmaX.Adapters.Printing;

/// <summary>
/// Fixed-pitch type sized to the paper: so many characters across the
/// width the printer can actually reach, and on a label so many lines
/// down it.
/// </summary>
/// <remarks>
/// The sizes used to be fixed in points, chosen for an 80 mm roll and a
/// 70 by 50 mm label. The pharmacy's till roll is 76 mm (40 columns) and
/// its labels are 102 by 36 mm (40 columns by 8 lines) on an Epson
/// LX-350, so the right-hand figures of a receipt ran off the roll and a
/// label ran off the bottom of the stock. Measured against the page
/// instead, a line is a line whatever the paper turns out to be.
/// </remarks>
internal static class FixedPitch
{
    public const string Family = "Consolas";

    /// <summary>
    /// The page inside its margins, and no wider than the printer can
    /// reach: a driver whose printable width is narrower than the paper
    /// it reports loses whatever is drawn past it.
    /// </summary>
    public static Rectangle Area(PrintPageEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var margins = e.MarginBounds;
        var reach = (int)e.PageSettings.PrintableArea.Width - margins.Left;

        return new Rectangle(margins.Left, margins.Top,
            Math.Max(1, Math.Min(reach, margins.Width)), margins.Height);
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
