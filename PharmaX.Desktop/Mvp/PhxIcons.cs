using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PharmaX.Desktop.Mvp;

/// <summary>
/// The icons PharmaX draws, by their code in the Tabler icon font.
/// </summary>
/// <remarks>
/// Codes from @tabler/icons-webfont 3.49.0. One outlined set throughout,
/// so a pill on a home tile and a pill in a menu are the same pill.
/// </remarks>
public enum PhxIcon
{
    Adjustments = 0xEA03,
    Archive = 0xEA0B,
    CashBanknote = 0xEE25,
    Coins = 0xF65D,
    DatabaseExport = 0xEE6E,
    DatabaseImport = 0xEE6F,
    FileImport = 0xEDEA,
    Language = 0xEBBE,
    Lock = 0xEAE2,
    Stethoscope = 0xEDBE,
    AdjustmentsHorizontal = 0xEC38,
    AlertTriangle = 0xEA06,
    ArrowBackUp = 0xEB77,
    ArrowsExchange = 0xF1F4,
    Barcode = 0xEBC6,
    Building = 0xEA4F,
    BuildingFactory = 0xEE1C,
    BuildingStore = 0xEA4E,
    Calculator = 0xEB80,
    CalendarCheck = 0xF824,
    CalendarX = 0xF832,
    Cash = 0xEA55,
    CashRegister = 0xFEE6,
    Category = 0xF1F6,
    ChartBar = 0xEA59,
    Check = 0xEA5E,
    ClipboardCheck = 0xEA6C,
    ClipboardList = 0xEA6D,
    ClipboardPlus = 0xEFB2,
    Clock = 0xEA70,
    DeviceFloppy = 0xEB62,
    DoorExit = 0xEF4D,
    Eye = 0xEA9A,
    File = 0xEAA4,
    FileDollar = 0xEFE0,
    FileExport = 0xEDE9,
    FileInvoice = 0xEB67,
    GitMerge = 0xEAB5,
    HeartHandshake = 0xF0F3,
    HelpCircle = 0xF91D,
    History = 0xEBEA,
    InfoCircle = 0xEAC5,
    Key = 0xEAC7,
    ListDetails = 0xEF40,
    ListTree = 0xFAFA,
    Login = 0xEBA7,
    Logout = 0xEBA8,
    Notebook = 0xEB96,
    Package = 0xEAFF,
    Palette = 0xEB01,
    Pencil = 0xEB04,
    Percentage = 0xECF4,
    Photo = 0xEB0A,
    Paperclip = 0xEB02,
    Pill = 0xEC44,
    PlayerPause = 0xED45,
    PlugConnected = 0xF00A,
    PlugConnectedX = 0xF0A0,
    Plus = 0xEB0B,
    Prescription = 0xEF99,
    Printer = 0xEB0E,
    Receipt = 0xEDFD,
    ReceiptRefund = 0xEDFC,
    Refresh = 0xEB13,
    ReportMoney = 0xEECD,
    Scale = 0xEBC2,
    Search = 0xEB1C,
    Send = 0xEB1E,
    Settings = 0xEB20,
    ShoppingCart = 0xEB25,
    Stack = 0xEEF7,
    Tags = 0xEF86,
    Terminal = 0xEBEF,
    Trash = 0xEB41,
    TrashX = 0xEF88,
    Truck = 0xEBC4,
    TruckDelivery = 0xEC4B,
    User = 0xEB4D,
    UserDollar = 0xF9D5,
    Users = 0xEBF2,
    X = 0xEB55
}

/// <summary>
/// Draws icons from the Tabler font compiled into PharmaX, and the
/// application's own icon for every window and the taskbar.
/// </summary>
/// <remarks>
/// A font rather than picture files: one 1.8 MB file holds every icon,
/// each is drawn sharp at whatever size the display scaling asks for, and
/// in whatever colour the palette says. Windows' own icon font would have
/// done for some of these, but it has no pill and no prescription, and
/// Windows 10 - what the pharmacy PC runs - lacks the newer one entirely.
/// </remarks>
public static class PhxIcons
{
    private const string FontResource = "PharmaX.Desktop.Resources.tabler-icons.ttf";
    private const string IconResource = "PharmaX.Desktop.Resources.pharmax.ico";

