using System.Globalization;

namespace PharmaX.Desktop.Mvp;

/// <summary>
/// How large PharmaX draws its windows, on top of the display's own
/// scaling: chosen from the shell's View menu, per till.
/// </summary>
/// <remarks>
/// For the counter whose screen is small, or far away, or read by someone
/// who would rather not lean in. Windows' own scaling does the same for
/// every program on the machine, and needs a sign-out; this is PharmaX
/// alone, at once.
///
/// Applied by PhxTheme.Apply after the theme's font, so every screen
/// opens at it, and to the windows already open when it changes. A window
/// laid out at the default font (PhxTheme.Follow - the prompts) keeps its
/// size: its boxes were measured for that font.
/// </remarks>
public static class PhxZoom
{
    /// The sizes on offer, in per cent.
    public static IReadOnlyList<int> Steps { get; } =
        new[] { 75, 90, 100, 110, 125, 150, 175, 200 };

    /// The size in use, in per cent.
    public static int Percent { get; private set; } = 100;

    /// Raised once every open window has been redrawn at a new size.
    public static event EventHandler? Changed;

    // The size each window is drawn at now, so a change scales it by the
    // difference rather than from scratch.
    private static readonly System.Runtime.CompilerServices
        .ConditionalWeakTable<Form, Level> Zoomed = new();

    private sealed class Level(int percent)
    {
        public int Percent = percent;
    }

    // Shared between windows: a scaled font is the same font wherever it
    // is used, and a till opens the same screens all day.
    private static readonly Dictionary<(string, float, FontStyle, GraphicsUnit), Font>
        Fonts = new();

    /// <summary>
    /// The stored setting as a size on offer; anything else is 100.
    /// </summary>
    public static int Resolve(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                     out var percent) && Steps.Contains(percent)
            ? percent
            : 100;

    /// The next size up, or down, from the one in use.
    public static int Step(int direction) =>
        direction > 0
            ? Steps.FirstOrDefault(p => p > Percent, Steps[^1])
            : Steps.LastOrDefault(p => p < Percent, Steps[0]);

    /// <summary>
    /// Puts a size in use for the windows opened from now on, without
    /// touching the ones already open. Read at sign-in.
    /// </summary>
    public static void Use(int percent) =>
        Percent = Resolve(percent.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Puts a size in use and redraws every open window at it.
    /// </summary>
    public static void Change(int percent)
    {
        Use(percent);

        foreach (var form in System.Windows.Forms.Application.OpenForms.Cast<Form>().ToList())
            if (Zoomed.TryGetValue(form, out _)) Apply(form);

        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// Draws a window at the size in use. Called by PhxTheme.Apply once
    /// the theme's font is on it.
    /// </summary>
    internal static void Apply(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);

        var box = Zoomed.GetValue(form, _ => new Level(100));
        if (box.Percent == Percent) return;

        var by = (float)Percent / box.Percent;
        box.Percent = Percent;

        Rescale(form, by);
    }

    /// <summary>
    /// Scales a window, its controls and their text by a factor.
    /// </summary>
    /// <remarks>
    /// The window's own font is changed and Windows Forms scales the
    /// layout from it, as it does for the display's scaling - the same
    /// AutoScaleMode.Font every designer here declares. A control given a
    /// font of its own - a heading, a total - does not follow its window's,
    /// so those are found first and scaled after, from what they were.
    /// Grids keep row heights and column widths in pixels, which the
    /// layout scaling does not reach, so they are done by hand.
    /// </remarks>
    private static void Rescale(Form form, float by)
    {
        var own = new List<(Control Control, Font Font)>();
        var items = new List<(ToolStripItem Item, Font Font)>();
        Collect(form, own, items);

        var scalesItself = form.AutoScaleMode == AutoScaleMode.Font;

        form.SuspendLayout();
        try
        {
            form.Font = Scaled(form.Font, by);

            // A window that does not scale from its font is scaled by the
            // factor directly.
            if (!scalesItself) form.Scale(new SizeF(by, by));

            foreach (var (control, font) in own)
                control.Font = Scaled(font, by);

            foreach (var (item, font) in items)
                item.Font = Scaled(font, by);

            foreach (var strip in Descendants(form).OfType<ToolStrip>())
                strip.ImageScalingSize = Size(strip.ImageScalingSize, by);

            foreach (var grid in Descendants(form).OfType<DataGridView>())
                Rescale(grid, by);
        }
        finally
        {
            form.ResumeLayout(true);
        }
    }

    /// Every control on a window whose font is its own, and every strip
    /// entry whose font is not its strip's.
    private static void Collect(Control parent,
                                List<(Control, Font)> own,
                                List<(ToolStripItem, Font)> items)
    {
        foreach (Control child in parent.Controls)
        {
            if (!child.Font.Equals(parent.Font)) own.Add((child, child.Font));

            if (child is ToolStrip strip)
                CollectItems(strip.Items, strip.Font, items);

            if (child.HasChildren) Collect(child, own, items);
        }
    }

    private static void CollectItems(ToolStripItemCollection collection, Font strip,
                                     List<(ToolStripItem, Font)> items)
    {
        foreach (ToolStripItem item in collection)
        {
            if (!item.Font.Equals(strip)) items.Add((item, item.Font));

            if (item is ToolStripDropDownItem { HasDropDownItems: true } drop)
                CollectItems(drop.DropDownItems, strip, items);
        }
    }

    private static void Rescale(DataGridView grid, float by)
    {
        Restyle(grid.DefaultCellStyle, by);
        Restyle(grid.ColumnHeadersDefaultCellStyle, by);
        Restyle(grid.RowsDefaultCellStyle, by);
        Restyle(grid.AlternatingRowsDefaultCellStyle, by);

        grid.RowTemplate.Height = Pixels(grid.RowTemplate.Height, by);

        if (grid.ColumnHeadersHeightSizeMode != DataGridViewColumnHeadersHeightSizeMode.AutoSize)
            grid.ColumnHeadersHeight = Math.Max(4, Pixels(grid.ColumnHeadersHeight, by));

        foreach (DataGridViewColumn column in grid.Columns)
        {
            Restyle(column.DefaultCellStyle, by);
            column.MinimumWidth = Math.Max(2, Pixels(column.MinimumWidth, by));

            if (column.AutoSizeMode is DataGridViewAutoSizeColumnMode.None
                or DataGridViewAutoSizeColumnMode.NotSet
                && grid.AutoSizeColumnsMode == DataGridViewAutoSizeColumnsMode.None)
                column.Width = Pixels(column.Width, by);
        }

        foreach (DataGridViewRow row in grid.Rows)
        {
            Restyle(row.DefaultCellStyle, by);
            row.Height = Math.Max(row.MinimumHeight, Pixels(row.Height, by));
        }
    }

    private static void Restyle(DataGridViewCellStyle style, float by)
    {
        if (style.Font is Font font) style.Font = Scaled(font, by);
    }

    private static Font Scaled(Font font, float by)
    {
        var size = (float)Math.Round(font.Size * by, 2);
        var key = (font.FontFamily.Name, size, font.Style, font.Unit);

        lock (Fonts)
        {
            if (!Fonts.TryGetValue(key, out var scaled))
            {
                scaled = new Font(font.FontFamily, size, font.Style, font.Unit);
                Fonts[key] = scaled;
            }

            return scaled;
        }
    }

    private static int Pixels(int value, float by) =>
        (int)Math.Round(value * by);

    private static Size Size(Size size, float by) =>
        new(Pixels(size.Width, by), Pixels(size.Height, by));

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;

            foreach (var grandchild in Descendants(child))
                yield return grandchild;
        }
    }
}
