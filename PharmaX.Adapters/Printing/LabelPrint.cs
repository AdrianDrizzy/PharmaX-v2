using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace PharmaX.Adapters.Printing;

/// <summary>
/// Draws the rows of a label on the dot-matrix label printer.
/// </summary>
/// <remarks>
/// <para>
/// In the printer's own draft face where it has one ("Draft 15cpi" on the
/// Epson LX-350), so each line goes to the printer as text and is printed
/// in one pass of the head, as the previous system's labels were. Drawn as
/// a picture instead, a line could fall across two passes of the head
/// travelling opposite ways, and came out slanted.
/// </para>
/// <para>
/// Where the printer has no such face - another printer, or a bitmap in a
/// test - in Consolas, as tall as the row allows and squeezed across so
/// every column fits: narrow, tall letters, like the draft face.
/// </para>
/// </remarks>
internal static partial class LabelPrint
{
    /// The printer's faces, tried in order. Fifteen to the inch is what
    /// forty-two columns need on a 77 mm sticker.
    private static readonly string[] DeviceFaces =
        { "Draft 15cpi", "Draft 12cpi", "Draft 10cpi" };

    private const int CharactersPerInch = 15;

    public static void Draw(Graphics g, Rectangle bounds,
                            IReadOnlyList<LabelRow> rows, int columns, int lines)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(rows);

        var layout = Layout(rows, bounds.Height, lines);