    private static readonly Lazy<FontFamily?> Family = new(LoadFont);
    private static readonly Lazy<Icon?> App = new(LoadIcon);
    private static readonly Dictionary<(PhxIcon, int, int), Bitmap> Cache = new();

    // GDI+ keeps a pointer to the font's bytes, not a copy, so both the
    // block and the collection live as long as the process does.
    private static PrivateFontCollection? _fonts;

    /// The window and taskbar icon: the logo's capsule on its navy.
    public static Icon? AppIcon => App.Value;

    /// <summary>An icon, <paramref name="size"/> pixels square.</summary>
    /// <remarks>
    /// Kept once drawn: a menu repaints its images on every hover. A
    /// missing font gives a blank image rather than a window that will
    /// not open.
    /// </remarks>
    public static Bitmap Image(PhxIcon icon, int size, Color colour)
    {
        size = Math.Max(size, 8);
        var key = (icon, size, colour.ToArgb());

        lock (Cache)
        {
            if (!Cache.TryGetValue(key, out var bitmap))
            {
                bitmap = Render(icon, size, colour);
                Cache[key] = bitmap;
            }

            return bitmap;
        }
    }

    private static Bitmap Render(PhxIcon icon, int size, Color colour)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        if (Family.Value is not FontFamily family) return bitmap;

        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        // The glyph as a shape, centred on its own bounds: the em square
        // is the icon's 24-unit grid, so every icon keeps the weight it
        // was drawn with whatever its outline happens to cover.
        using var path = new GraphicsPath();
        path.AddString(char.ConvertFromUtf32((int)icon), family,
            (int)FontStyle.Regular, size, PointF.Empty,
            StringFormat.GenericTypographic);

        var bounds = path.GetBounds();
        if (bounds.Width <= 0 || bounds.Height <= 0) return bitmap;

        using var centre = new Matrix();
        centre.Translate(size / 2f - (bounds.X + bounds.Width / 2f),
                         size / 2f - (bounds.Y + bounds.Height / 2f));
        path.Transform(centre);

