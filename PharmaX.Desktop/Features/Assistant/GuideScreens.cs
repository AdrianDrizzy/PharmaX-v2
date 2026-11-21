using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Assistant;

/// <summary>
/// Pictures for a how-to answer: the screen the section is about, drawn
/// as it opens, with what a line names - a button, a box, a heading -
/// outlined on it.
/// </summary>
/// <remarks>
/// Drawn from the screens themselves when asked, not kept as screenshots,
/// so a picture never shows a button that has since moved or been
/// renamed. The screen is built empty and never shown: nothing is loaded
/// and nothing opens on the operator's desktop.
/// </remarks>
internal static partial class GuideScreens
{
    /// <summary>
    /// The screens a section can be about, by menu key: the screen first,
    /// then the windows it opens, where a step's button may be instead -
    /// Pay on Sell is on the payment window.
    /// </summary>
    /// <remarks>
    /// Built as the designer would, or with nothing for the services a
    /// screen only keeps for later: it is drawn, never shown, so OnLoad -
    /// the one place they are used - never runs.
    /// </remarks>
    private static readonly Dictionary<string, Func<Form>[]> Forms = new(StringComparer.Ordinal)
    {
        ["pos"] = Of(() => new Sales.frmPOS(), () => new Sales.frmPayment(0m),
                     () => new Sales.frmHeldSales(Array.Empty<HeldSaleDto>())),
        ["collect"] = Of(() => new Sales.frmCollect(), () => new Sales.frmPayment(0m)),
        ["returns"] = Of(() => new Sales.frmReturn()),
        ["drug-returns"] = Of(() => new Sales.frmReturn()),
        ["quotation"] = Of(() => new Sales.frmQuotation()),
        ["receipts"] = Of(() => new Sales.frmReceipts()),
        ["dispensing"] = Of(() => new Dispensing.frmDispense()),
        ["labels"] = Of(() => new Dispensing.frmLabels()),
        ["reprint-script"] = Of(() => new Dispensing.frmLabels()),
        ["script-maintenance"] = Of(() => new Dispensing.frmScriptList(), () => new Dispensing.frmScriptEdit()),
        ["dispensary-quote"] = Of(() => new Dispensing.frmDispensaryQuote()),
        ["to-follow"] = Of(() => new Dispensing.frmToFollow(null!, null!)),
        ["patients"] = Of(() => new Patients.frmPatientList(), () => new Patients.frmPatientEdit()),
        ["merge-patients"] = Of(() => new Patients.frmPatientMerge()),
        ["accounts-receivable"] = Of(() => new Patients.frmAccounts()),
        ["products"] = Of(() => new Inventory.frmProductList(), () => new Inventory.frmProductEdit()),
        ["alerts"] = Of(() => new Inventory.frmAlerts(), () => new Inventory.frmStockAssistant(),
                        () => new Suppliers.frmDraftOrders()),
        ["stock-adjust"] = Of(() => new Inventory.frmStockAdjustment()),
        ["supplier-return"] = Of(() => new Inventory.frmStockAdjustment()),
        ["move-stock"] = Of(() => new Inventory.frmStockAdjustment()),
        ["prepack"] = Of(() => new Inventory.frmPrepack()),
        ["stocktake"] = Of(() => new Inventory.frmStocktakeList(), () => new Inventory.frmStocktakeScope(),
                           () => new Inventory.frmStocktakeCapture()),
        ["suppliers"] = Of(() => new Suppliers.frmSupplierList(), () => new Suppliers.frmSupplierEdit(),
                           () => new Reference.frmReferenceEdit()),
        ["goods-receipt"] = Of(() => new Suppliers.frmGoodsReceipt()),
        ["purchase-orders"] = Of(() => new Suppliers.frmPurchaseOrderList(),
                                 () => new Suppliers.frmPurchaseOrderEdit(), () => new Suppliers.frmDraftOrders()),
        ["trial-balance"] = Of(() => new Finance.frmTrialBalance()),
        ["journal"] = Of(() => new Finance.frmJournal()),
        ["claims"] = Of(() => new Finance.frmClaims()),
        ["chart-of-accounts"] = Of(() => new Finance.frmChartOfAccounts()),
        ["day-end"] = Of(() => new Finance.frmDayEnd()),
        ["till"] = Of(() => new Tills.frmTillSession(), () => new Tills.frmPayouts()),
        ["money-out"] = Of(() => new Tills.frmPayouts()),
        ["cash-up"] = Of(() => new Tills.frmCashUp(), () => new Tills.frmTillSession(), () => new Tills.frmPayouts()),
        ["end-of-day"] = Of(() => new Tills.frmEndOfDay()),
        ["users"] = Of(() => new Admin.frmUserList(), () => new Admin.frmUserEdit()),
        ["role-permissions"] = Of(() => new Admin.frmRolePermissions(new Admin.RolePermissionPresenter(null!))),
        ["system-setup"] = Of(() => new Admin.frmSystemSetup(null!, null!, null!, null!)),
        ["membership"] = Of(() => new Admin.frmMembership(null!)),
        ["printer-setup"] = Of(() => new Admin.frmPrinterSetup(null!, null!, null!, null!)),
        ["receipt-template"] = Of(() => new Admin.frmReceiptTemplate()),
        ["data-import"] = Of(() => new Admin.frmDataImport(null!)),
        ["system-console"] = Of(() => new Admin.frmSystemInfo()),
        ["reports"] = Of(() => new Reports.frmReports()),
        ["stock"] = Of(() => new Reports.frmReports()),
        ["schemes"] = Of(() => new Reference.frmReferenceList(), () => new Reference.frmReferenceEdit()),
        ["dosage-codes"] = Of(() => new Reference.frmReferenceList(), () => new Reference.frmReferenceEdit()),
        ["warning-codes"] = Of(() => new Reference.frmReferenceList(), () => new Reference.frmReferenceEdit()),
        ["branches"] = Of(() => new Reference.frmReferenceList(), () => new Reference.frmReferenceEdit()),
        ["tariffs"] = Of(() => new Reference.frmTariffList(), () => new Reference.frmTariffEdit(),
                         () => new Reference.frmReferenceEdit()),
        ["sign-in"] = Of(() => new Login.frmLogin(), () => new Login.frmChangePassword(null!, "", "")),
        ["assistant"] = Of(() => new frmAssistant())
    };

