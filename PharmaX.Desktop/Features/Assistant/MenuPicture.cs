using System.ComponentModel;

namespace PharmaX.Desktop.Features.Assistant;

/// <summary>
/// A picture of a menu path being clicked - "File > Cash Up" - from the
/// main window's own menu: its bar as it is on screen, then each list
/// opened under it, the name to click outlined in red at every level.
/// </summary>
/// <remarks>
/// A closed drop-down cannot be drawn by Windows, so the lists are drawn
/// here from the items themselves: their icons, names and keys, in the
/// menu's colours. Items the operator may not see are left out, as the
/// real menu leaves them out.
/// </remarks>
internal static class MenuPicture
{
    /// Null where the menu has no such path for this operator.
    public static Bitmap? Draw(MenuStrip menu, IReadOnlyList<string> path)
    {
        if (path.Count == 0 || menu.Width <= 0 || menu.Height <= 0) return null;
        if (Named(menu.Items, path[0]) is not ToolStripMenuItem top) return null;

        var font = menu.Font;
        var row = Math.Max(font.Height + 12, 28);
        var lists = new List<(Rectangle Box, IReadOnlyList<ToolStripItem> Items, ToolStripMenuItem Chosen)>();
        var at = new Point(top.Bounds.Left, menu.Height);
        var parent = top;

        foreach (var name in path.Skip(1))
        {
            var items = parent.DropDownItems.Cast<ToolStripItem>().Where(i => i.Available).ToList();
            if (Named(items, name) is not ToolStripMenuItem chosen) return null;

            var width = items.OfType<ToolStripMenuItem>()
                .Max(i => TextRenderer.MeasureText(Plain(i.Text), font).Width + TextRenderer.MeasureText(ShortcutOf(i), font).Width)
                + row + 64;
            var height = items.Sum(i => i is ToolStripSeparator ? 9 : row) + 4;
            var box = new Rectangle(at, new Size(width, height));
            lists.Add((box, items, chosen));

            at = new Point(box.Right - 4, box.Top + 2 + items.TakeWhile(i => i != chosen)
                                                             .Sum(i => i is ToolStripSeparator ? 9 : row));
            parent = chosen;
        }

        var left = Math.Max(0, top.Bounds.Left - 160);
        var right = Math.Max(top.Bounds.Right, lists.Count == 0 ? 0 : lists.Max(l => l.Box.Right)) + 24;
        right = Math.Max(right, left + 420);
        var bottom = (lists.Count == 0 ? menu.Height : lists.Max(l => l.Box.Bottom)) + 16;

        // No alpha: Windows' text drawing smudges on a picture that has one.
        var picture = new Bitmap(right - left, bottom, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(picture);
        g.Clear(SystemColors.Window);
        g.TranslateTransform(-left, 0);

        using (var bar = new Bitmap(menu.Width, menu.Height))
        {
            menu.DrawToBitmap(bar, new Rectangle(Point.Empty, bar.Size));
            g.DrawImage(bar, 0, 0);
        }

        using var red = new Pen(Color.Red, 3f);
        var heading = top.Bounds;
        heading.Inflate(-1, -2);
        g.DrawRectangle(red, heading);

        foreach (var (box, items, chosen) in lists)
            DrawList(g, box, items, chosen, parentBack: top.DropDown.BackColor, font, row, red);

        g.ResetTransform();
        using var frame = new Pen(Color.FromArgb(0xC8, 0xCD, 0xD4));
        g.DrawRectangle(frame, 0, 0, picture.Width - 1, picture.Height - 1);

        return picture;
    }

    private static void DrawList(Graphics g, Rectangle box, IReadOnlyList<ToolStripItem> items,
                                 ToolStripMenuItem chosen, Color parentBack, Font font, int row, Pen red)
    {
        using (var shadow = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
            g.FillRectangle(shadow, box.X + 3, box.Y + 3, box.Width, box.Height);

        using (var back = new SolidBrush(parentBack))
            g.FillRectangle(back, box);
        using (var edge = new Pen(Color.FromArgb(0xA0, 0xA6, 0xAE)))
            g.DrawRectangle(edge, box);

        var y = box.Top + 2;

        foreach (var item in items)
        {
            if (item is ToolStripSeparator)
            {
                using var line = new Pen(Color.FromArgb(0xD0, 0xD4, 0xDA));
                g.DrawLine(line, box.Left + row + 4, y + 4, box.Right - 6, y + 4);
                y += 9;
                continue;
            }

            var line2 = new Rectangle(box.Left + 2, y, box.Width - 4, row);

            if (item == chosen)
            {
                using var lit = new SolidBrush(Color.FromArgb(0xDC, 0xE6, 0xF2));
                g.FillRectangle(lit, line2);
            }

            if (item.Image is { } icon)
            {
                var size = Math.Min(icon.Height, row - 8);
                g.DrawImage(icon, line2.Left + (row - size) / 2, y + (row - size) / 2, size, size);
            }

            // Drawn smooth by GDI+: GDI's own text comes out heavy on a picture.
            var text = new RectangleF(line2.Left + row + 6, y, line2.Width - row - 12, row);
            using var format = new StringFormat { LineAlignment = StringAlignment.Center, HotkeyPrefix = System.Drawing.Text.HotkeyPrefix.Hide };
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using (var ink = new SolidBrush(item.Enabled ? SystemColors.MenuText : SystemColors.GrayText))
                g.DrawString(item.Text, font, ink, text, format);
            format.Alignment = StringAlignment.Far;
            using (var grey = new SolidBrush(SystemColors.GrayText))
                g.DrawString(ShortcutOf((ToolStripMenuItem)item), font, grey, text, format);

            if (item == chosen)
            {
                var outline = line2;
                outline.Inflate(-1, -1);
                g.DrawRectangle(red, outline);
            }

            y += row;
        }
    }

    private static ToolStripMenuItem? Named(System.Collections.IEnumerable items, string name) =>
        items.OfType<ToolStripMenuItem>()
             .FirstOrDefault(i => i.Available && GuideScreens.Normalise(i.Text ?? "") == GuideScreens.Normalise(name));

    private static string Plain(string? text) => (text ?? "").Replace("&", "", StringComparison.Ordinal);

    private static string ShortcutOf(ToolStripMenuItem item) =>
        !item.ShowShortcutKeys || item.ShortcutKeys == Keys.None
            ? ""
            : item.ShortcutKeyDisplayString
              ?? TypeDescriptor.GetConverter(typeof(Keys)).ConvertToString(item.ShortcutKeys) ?? "";
}