        using var brush = new SolidBrush(colour);
        graphics.FillPath(brush, path);
        return bitmap;
    }

    private static FontFamily? LoadFont()
    {
        using var stream = typeof(PhxIcons).Assembly
            .GetManifestResourceStream(FontResource);

        if (stream is null) return null;

        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);

        var memory = Marshal.AllocCoTaskMem(bytes.Length);
        Marshal.Copy(bytes, 0, memory, bytes.Length);

        _fonts = new PrivateFontCollection();
        _fonts.AddMemoryFont(memory, bytes.Length);
        return _fonts.Families.FirstOrDefault();
    }

    private static Icon? LoadIcon()
    {
        using var stream = typeof(PhxIcons).Assembly
            .GetManifestResourceStream(IconResource);

        return stream is null ? null : new Icon(stream);
    }

    // ---- Buttons ---------------------------------------------------------

    /// <summary>What a button's caption says it does, as an icon.</summary>
    /// <remarks>
    /// Checked in order and the first word counts first, as FunctionKeys
    /// reads captions: "Reprint labels" is a printer, "Remove item" a bin.
    /// </remarks>
    private static readonly (string Words, PhxIcon Icon)[] Meanings =
    {
        ("reprint", PhxIcon.Printer), ("print", PhxIcon.Printer),
        ("preview", PhxIcon.Eye), ("what prints", PhxIcon.Eye),
        ("view", PhxIcon.Eye), ("export", PhxIcon.FileExport),
        ("find", PhxIcon.Search), ("search", PhxIcon.Search),
        ("look up", PhxIcon.Search), ("lookup", PhxIcon.Search),
        ("add", PhxIcon.Plus), ("new", PhxIcon.Plus),
        ("edit", PhxIcon.Pencil), ("change", PhxIcon.Pencil),
        ("correct", PhxIcon.Pencil), ("amend", PhxIcon.Pencil),
        ("refresh", PhxIcon.Refresh), ("reload", PhxIcon.Refresh),
        ("save", PhxIcon.DeviceFloppy), ("pay", PhxIcon.Cash),
        ("take payment", PhxIcon.Cash), ("cash up", PhxIcon.CashRegister),
        ("open session", PhxIcon.CashRegister), ("x-read", PhxIcon.Receipt),
        ("declare", PhxIcon.ClipboardCheck), ("dispense", PhxIcon.Pill),
        ("issue", PhxIcon.Pill), ("post", PhxIcon.Send),
        ("suspended", PhxIcon.Clock), ("give", PhxIcon.Check),
        ("lock", PhxIcon.Lock), ("no sale", PhxIcon.CashBanknote),
        ("receive", PhxIcon.TruckDelivery), ("log in", PhxIcon.Login),
        ("sign in", PhxIcon.Login), ("merge", PhxIcon.GitMerge),
        ("refund", PhxIcon.ReceiptRefund), ("return", PhxIcon.ArrowBackUp),
        ("reverse", PhxIcon.ArrowBackUp), ("remove", PhxIcon.Trash),
        ("delete", PhxIcon.Trash), ("void", PhxIcon.TrashX),
        ("write off", PhxIcon.TrashX), ("discard", PhxIcon.TrashX),
        ("hold", PhxIcon.PlayerPause), ("park", PhxIcon.PlayerPause),
        ("quote", PhxIcon.FileDollar), ("recall", PhxIcon.History),
        ("resume", PhxIcon.History), ("choose image", PhxIcon.Photo),
        ("attach", PhxIcon.Paperclip),
        ("settle", PhxIcon.Check), ("apply", PhxIcon.Check),
        ("confirm", PhxIcon.Check), ("finalise", PhxIcon.Check),
        ("record", PhxIcon.Check), ("ok", PhxIcon.Check),
        ("done", PhxIcon.Check), ("exit", PhxIcon.DoorExit),
        ("close", PhxIcon.X), ("cancel", PhxIcon.X)
    };

    public static PhxIcon? ForCaption(string? caption)
    {
        var words = FunctionKeys.Words(caption);
        if (words.Length == 0) return null;

        foreach (var (meaning, icon) in Meanings)
        {
            if (words == meaning
                || words.StartsWith(meaning + " ", StringComparison.Ordinal))
                return icon;
        }

        foreach (var (meaning, icon) in Meanings)
        {
            if ((" " + words + " ").Contains(" " + meaning + " ",
                    StringComparison.Ordinal))
                return icon;
        }

        return null;
    }

    // The buttons given an icon, so a later caption, size or palette
    // change can draw it again - or take it away where it no longer fits.
    private static readonly ConditionalWeakTable<Button, object> Decorated = new();

    /// <summary>
    /// Puts an icon on every button whose caption says what it does, where
    /// the icon fits beside the caption at the caption's own size.
    /// </summary>
    /// <remarks>
    /// A button too narrow for both keeps its words and goes without: an
    /// icon is never worth shrinking "F6 Cash up and close" to fit it.
    /// </remarks>
    public static void Decorate(Control root)
    {
        ArgumentNullException.ThrowIfNull(root);

        foreach (Control child in root.Controls)
        {
            if (child is Button button && ForCaption(button.Text) is not null)
            {
                if (!Decorated.TryGetValue(button, out _))
                {
                    Decorated.Add(button, new object());
                    button.TextChanged += (_, _) => Fit(button);
                    button.SizeChanged += (_, _) => Fit(button);
                    button.EnabledChanged += (_, _) => Fit(button);
                }

                Fit(button);
            }

            if (child.HasChildren) Decorate(child);
        }
    }

    private static void Fit(Button button)
    {
        // System-drawn buttons cannot show an image at all.
        if (button.FlatStyle == FlatStyle.System) return;

        var icon = ForCaption(button.Text);
        var size = button.Font.Height;
        var room = button.Width - button.Padding.Horizontal
                   - button.LogicalToDeviceUnits(12);
        var text = TextRenderer.MeasureText(button.Text, button.Font).Width;

        if (icon is not PhxIcon chosen
            || (!button.AutoSize && text + size + button.LogicalToDeviceUnits(6) > room))
        {
            button.Image = null;
            return;
        }

        // A filled button carries its icon in its own text colour; any
        // other in the palette's, so the icons read as one set.
        var colour = !button.Enabled
            ? PhxTheme.Muted
            : button.FlatStyle == FlatStyle.Flat ? button.ForeColor : PhxTheme.Title;

        button.Image = Image(chosen, size, colour);
        button.ImageAlign = ContentAlignment.MiddleCenter;
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.TextImageRelation = TextImageRelation.ImageBeforeText;
    }
}