    /// <summary>
    /// Sections whose screen is not the first menu path in them: one with
    /// none ("With lines on the sale, F8 Hold"), or whose first path is
    /// not the screen ("File > Log Off" goes back to sign-in).
    /// </summary>
    private static readonly Dictionary<string, string> Titled = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Hold and recall"] = "pos",
        ["Money out of the drawer"] = "money-out",
        ["Stocktake"] = "stocktake",
        ["Reference lists"] = "tariffs",
        ["Role permissions"] = "role-permissions",
        ["Branches"] = "branches",
        ["VAT summary and stock transactions"] = "reports",
        ["System console and control variables"] = "system-console",
        ["Sign in, log off, and the password"] = "sign-in",
        ["Ask PharmaX"] = "assistant"
    };

    private static Func<Form>[] Of(params Func<Form>[] screens) => screens;

    internal static IEnumerable<string> Keys => Forms.Keys;

    /// How many windows <paramref name="key"/> has to draw.
    internal static int WindowsOf(string key) => Forms[key].Length;

    /// <summary>
    /// The menu key of the screen a section is about: named for its title
    /// above, or the menu path it starts from, "**Dispensary > Dispensing**
    /// (F2)". Null where it names none, or one that cannot be drawn.
    /// </summary>
    public static string? ScreenOf(string section)
    {
        var title = section.Split('\n', 2)[0].Trim();
        if (Titled.TryGetValue(title, out var named)) return named;

        var path = MenuPath().Match(section);
        if (!path.Success) return null;

        var key = AssistantOpens.In(path.Groups[1].Value).Select(o => o.Key).FirstOrDefault();
        return key is not null && Forms.ContainsKey(key) ? key : null;
    }

    /// <summary>
    /// The section cut after every line that names something on the
    /// screen, each piece with the captions it names there; the last piece
    /// has none (null). Where no line does, cut once after the menu path,
    /// with no captions (empty): the plain screen.
    /// </summary>
    public static IReadOnlyList<(string Text, IReadOnlyList<string>? Captions)> Split(
        string section, Func<string, bool> onScreen)
    {
        var pieces = new List<(string Text, IReadOnlyList<string>? Captions)>();
        var text = new StringBuilder();

        foreach (var line in section.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            text.Append(line).Append('\n');

            var captions = Bold().Matches(line).Select(m => m.Groups[1].Value)
                .Where(onScreen).Distinct(StringComparer.Ordinal).ToList();
            if (captions.Count == 0) continue;

            pieces.Add((text.ToString(), captions));
            text.Clear();
        }

        var rest = text.ToString().TrimEnd('\n');
        if (rest.Length > 0) pieces.Add((rest, null));
        if (pieces.Any(p => p.Captions is not null)) return pieces;

        // Nothing named on it: the screen once, plain, under the menu path
        // that opens it, or under the title where there is none.
        var path = MenuPath().Match(rest);
        var end = rest.IndexOf('\n', path.Success ? path.Index : 0);
        return end < 0
            ? new[] { (rest, (IReadOnlyList<string>?)Array.Empty<string>()) }
            : new[] { (rest[..(end + 1)], (IReadOnlyList<string>?)Array.Empty<string>()), (rest[(end + 1)..], null) };
    }

    /// <summary>
    /// The first menu path in a section, "**File > Cash Up**", split into
    /// its names, and where the line it is on ends; null where none.
    /// </summary>
    public static (int LineEnd, IReadOnlyList<string> Names)? MenuPathIn(string section)
    {
        var path = MenuPath().Match(section);
        if (!path.Success) return null;

        var end = section.IndexOf('\n', path.Index);
        return (end < 0 ? section.Length : end + 1,
                path.Groups[1].Value.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// A caption as the guide writes it: no access-key ampersand, no
    /// function key before it ("F2 Find script"), no shortcut after it
    /// ("X-read Ctrl+X"), no dots or colon at the end.
    /// </summary>
    internal static string Normalise(string caption)
    {
        var text = caption.Replace("&&", "\u0001", StringComparison.Ordinal)
                          .Replace("&", "", StringComparison.Ordinal)
                          .Replace('\u0001', '&');
        text = KeyBefore().Replace(text.Trim(), "");
        text = KeyAfter().Replace(text, "");
        text = text.TrimEnd('.', '\u2026', ':', ' ');
        return Spaces().Replace(text, " ").ToLowerInvariant();
    }

    /// <summary>
    /// The screens for <paramref name="key"/> drawn as they open, with
    /// where each caption on them is. Null where none can be drawn: a
    /// picture is a help, never a reason for an answer to fail.
    /// </summary>
    public static ScreenDrawing? Draw(string key)
    {
        if (!Forms.TryGetValue(key, out var screens)) return null;

        var drawn = screens.Select(DrawOne).OfType<DrawnWindow>().ToList();
        return drawn.Count == 0 ? null : new ScreenDrawing(drawn);
    }

    private static DrawnWindow? DrawOne(Func<Form> build)
    {
        try
        {
            using var form = build();
            form.StartPosition = FormStartPosition.Manual;
            form.Location = Point.Empty;
            form.ShowInTaskbar = false;

            if (form is PhxFormBase screen) screen.DressWithoutShowing();

            // Every window made, the form's own hidden, so the children are
            // drawn and can be measured. The public CreateControl does
            // nothing for a window that is not shown, and Show would load
            // the screen.
            typeof(Control).GetMethod("CreateControl", BindingFlags.Instance | BindingFlags.NonPublic,
                    new[] { typeof(bool) })!
                .Invoke(form, new object[] { true });
            form.PerformLayout();

            var picture = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(picture, new Rectangle(Point.Empty, form.Size));

            var places = new Dictionary<string, Rectangle>(StringComparer.Ordinal);

            // Buttons and ticks before labels: where a label and a button
            // say the same, the step means the button.
            foreach (var control in All(form).OrderBy(c => c is ButtonBase ? 0 : 1))
            {
                if (control is not (ButtonBase or Label or GroupBox)) continue;
                if (!Shown(control, form)) continue;

                var caption = Normalise(control.Text);
                if (caption.Length == 0 || places.ContainsKey(caption)) continue;

                var bounds = control.Parent!.RectangleToScreen(control.Bounds);
                bounds.Offset(-form.Left, -form.Top);
                if (bounds.Width > 0 && bounds.Height > 0) places[caption] = bounds;
            }

            // A tab's header shows even when its page is not in front.
            foreach (var tabs in All(form).OfType<TabControl>().Where(t => Shown(t, form)))
                for (var i = 0; i < tabs.TabCount; i++)
                {
                    var bounds = tabs.RectangleToScreen(tabs.GetTabRect(i));
                    bounds.Offset(-form.Left, -form.Top);
                    places.TryAdd(Normalise(tabs.TabPages[i].Text), bounds);
                }

            return new DrawnWindow(picture, places, form.PointToScreen(Point.Empty).Y - form.Top);
        }
#pragma warning disable CA1031 // No picture rather than no answer.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ErrorHandler.Log(ex);
            return null;
        }
    }

    private static IEnumerable<Control> All(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var inner in All(child)) yield return inner;
        }
    }

    /// Visible on the screen as it opens. Control.Visible says no for
    /// everything on a window not shown, so each window's own setting is
    /// read instead - a hidden panel, a tab not in front.
    private static bool Shown(Control control, Form form)
    {
        for (var at = control; at is not null && at != form; at = at.Parent)
            if (!at.IsHandleCreated || (GetWindowLong(at.Handle, StyleIndex) & VisibleStyle) == 0)
                return false;

        return true;
    }

    private const int StyleIndex = -16;
    private const int VisibleStyle = 0x10000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [GeneratedRegex(@"\*\*([^*\r\n]*>[^*\r\n]*)\*\*", RegexOptions.CultureInvariant)]
    private static partial Regex MenuPath();

    [GeneratedRegex(@"\*\*(.+?)\*\*", RegexOptions.CultureInvariant)]
    private static partial Regex Bold();

    [GeneratedRegex(@"^(F\d{1,2}|Esc|Del|Enter)\s+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex KeyBefore();

    [GeneratedRegex(@"\s*\(?(Ctrl|Alt|Shift)\+[^\s)]+\)?$|\s*\(F\d{1,2}\)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex KeyAfter();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();
}

/// One window drawn, where each caption on it is, and how tall its title bar is.
internal sealed record DrawnWindow(Bitmap Picture, IReadOnlyDictionary<string, Rectangle> Places, int TitleHeight);

/// <summary>
/// A section's screen and the windows it opens, each drawn once, for as
/// many outlined pictures of them as the answer has steps.
/// </summary>
internal sealed class ScreenDrawing : IDisposable
{
    private readonly IReadOnlyList<DrawnWindow> _screens;

    public ScreenDrawing(IReadOnlyList<DrawnWindow> screens) => _screens = screens;

    /// How many of the windows could be drawn.
    internal int Count => _screens.Count;

    public bool Has(string caption) => _screens.Any(s => s.Places.ContainsKey(GuideScreens.Normalise(caption)));

    /// The window the first caption is on - the screen itself where there
    /// is none - and those of the captions it has.
    private (DrawnWindow Screen, List<Rectangle> Places) Find(IReadOnlyList<string> captions)
    {
        var names = captions.Select(GuideScreens.Normalise).ToList();
        var screen = names.Select(n => _screens.FirstOrDefault(s => s.Places.ContainsKey(n)))
                          .FirstOrDefault(s => s is not null) ?? _screens[0];
        return (screen, names.Where(screen.Places.ContainsKey).Select(n => screen.Places[n]).ToList());
    }

    /// The window with <paramref name="captions"/> outlined in red; the
    /// screen plain where there are none.
    public Bitmap Outlined(IReadOnlyList<string> captions)
    {
        var (screen, places) = Find(captions);
        var picture = new Bitmap(screen.Picture);

        using var g = Graphics.FromImage(picture);
        using var pen = new Pen(Color.Red, 4f);
        foreach (var at in places)
        {
            var box = at;
            box.Inflate(4, 4);
            g.DrawRectangle(pen, box);
        }

        return picture;
    }

    /// <summary>
    /// The part of the window around <paramref name="captions"/>, big enough
    /// to see where on it they are; the whole window where there are none.
    /// Shown at nearly full size, a button can be read.
    /// </summary>
    public Rectangle Around(IReadOnlyList<string> captions)
    {
        var (screen, places) = Find(captions);
        var whole = new Rectangle(Point.Empty, screen.Picture.Size);
        if (places.Count == 0) return whole;

        var at = places.Aggregate(Rectangle.Union);
        var size = new Size(Math.Min(whole.Width, Math.Max(640, at.Width + 160)),
                            Math.Min(whole.Height, Math.Max(220, at.Height + 120)));
        var x = Math.Clamp(at.Left + at.Width / 2 - size.Width / 2, 0, whole.Width - size.Width);
        var y = Math.Clamp(at.Top + at.Height / 2 - size.Height / 2, 0, whole.Height - size.Height);
        return new Rectangle(new Point(x, y), size);
    }

    /// <summary>
    /// The picture for a step: the part of the window around
    /// <paramref name="captions"/>, outlined, with the window's title bar
    /// kept above it so the window can be told by name, and a frame.
    /// </summary>
    public Bitmap Picture(IReadOnlyList<string> captions)
    {
        var (screen, _) = Find(captions);
        var part = Around(captions);
        using var whole = Outlined(captions);

        // Cut from lower down: the title above, then a gap with a dashed line.
        var title = part.Top > screen.TitleHeight ? screen.TitleHeight : 0;
        var gap = title > 0 ? 10 : 0;

        var picture = new Bitmap(part.Width + 2, title + gap + part.Height + 2);
        using var g = Graphics.FromImage(picture);
        g.Clear(Color.White);

        if (title > 0)
        {
            g.DrawImage(whole, new Rectangle(1, 1, part.Width, title),
                        new Rectangle(0, 0, part.Width, title), GraphicsUnit.Pixel);
            using var dashes = new Pen(Color.Silver) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            g.DrawLine(dashes, 1, 1 + title + gap / 2, part.Width, 1 + title + gap / 2);
        }

        g.DrawImage(whole, new Rectangle(1, 1 + title + gap, part.Width, part.Height), part, GraphicsUnit.Pixel);
        using var frame = new Pen(Color.FromArgb(0xC8, 0xCD, 0xD4));
        g.DrawRectangle(frame, 0, 0, picture.Width - 1, picture.Height - 1);

        return picture;
    }

    public void Dispose()
    {
        foreach (var screen in _screens) screen.Picture.Dispose();
    }
}