        if (!InPrinterFace(g, bounds, rows, layout, columns))
            Condensed(g, bounds, rows, layout, columns);
    }

    /// <summary>
    /// Where each row sits: an equal share of the height the printer
    /// reaches, never less than <paramref name="lines"/> to the label. A
    /// label with that many rows or more fills it to the bottom, with no
    /// blank row and no gap under the address.
    /// </summary>
    private static List<(float Y, float Height, bool Compact)> Layout(
        IReadOnlyList<LabelRow> rows, float height, int lines)
    {
        var slot = height / Math.Max(1, Math.Max(lines, rows.Count));

        return rows.Select((row, i) => (i * slot, slot, row.Compact)).ToList();
    }

    /// <summary>
    /// The runs of text in a row and the column each starts at: words
    /// separated by one space belong together, two or more part them -
    /// the price from the date, the expiry from the batch.
    /// </summary>
    internal static IEnumerable<(int Column, string Text)> Segments(string row) =>
        Runs().Matches(row ?? "").Select(m => (m.Index, m.Value));

    [GeneratedRegex(@"\S+(?: \S+)*")]
    private static partial Regex Runs();

    private static void Condensed(Graphics g, Rectangle bounds,
                                  IReadOnlyList<LabelRow> rows,
                                  List<(float Y, float Height, bool Compact)> layout,
                                  int columns)
    {
        // One direction, so a line the head would otherwise print going
        // both ways does not come out sheared. The third and fourth lines
        // were the ones that straddled a pass.
        var hdc = g.GetHdc();

        try
        {
            OneDirection(hdc);
        }
        finally
        {
            g.ReleaseHdc(hdc);
        }

        // Every row has the same slot (Layout).
        var headHeight = layout.Count > 0 ? layout[0].Height : bounds.Height;
        var footHeight = headHeight;

        // A gap under every line. A font as tall as the row sat on the
        // next line, and that line was the one that looked broken.
        using var probe = new Font(FixedPitch.Family, 10f);
        using var exact = FixedPitch.Format();
        exact.FormatFlags |= StringFormatFlags.NoClip | StringFormatFlags.NoWrap;

        var sample = new string('0', Math.Max(1, columns));
        var across = g.MeasureString(sample, probe, PointF.Empty, exact).Width;
        var byWidth = 10f * bounds.Width * 0.98f / across;

        float SizeFor(float rowHeight)
        {
            var byHeight = 10f * rowHeight * 0.78f / probe.GetHeight(g);
            return Math.Max(6f, Math.Min(byHeight, byWidth));
        }

        var size = SizeFor(headHeight);

        // Two pixels under the head. Smaller still where the foot line
        // itself is shorter than that, so the year and the address fit.
        var twoPixels = 2f * 72f / Math.Max(1f, g.DpiY);
        var footSize = Math.Max(5f, Math.Min(size - twoPixels, SizeFor(footHeight)));

        using var plain = new Font(FixedPitch.Family, size);
        using var foot = new Font(FixedPitch.Family, footSize);
        var cell = g.MeasureString(sample, plain, PointF.Empty, exact).Width
                   / Math.Max(1, columns);
        using var pen = new Pen(Color.Black, Math.Max(1f, size / 14f));

        var state = g.Save();

        try
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
            g.TranslateTransform(bounds.Left, bounds.Top);

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var band = layout[i];
                var font = band.Compact ? foot : plain;

                foreach (var (column, text) in Segments(row.Text))
                {
                    var x = column * cell;
                    g.DrawString(text, font, Brushes.Black, x, band.Y, exact);

                    if (!row.Underlined) continue;

                    var width = g.MeasureString(text, font, PointF.Empty, exact).Width;
                    var line = band.Y + font.GetHeight(g) * 0.92f;
                    g.DrawLine(pen, x, line, x + width, line);
                }
            }
        }
        finally
        {
            g.Restore(state);
        }
    }

    /// <summary>
    /// Prints the rows in the printer's draft face, and says whether it
    /// could: false where the printer has no such face, or the rows will
    /// not fit it, and nothing has been drawn.
    /// </summary>
    private static bool InPrinterFace(Graphics g, Rectangle bounds,
                                      IReadOnlyList<LabelRow> rows,
                                      List<(float Y, float Height, bool Compact)> layout,
                                      int columns)
    {
        var hdc = g.GetHdc();

        try
        {
            var dpiX = GetDeviceCaps(hdc, LogPixelsX);
            var dpiY = GetDeviceCaps(hdc, LogPixelsY);
            if (dpiX <= 0 || dpiY <= 0) return false;

            double Across(float hundredths) => hundredths * dpiX / 100.0;
            double Down(float hundredths) => hundredths * dpiY / 100.0;

            var natural = (double)dpiX / CharactersPerInch;
            var room = Across(bounds.Width) / Math.Max(1, columns);
            var pitch = Math.Min(natural, room);
            // Every row has the same slot (Layout).
            var headBand = layout.Count > 0 ? layout[0].Height : bounds.Height;

            // Shorter than the row, so the line under the words sits in
            // the gap and never on the type of the next row.
            var faceHeight = (int)Math.Round(Down(headBand) * 0.78);

            if (pitch < natural * 0.7 || faceHeight < 8)
                return false;

            // The foot is two pixels smaller, and narrower by the same
            // amount, so the year is not cut off at the edge of the sticker.
            var footFace = Math.Max(8, faceHeight - 2);
            var footPitch = pitch * footFace / faceHeight;

            OneDirection(hdc);
            _ = SetBkMode(hdc, Transparent);
            _ = SetTextCharacterExtra(hdc, 0);

            var width = Math.Max(1, (int)Math.Round(pitch));
            var footWidth = Math.Max(1, (int)Math.Round(footPitch));

            foreach (var name in DeviceFaces)
            {
                var plain = Face(name, faceHeight, width);
                var small = Face(name, footFace, footWidth);
                var before = SelectObject(hdc, plain);

                try
                {
                    if (!IsPrintersOwn(hdc, name)) continue;

                    if (!GetTextMetricsW(hdc, out var metrics)) continue;

                    SelectObject(hdc, small);
                    if (!GetTextMetricsW(hdc, out var footMetrics))
                        footMetrics = metrics;

                    var origin = Down(bounds.Top);

                    for (var i = 0; i < rows.Count; i++)
                    {
                        var row = rows[i];
                        var band = layout[i];
                        var compact = band.Compact;
                        SelectObject(hdc, compact ? small : plain);

                        var y = (int)Math.Round(origin + Down(band.Y));
                        var used = compact ? footPitch : pitch;
                        var ascent = compact ? footMetrics.Ascent : metrics.Ascent;

                        foreach (var (column, text) in Segments(row.Text))
                        {
                            var x = (int)Math.Round(Across(bounds.Left) + column * used);
                            TextOutW(hdc, x, y, text, text.Length);

                            if (!row.Underlined) continue;

                            // A drawn line, not the font's own underline.
                            // The font underline is painted on the next
                            // pass of the head and shears that line.
                            var under = y + ascent + 1;
                            MoveToEx(hdc, x, under, IntPtr.Zero);
                            LineTo(hdc, x + (int)Math.Round(text.Length * used), under);
                        }
                    }

                    return true;
                }
                finally
                {
                    SelectObject(hdc, before);
                    DeleteObject(plain);
                    DeleteObject(small);
                }
            }

            return false;
        }
        finally
        {
            g.ReleaseHdc(hdc);
        }
    }

    /// Epson: print this page in one direction. Ignored where the driver
    /// does not take raw commands.
    private static void OneDirection(IntPtr hdc)
    {
        var bytes = new byte[] { 3, 0, 0x1B, 0x55, 1 };
        _ = ExtEscape(hdc, PassThrough, bytes.Length, bytes, 0, null);
    }

    private static IntPtr Face(string name, int height, int width) =>
        CreateFontW(-height, width, 0, 0, NormalWeight, 0, 0, 0,
                    DefaultCharset, DevicePrecision, DefaultClip, DraftQuality,
                    FixedPitchModern, name);

    /// The face selected is the printer's own draft face, not a screen
    /// font Windows put in its place.
    private static bool IsPrintersOwn(IntPtr hdc, string wanted)
    {
        if (!GetTextMetricsW(hdc, out var metrics)
            || (metrics.PitchAndFamily & DeviceFont) == 0)
            return false;

        var face = new StringBuilder(64);
        var asked = wanted.Split(' ')[0];

        return GetTextFaceW(hdc, face.Capacity, face) > 0
               && face.ToString().Contains(asked, StringComparison.OrdinalIgnoreCase);
    }

    private const int LogPixelsX = 88;
    private const int LogPixelsY = 90;
    private const int Transparent = 1;
    private const int PassThrough = 19;
    private const int NormalWeight = 400;
    private const uint DefaultCharset = 1;
    private const uint DevicePrecision = 5;
    private const uint DefaultClip = 0;
    private const uint DraftQuality = 1;
    private const uint FixedPitchModern = 1 | (3 << 4);
    private const byte DeviceFont = 0x08;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TextMetric
    {
        public int Height, Ascent, Descent, InternalLeading, ExternalLeading,
                   AveCharWidth, MaxCharWidth, Weight, Overhang,
                   DigitizedAspectX, DigitizedAspectY;
        public char FirstChar, LastChar, DefaultChar, BreakChar;
        public byte Italic, Underlined, StruckOut, PitchAndFamily, CharacterSet;
    }

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr hdc, int index);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFontW(int height, int width, int escapement,
        int orientation, int weight, uint italic, uint underline, uint strikeOut,
        uint charSet, uint outPrecision, uint clipPrecision, uint quality,
        uint pitchAndFamily, string face);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern int SetBkMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll")]
    private static extern int SetTextCharacterExtra(IntPtr hdc, int extra);

    [DllImport("gdi32.dll")]
    private static extern bool MoveToEx(IntPtr hdc, int x, int y, IntPtr point);

    [DllImport("gdi32.dll")]
    private static extern bool LineTo(IntPtr hdc, int x, int y);

    [DllImport("gdi32.dll", CharSet = CharSet.Ansi)]
    private static extern int ExtEscape(IntPtr hdc, int escape, int inputCount,
        byte[] input, int outputCount, byte[]? output);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool TextOutW(IntPtr hdc, int x, int y, string text, int length);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetTextMetricsW(IntPtr hdc, out TextMetric metrics);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetTextFaceW(IntPtr hdc, int count, StringBuilder face);
}
