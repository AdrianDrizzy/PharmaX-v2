namespace PharmaX.Desktop.Mvp;

/// <summary>
/// The look of PharmaX in one place: fonts, colours and the spacing
/// every screen measures from.
/// </summary>
/// <remarks>
/// A theme rather than per-form choices, because forty screens each
/// picking their own font size is how a product ends up looking like
/// forty products. Applied by PhxFormBase, so a new screen inherits it
/// without doing anything.
/// </remarks>
public static class PhxTheme
{
    // ---- Spacing ---------------------------------------------------------

    /// The gutter every screen leaves round its edge.
    public const int Gutter = 12;

    /// The height of a header or footer band.
    public const int BandHeight = 52;

    /// One row of a labelled form.
    public const int RowHeight = 34;

    // ---- Type ------------------------------------------------------------

    public static Font Body { get; } = new("Segoe UI", 9.75f);

    public static Font Emphasis { get; } =
        new("Segoe UI", 9.75f, FontStyle.Bold);

    /// The name of the pharmacy on the shell's home screen.
    public static Font Display { get; } =
        new("Segoe UI", 19f, FontStyle.Bold);

    /// The title of a screen or a section of one.
    public static Font Heading { get; } =
        new("Segoe UI", 13f, FontStyle.Bold);

    public static Font Subheading { get; } =
        new("Segoe UI", 10.5f, FontStyle.Bold);

    /// <summary>
    /// Figures. Still Segoe UI rather than a monospaced face: its digits
    /// are already the same width as each other, so a right-aligned
    /// column of money lines up on the decimal point without the screen
    /// looking like a terminal.
    /// </summary>
    public static Font Figure { get; } = new("Segoe UI", 11f);

    public static Font FigureLarge { get; } =
        new("Segoe UI", 18f, FontStyle.Bold);

    // ---- Colour ----------------------------------------------------------
    //
    // Built from a Palette - by default the one in the pharmacy's logo:
    // its primary carries the buttons, titles and selections, its highlight
    // the rules and outlines. Every other shade below is worked out from
    // those two by Use. The highlight never sits behind text, because a
    // bright brand colour rarely reads white text well (the BethRapha
    // orange manages 3:1); HighlightText is its readable shade.

    /// The palette in use.
    public static Palette Palette { get; private set; } = Palette.NavyAndOrange;

    /// Light, dark or one of the others: what the shades are mixed from.
    public static ThemeMode Mode { get; private set; } = ThemeMode.Light;

    /// The ground of text boxes, lists and grids.
    public static Color Field { get; private set; }

    /// Text.
    public static Color Ink { get; private set; }

    /// An ordinary button, as against the filled primary one.
    public static Color ButtonFace { get; private set; }

    /// <summary>
    /// A card on the page: a home tile, the home header, the suggestions
    /// under a search box.
    /// </summary>
    /// <remarks>
    /// White in the light mode, but built with FromArgb rather than
    /// Color.White: Recolour swaps colours by value, and a Color.White
    /// key would turn the white captions on every primary button dark.
    /// </remarks>
    public static Color Card { get; private set; }

    /// The primary: buttons, titles, selections. Always reads white text.
    public static Color Brand { get; private set; }

    /// The primary pressed or under the mouse.
    public static Color BrandDeep { get; private set; }

    /// <summary>
    /// Words and icons in the brand's colour on the page: a tile's name,
    /// the pharmacy's name, a total. Not a fill - Brand is that.
    /// </summary>
    /// <remarks>
    /// Brand has to stay dark enough to carry white text on a button, which
    /// on a dark page left it a dim navy that barely read as words. Here it
    /// is lifted into a bright tint of itself - of the glow, in a mode that
    /// has one - at 7:1. On a light page it is the brand a shade deeper,
    /// which also keeps the two apart for Recolour.
    /// </remarks>
    public static Color Title { get; private set; }

    /// Rules and outlines only.
    public static Color Highlight { get; private set; }

    /// The highlight darkened until text in it reads at 4.5:1 or better.
    public static Color HighlightText { get; private set; }

    /// The page behind a screen's content: white with a breath of the brand.
    public static Color Surface { get; private set; }

    /// A header or footer band, one step darker than the surface.
    public static Color Band { get; private set; }

    public static Color Line { get; private set; }

    public static Color Muted { get; private set; }

    /// Under the mouse: a card that would open if clicked.
    public static Color Hover { get; private set; }

    /// The selected row of a list.
    public static Color Selection { get; private set; }

    /// Every other row of a list.
    public static Color AlternateRow { get; private set; }

    /// Informative, not alarming: offline is a normal working state.
    public static Color Information { get; private set; }

    /// Something needs a decision before it can proceed.
    public static Color Attention { get; private set; }

    /// Something is wrong and a person has to look at it.
    public static Color Problem { get; private set; }

    /// The words that say so: an error under a form, a total out of balance.
    public static Color ProblemText { get; private set; }

    /// A figure that is short rather than over.
    public static Color Negative => ProblemText;

    /// <summary>
    /// The box the keyboard is in. The field tinted with the highlight -
    /// cyan in Midnight neon - so a till operator looking up from the
    /// scanner can see where the next keystroke goes.
    /// </summary>
    public static Color Focus { get; private set; }

    /// The one colour the eye is meant to be drawn to on a screen.
    public static Color Accent => Brand;

    public static Color AccentDisabled { get; private set; }

    static PhxTheme() => Use(Palette.NavyAndOrange);

    /// <summary>
    /// Raised after Use, with each colour that changed and what it became,
    /// so an open window can swap its old colours for the new ones.
    /// </summary>
    public static event EventHandler<IReadOnlyDictionary<Color, Color>>? Changed;

    /// <summary>
    /// Builds every shade from a palette and a mode - the mode in use if
    /// none is given - and puts them in use.
    /// </summary>
    public static void Use(Palette palette, ThemeMode? mode = null)
    {
        ArgumentNullException.ThrowIfNull(palette);

        mode ??= Mode;
        var before = Shades();
        var paper = mode.Paper;

        Palette = palette;
        Mode = mode;

        // White text on it, and - on a dark page, lifted until it does -
        // readable as a title on the page too.
        Brand = Palette.Readable(
            Palette.Readable(palette.Primary, Color.White), paper, 3.0);

        // What the page is tinted towards. On a dark page the brand alone
        // would darken it further, so it is lightened with the ink first.
        var tint = mode.IsDark ? Palette.Mix(Brand, mode.Ink, 0.5) : Brand;

        Field = mode.Field;
        Ink = mode.Ink;
        ButtonFace = mode == ThemeMode.Light
            ? SystemColors.Control
            : Palette.Mix(mode.Field, mode.Ink, 0.08);
        Card = mode == ThemeMode.Light
            ? Color.FromArgb(255, 255, 255)
            : mode.IsDark ? Palette.Mix(mode.Field, mode.Ink, 0.04) : mode.Field;

        BrandDeep = Palette.Mix(Brand, Color.Black, 0.2);
        Highlight = mode.Glow ?? palette.Highlight;
        HighlightText = Palette.Readable(Highlight, paper, 4.8);
        Surface = Palette.Mix(paper, tint, 0.04);
        Band = Palette.Mix(paper, tint, 0.09);
        Line = Palette.Mix(paper, tint, 0.18);
        Hover = mode.IsDark
            ? Palette.Mix(Card, tint, 0.12)
            : Palette.Mix(paper, tint, 0.06);
        Selection = Palette.Mix(paper, tint, mode.IsDark ? 0.3 : 0.16);
        AlternateRow = Palette.Mix(mode.IsDark ? mode.Field : paper, tint,
                                   mode.IsDark ? 0.04 : 0.025);
        Information = Palette.Mix(paper, tint, 0.11);
        AccentDisabled = Palette.Mix(paper, tint, 0.13);
        // A quieter grey than the ink, but on a dark page one that is still
        // plainly read: the brand-tinted grey of the light mode sank into it.
        Muted = mode.IsDark
            ? Palette.Readable(Palette.Mix(mode.Ink, Band, 0.3), Band, 7.0)
            : Palette.Readable(
                Palette.Mix(Brand, Color.FromArgb(107, 114, 128), 0.6), Band);

        Title = mode.IsDark
            ? Palette.Readable(Palette.Mix(mode.Glow ?? Brand, mode.Ink, 0.4), Card, 7.0)
            : Palette.Mix(Brand, mode.Ink, 0.1);

        // Pale amber and pink on a light page, as they have always been. On
        // a dark one a pale block glares and will not take the light ink, so
        // the field is tinted with the mode's own warning and error instead.
        var warning = mode.Warning ?? Color.FromArgb(244, 187, 79);
        var error = mode.Error ?? Color.FromArgb(255, 98, 110);

        Attention = mode.IsDark
            ? Palette.Mix(Field, warning, 0.22)
            : Color.FromArgb(255, 244, 214);
        Problem = mode.IsDark
            ? Palette.Mix(Field, error, 0.22)
            : Color.FromArgb(255, 226, 226);
        ProblemText = mode.IsDark
            ? Palette.Readable(error, Surface)
            : Color.FromArgb(168, 32, 32);
        Focus = Palette.Mix(Field, Highlight, mode.IsDark ? 0.18 : 0.10);

        // The menu and status bars draw themselves in the system's
        // colours unless told otherwise. Swapped only on the way into or
        // out of light, which keeps the system's renderer untouched.
        var themed = ToolStripManager.Renderer is StripRenderer;

        if (themed == (mode == ThemeMode.Light))
            ToolStripManager.Renderer = themed
                ? new ToolStripProfessionalRenderer()
                : new StripRenderer();

        var after = Shades();
        var changes = new Dictionary<Color, Color>();

        for (var i = 0; i < before.Length; i++)
            if (before[i] != after[i]) changes.TryAdd(before[i], after[i]);

        if (changes.Count == 0) return;

        // What a designer wrote in by name was mapped when each window
        // opened; one open across the change - the shell - is mapped now.
        if (mode != ThemeMode.Light)
            foreach (var (from, to) in FromSystem()) changes.TryAdd(from, to);

        Changed?.Invoke(null, changes);
    }

    private static Color[] Shades() => new[]
    {
        Brand, BrandDeep, Highlight, HighlightText, Surface, Band, Line, Muted,
        Hover, Selection, AlternateRow, Information, AccentDisabled,
        Field, Ink, ButtonFace, Card, Attention, Problem, ProblemText, Focus,
        Title
    };

    /// <summary>
    /// The system colours a designer wrote in by name, and the shade each
    /// becomes in a mode other than light.
    /// </summary>
    private static Dictionary<Color, Color> FromSystem() => new()
    {
        [SystemColors.ControlText] = Ink,
        [SystemColors.WindowText] = Ink,
        [SystemColors.Window] = Field,
        [SystemColors.Control] = ButtonFace,
        [SystemColors.GrayText] = Muted
    };

    /// <summary>
    /// A dark title bar on a dark mode, where Windows 10 2004 or later
    /// draws one, and the window's edge in the mode's glow where Windows
    /// 11 draws a border; elsewhere the calls do nothing.
    /// </summary>
    public static void TitleBar(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (!form.IsHandleCreated) return;

        var dark = Mode.IsDark ? 1 : 0;
        _ = NativeMethods.DwmSetWindowAttribute(form.Handle,
            NativeMethods.UseImmersiveDarkMode, ref dark, sizeof(int));

        var border = Mode.Glow is Color glow
            ? ColorTranslator.ToWin32(glow)
            : NativeMethods.DefaultColour;
        _ = NativeMethods.DwmSetWindowAttribute(form.Handle,
            NativeMethods.BorderColour, ref border, sizeof(int));
    }

    private static class NativeMethods
    {
        public const int UseImmersiveDarkMode = 20;
        public const int BorderColour = 34;
        public const int DefaultColour = unchecked((int)0xFFFFFFFF);

        public const uint FrameChanged = 0x0020 | 0x0001 | 0x0002 | 0x0004 | 0x0010;

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd,
            int attribute, ref int value, int size);

        [System.Runtime.InteropServices.DllImport("uxtheme.dll",
            CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        public static extern int SetWindowTheme(IntPtr hwnd, string? appName,
            string? idList);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(
            System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after,
            int x, int y, int width, int height, uint flags);
    }

    // ---- Native controls -------------------------------------------------

    // What the tables below hold against each control: only being there
    // counts.
    private static readonly object Mark = new();

    // The controls given Windows' dark theme, so the way back to light
    // undoes only what was done and a light window is never touched.
    private static readonly System.Runtime.CompilerServices
        .ConditionalWeakTable<Control, object> Darkened = new();

    /// <summary>
    /// Scroll bars and drop-down boxes as dark as the window, which
    /// Windows draws only for a control that asks for its dark theme by
    /// name. Asked again whenever the control's window is made afresh.
    /// </summary>
    private static void Native(Control control)
    {
        if (control is not (ScrollBar or ListBox or TreeView or ComboBox
                or TextBoxBase { Multiline: true }
                or ScrollableControl { AutoScroll: true })
            || control is Form) return;

        control.HandleCreated -= OnNativeHandle;
        control.HandleCreated += OnNativeHandle;

        if (control.IsHandleCreated) DarkenNative(control);
    }

    private static void OnNativeHandle(object? sender, EventArgs e)
    {
        if (sender is Control control) DarkenNative(control);
    }

    private static void DarkenNative(Control control)
    {
        if (Mode.IsDark)
        {
            _ = NativeMethods.SetWindowTheme(control.Handle,
                control is ComboBox ? "DarkMode_CFD" : "DarkMode_Explorer", null);
            Darkened.AddOrUpdate(control, Mark);
        }
        else if (Darkened.Remove(control))
        {
            _ = NativeMethods.SetWindowTheme(control.Handle, null, null);
        }
        else return;

        // The scroll bars sit in the window's frame, which is redrawn only
        // when told it has changed.
        _ = NativeMethods.SetWindowPos(control.Handle, IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.FrameChanged);
        control.Invalidate();
    }

    // ---- Opting out ------------------------------------------------------

    private static readonly System.Runtime.CompilerServices
        .ConditionalWeakTable<Control, object> Excluded = new();

    /// <summary>
    /// Leaves a control, and everything on it, as its screen set it: the
    /// test database's amber status bar, the logo on its white plate.
    /// </summary>
    public static void Exclude(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        Excluded.AddOrUpdate(control, Mark);
    }

    private static bool IsExcluded(Control control) =>
        Excluded.TryGetValue(control, out _);

    // ---- Focus -----------------------------------------------------------

    // Only over the plain field, and only back to it: a box coloured for a
    // reason - a variance, an error - keeps its colour while typed in.
    private static void OnFocusEnter(object? sender, EventArgs e)
    {
        if (sender is Control box && box.BackColor == Field) box.BackColor = Focus;
    }

    private static void OnFocusLeave(object? sender, EventArgs e)
    {
        if (sender is Control box && box.BackColor == Focus) box.BackColor = Field;
    }

    /// <summary>
    /// A control put on a screen after it opened is themed as it arrives,
    /// as it would have been had it been there from the start.
    /// </summary>
    private static void OnControlAdded(object? sender, ControlEventArgs e)
    {
        if (e.Control is not Control control) return;
        if (sender is Control parent && IsExcluded(parent)) return;

        ApplyTo(control);
        if (Mode != ThemeMode.Light) Recolour(control, FromSystem());
    }

    /// <summary>
    /// The theme for a window that does not inherit PhxFormBase - the
    /// prompts, the payment dialog: its colours when it opens, and the new
    /// ones if the theme changes while it is open.
    /// </summary>
    /// <remarks>
    /// Colours only. These dialogs were laid out at the default font, and
    /// giving them the theme's would make every caption a size larger than
    /// the box it was measured for. Not PhxFormBase either, which would
    /// also give them function-key captions, a branded title, a watermark
    /// and its own Escape handling.
    /// </remarks>
    public static void Follow(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);

        void OnChange(object? sender, IReadOnlyDictionary<Color, Color> changes)
        {
            if (form.IsDisposed) return;

            if (form.InvokeRequired)
            {
                form.BeginInvoke(() => OnChange(sender, changes));
                return;
            }

            Recolour(form, changes);
            TitleBar(form);
            form.Invalidate(true);
        }

        form.Load += (_, _) => Colour(form);
        form.HandleCreated += (_, _) => Changed += OnChange;
        form.HandleDestroyed += (_, _) => Changed -= OnChange;
    }

    /// <summary>
    /// The menu, status bar and drop-downs in the mode's colours. Only
    /// outside the light mode, which keeps the system's own look.
    /// </summary>
    private sealed class StripRenderer : ToolStripProfessionalRenderer
    {
        public StripRenderer() : base(new StripColours()) =>
            RoundedEdges = false;

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            // Only the default text colours: the waiting count and the
            // stock alerts keep their own. Black on a bar given a pale
            // colour of its own - the test database's yellow status bar.
            if (e.TextColor == SystemColors.ControlText
                || e.TextColor == SystemColors.MenuText
                || e.TextColor == SystemColors.HighlightText
                || e.TextColor == SystemColors.GrayText
                || e.TextColor == Ink)
                e.TextColor = e.ToolStrip is not ToolStripDropDown
                              && Palette.Luminance(e.ToolStrip.BackColor) > 0.5
                    ? Color.Black
                    : e.Item.Enabled ? Ink : Muted;

            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Ink;
            base.OnRenderArrow(e);
        }
    }

    private sealed class StripColours : ProfessionalColorTable
    {
        public StripColours() => UseSystemColors = false;

        public override Color MenuStripGradientBegin => Band;
        public override Color MenuStripGradientEnd => Band;
        public override Color StatusStripGradientBegin => Band;
        public override Color StatusStripGradientEnd => Band;
        public override Color ToolStripGradientBegin => Band;
        public override Color ToolStripGradientMiddle => Band;
        public override Color ToolStripGradientEnd => Band;
        public override Color ToolStripBorder => Line;
        public override Color ToolStripDropDownBackground => Card;
        public override Color ImageMarginGradientBegin => Card;
        public override Color ImageMarginGradientMiddle => Card;
        public override Color ImageMarginGradientEnd => Card;
        public override Color MenuBorder => Line;
        public override Color MenuItemBorder => Highlight;
        public override Color MenuItemSelected => Selection;
        public override Color MenuItemSelectedGradientBegin => Selection;
        public override Color MenuItemSelectedGradientEnd => Selection;
        public override Color MenuItemPressedGradientBegin => Selection;
        public override Color MenuItemPressedGradientMiddle => Selection;
        public override Color MenuItemPressedGradientEnd => Selection;
        public override Color CheckBackground => Selection;
        public override Color CheckSelectedBackground => Selection;
        public override Color CheckPressedBackground => Selection;
        public override Color ButtonSelectedHighlight => Selection;
        public override Color ButtonSelectedGradientBegin => Selection;
        public override Color ButtonSelectedGradientEnd => Selection;
        public override Color SeparatorDark => Line;
        public override Color SeparatorLight => Line;
    }

    /// <summary>
    /// Swaps the old palette's colours for the new ones everywhere a window
    /// holds them: back and fore colours, flat buttons and grids.
    /// </summary>
    /// <remarks>
    /// Screens copy theme colours into their controls when they are built,
    /// so changing the palette changes nothing on a window already open.
    /// Swapping by value reaches every one of those copies without any
    /// screen having to know a palette can change.
    /// </remarks>
    public static void Recolour(Control root, IReadOnlyDictionary<Color, Color> changes)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(changes);

        if (IsExcluded(root)) return;

        Color Swap(Color colour) => changes.TryGetValue(colour, out var to) ? to : colour;

        Native(root);

        // Only where it changes: writing back an inherited or transparent
        // colour would pin it, or throw on a control that cannot be clear.
        if (changes.TryGetValue(root.BackColor, out var back)) root.BackColor = back;
        if (changes.TryGetValue(root.ForeColor, out var fore)) root.ForeColor = fore;

        switch (root)
        {
            case Button { FlatStyle: FlatStyle.Flat } button:
                button.FlatAppearance.MouseOverBackColor =
                    Swap(button.FlatAppearance.MouseOverBackColor);
                button.FlatAppearance.BorderColor =
                    Swap(button.FlatAppearance.BorderColor);
                break;

            // Back to the system's own button on the way back to light;
            // any other colour is drawn as set.
            case Button button:
                button.UseVisualStyleBackColor =
                    button.BackColor == SystemColors.Control;
                break;

            case TabPage page:
                Tab(page);
                break;

            case DataGridView grid:
                grid.GridColor = Swap(grid.GridColor);
                grid.BackgroundColor = Swap(grid.BackgroundColor);

                // The columns too: a designer gives one column its own
                // colour - greyed, or the one that is typed into.
                foreach (var style in new[]
                {
                    grid.ColumnHeadersDefaultCellStyle, grid.DefaultCellStyle,
                    grid.AlternatingRowsDefaultCellStyle, grid.RowsDefaultCellStyle
                }.Concat(grid.Columns.Cast<DataGridViewColumn>()
                    .Where(c => c.HasDefaultCellStyle)
                    .Select(c => c.DefaultCellStyle)))
                {
                    style.BackColor = Swap(style.BackColor);
                    style.ForeColor = Swap(style.ForeColor);
                    style.SelectionBackColor = Swap(style.SelectionBackColor);
                    style.SelectionForeColor = Swap(style.SelectionForeColor);
                }

                break;

            case ToolStrip strip:
                foreach (ToolStripItem item in strip.Items)
                    item.ForeColor = Swap(item.ForeColor);
                break;
        }

        foreach (Control child in root.Controls) Recolour(child, changes);
    }

    // ---- Building blocks -------------------------------------------------

    /// <summary>
    /// A header band across the top of a screen. Returns the panel so
    /// the caller can put its own labels in it.
    /// </summary>
    public static Panel HeaderBand(int height = BandHeight)
    {
        var band = new Panel
        {
            BackColor = Band,
            Dock = DockStyle.Top,
            Height = height,
            Padding = new Padding(Gutter, 8, Gutter, 8)
        };

        // The logo's orange along the foot of the band. Painted rather than
        // docked, so it sits in the padding and no caller's layout moves.
        band.Paint += (_, e) =>
        {
            using var brush = new SolidBrush(Highlight);
            e.Graphics.FillRectangle(brush, 0, band.Height - 3, band.Width, 3);
        };

        return band;
    }

    public static Panel FooterBand(int height = BandHeight) => new()
    {
        BackColor = Band,
        Dock = DockStyle.Bottom,
        Height = height,
        Padding = new Padding(Gutter, 8, Gutter, 8)
    };

    /// A one-pixel rule, for separating a band from the content.
    public static Panel Rule(DockStyle edge = DockStyle.Top) => new()
    {
        BackColor = Line,
        Dock = edge,
        Height = 1
    };

    public static Label Caption(string text, Point at, int width = 120) =>
        new()
        {
            AutoSize = false,
            Font = Body,
            ForeColor = Muted,
            Location = at,
            Size = new Size(width, 20),
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft
        };

    /// <summary>
    /// A right-aligned figure. Right-aligned because that is how a
    /// column of money is read, and money is most of what PharmaX shows.
    /// </summary>
    public static Label Figures(Point at, int width = 140,
                                bool large = false) => new()
    {
        AutoSize = false,
        Font = large ? FigureLarge : Figure,
        Location = at,
        Size = new Size(width, large ? 30 : 22),
        Text = Format.Amount(0m),
        TextAlign = ContentAlignment.MiddleRight
    };

    /// <summary>
    /// The one button on a screen that does the thing the screen exists
    /// for: post the receipt, take the payment, close the till.
    /// </summary>
    /// <remarks>
    /// Filled rather than merely bolder. On a till the operator is not
    /// reading the screen, they are hitting the button they hit a
    /// thousand times before, and a block of colour is what the hand
    /// aims at. Flat style is also the marker Apply uses to leave these
    /// buttons alone, so the theme does not paint over them afterwards.
    /// </remarks>
    public static void MakePrimary(Button button)
    {
        ArgumentNullException.ThrowIfNull(button);

        button.Font = Emphasis;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.UseVisualStyleBackColor = false;

        // A flat-styled button keeps its colour when disabled, which
        // would leave a dead control looking like the live one.
        button.EnabledChanged += (sender, _) => PaintPrimary((Button)sender!);
        PaintPrimary(button);
    }

    private static void PaintPrimary(Button button)
    {
        button.BackColor = button.Enabled ? Accent : AccentDisabled;
        button.ForeColor = button.Enabled ? Color.White : Muted;

        button.FlatAppearance.MouseOverBackColor = button.Enabled
            ? BrandDeep
            : AccentDisabled;
    }

    /// <summary>
    /// Applies the theme to a form and everything already on it. Called
    /// by PhxFormBase after InitializeComponent, so a screen gets this
    /// without asking.
    /// </summary>
    public static void Apply(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);

        form.Font = Body;
        Colour(form);
        SeatRightAnchoredControls(form);
    }

    /// The colours of the theme on a form and everything on it, and on
    /// anything put on it later.
    private static void Colour(Form form)
    {
        form.BackColor = Surface;
        form.ForeColor = Ink;

        ApplyTo(form.Controls);

        form.ControlAdded -= OnControlAdded;
        form.ControlAdded += OnControlAdded;

        // What the designers wrote in by name - a black caption, a grey
        // panel - follows the mode too, rather than staying light on dark.
        if (Mode != ThemeMode.Light) Recolour(form, FromSystem());

        TitleBar(form);
    }

    /// <summary>
    /// Brings back any control that is anchored to the right-hand edge
    /// and has ended up off the side of the window.
    /// </summary>
    /// <remarks>
    /// A right-anchored control remembers how far its right edge sits
    /// from its parent's right edge, and it remembers that distance as
    /// it was the last time the control's own bounds changed. In a
    /// hand-written designer that moment is while the band it sits in
    /// is still the default 200 pixels wide, so the remembered distance
    /// comes out several hundred pixels negative and Windows Forms
    /// dutifully lays the control out that far past the edge. Close
    /// buttons, record counts and the whole totals block on two screens
    /// were invisible for this reason, on every screen, from the first
    /// build.
    ///
    /// Re-seating them here - once, after the form has its real size -
    /// teaches each anchor the distance it should have had, and the
    /// anchor behaves correctly from then on. Only controls that are
    /// actually off the edge are touched, so a screen that was laid out
    /// properly is left exactly as its designer wrote it.
    /// </remarks>
    private static void SeatRightAnchoredControls(Control parent)
    {
        var available = parent.ClientSize.Width - parent.Padding.Right;

        foreach (Control child in parent.Controls)
        {
            if (child.HasChildren) SeatRightAnchoredControls(child);

            var anchor = child.Anchor;

            if (!anchor.HasFlag(AnchorStyles.Right)
                || anchor.HasFlag(AnchorStyles.Left)) continue;

            if (child.Right <= available) continue;

            child.Left = available - Gutter - child.Width;
        }
    }

    private static void ApplyTo(Control.ControlCollection controls)
    {
        foreach (Control control in controls) ApplyTo(control);
    }

    private static void ApplyTo(Control control)
    {
        if (IsExcluded(control)) return;

        switch (control)
        {
            // Its own scroll bars and nothing else: the grid colours the
            // box a cell is edited in from the cell, flagged or not.
            case DataGridView grid:
                ApplyToGrid(grid);
                foreach (Control bar in grid.Controls) Native(bar);
                return;

            // Flat is MakePrimary's marker. Repainting it here is
            // what would quietly undo the accent on every screen.
            // Standard rather than System: it looks the same, and a
            // System-drawn button cannot show an icon.
            case Button button when button.FlatStyle != FlatStyle.Flat:
                button.FlatStyle = FlatStyle.Standard;
                break;

            case TextBox or ComboBox or ListBox or NumericUpDown or TreeView:
                control.BackColor = Field;
                control.ForeColor = Ink;
                break;

            case TabPage page when Mode != ThemeMode.Light:
                Tab(page);
                break;
        }

        if (control is TextBox or ComboBox or NumericUpDown)
        {
            control.Enter -= OnFocusEnter;
            control.Enter += OnFocusEnter;
            control.Leave -= OnFocusLeave;
            control.Leave += OnFocusLeave;
        }

        Native(control);

        control.ControlAdded -= OnControlAdded;
        control.ControlAdded += OnControlAdded;

        if (control.HasChildren) ApplyTo(control.Controls);
    }

    /// <summary>
    /// A tab page paints the system's own white unless told otherwise,
    /// which in a dark mode left light captions on white.
    /// </summary>
    private static void Tab(TabPage page)
    {
        page.UseVisualStyleBackColor = Mode == ThemeMode.Light;
        if (Mode != ThemeMode.Light) page.BackColor = Surface;
    }

    /// <summary>
    /// The grid settings every list in PharmaX wants. PhxGrid sets them
    /// itself; this catches the plain DataGridViews used where a grid has
    /// to be editable, which is where the default grey background and
    /// cramped rows were showing through.
    /// </summary>
    public static void ApplyToGrid(DataGridView grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        grid.BackgroundColor = Field;
        grid.BorderStyle = BorderStyle.None;
        grid.GridColor = Line;
        grid.EnableHeadersVisualStyles = false;
        grid.RowHeadersVisible = false;
        grid.AllowUserToResizeRows = false;

        if (grid.RowTemplate.Height < 26) grid.RowTemplate.Height = 26;

        grid.ColumnHeadersHeight = 32;
        grid.ColumnHeadersHeightSizeMode =
            DataGridViewColumnHeadersHeightSizeMode.EnableResizing;

        grid.ColumnHeadersDefaultCellStyle.BackColor = Band;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Ink;

        // The heading over the current cell otherwise turns system blue.
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Band;
        grid.ColumnHeadersDefaultCellStyle.Font = Emphasis;
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;

        grid.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        grid.DefaultCellStyle.BackColor = Field;
        grid.DefaultCellStyle.ForeColor = Ink;
        grid.DefaultCellStyle.SelectionBackColor = Selection;
        grid.DefaultCellStyle.SelectionForeColor = Ink;

        grid.AlternatingRowsDefaultCellStyle.BackColor =
            AlternateRow;

        // Detached first so that applying the theme twice to the same
        // grid does not stack handlers.
        grid.CellFormatting -= ReadableCell;
        grid.CellFormatting += ReadableCell;

        grid.DataError -= QuietDataError;
        grid.DataError += QuietDataError;
    }

    /// <summary>
    /// A value a cell cannot show or take is marked on the cell and
    /// logged, rather than raised as the framework's own modal dialog.
    /// </summary>
    /// <remarks>
    /// Nothing in the application handled DataError, so any value a grid
    /// could not format put up "DataGridView Default Error Dialog" - 47 of
    /// them in one test run across Returns, Claims and Capture Script -
    /// each one stopping the screen until it was dismissed (DEF-0017).
    /// The cause of those was ReadableCell below, and is fixed there; this
    /// is so that the next one is a red mark on a cell and a line in the
    /// log, not a wall of dialogs.
    /// </remarks>
    private static void QuietDataError(object? sender,
        DataGridViewDataErrorEventArgs e)
    {
        if (e is null) return;

        e.ThrowException = false;
        e.Cancel = e.Context.HasFlag(DataGridViewDataErrorContexts.Commit);

        if (e.Exception is not null) ErrorHandler.Log(e.Exception);

        if (sender is DataGridView grid && e.RowIndex >= 0
            && e.RowIndex < grid.Rows.Count && e.ColumnIndex >= 0)
            grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText =
                "This value could not be shown or saved: "
                + (e.Exception?.Message ?? "unknown problem");
    }

    /// <summary>
    /// Colours a grid row so that the colour survives selection.
    /// </summary>
    /// <remarks>
    /// A background set on the row alone disappears the moment the row
    /// is selected, and a grid selects its first row on its own. That
    /// is how a till that is eighty rand short came to look exactly
    /// like one that balanced. A row colour here is a warning, so it
    /// has to outlast a mouse click.
    /// </remarks>
    public static void Flag(DataGridViewRow row, Color colour)
    {
        ArgumentNullException.ThrowIfNull(row);

        row.DefaultCellStyle.BackColor = colour;
        row.DefaultCellStyle.SelectionBackColor = Darken(colour);
        row.DefaultCellStyle.SelectionForeColor = Ink;
    }

    private static Color Darken(Color colour) => Color.FromArgb(
        (int)(colour.R * 0.90), (int)(colour.G * 0.90),
        (int)(colour.B * 0.90));

    /// <summary>
    /// A column bound to a bool renders True and False by default,
    /// which is programmer output on a pharmacist's screen.
    /// </summary>
    /// <remarks>
    /// Only where the cell shows text. A tick box's formatted value has to
    /// stay a bool, and turning it into "Yes" was what raised the
    /// FormatException dialogs - and why Capture Script's "Generic ok" box
    /// showed unticked while its value was yes (DEF-0017).
    /// </remarks>
    private static void ReadableCell(object? sender,
        DataGridViewCellFormattingEventArgs e)
    {
        if (e is null) return;

        // A row flagged in a pale colour - an allergy, a shortage - keeps
        // dark text in a dark mode, where the ink is light.
        if (Mode.IsDark && e.CellStyle is { BackColor.A: > 0 } flagged
            && Palette.Luminance(flagged.BackColor) > 0.5)
        {
            flagged.ForeColor = Color.Black;
            flagged.SelectionForeColor = Color.Black;
        }

        // A time stored in UTC is shown in the till's own time. The
        // stocktake list and the users list bound their UTC columns
        // straight to the grid, so a count opened at 05:59 was listed at
        // 03:59 (DEF-0028). Every stored time carries the suffix, so the
        // rule follows the name rather than being repeated per screen.
        if (e.Value is DateTime stamp && sender is DataGridView grid
            && e.ColumnIndex >= 0
            && grid.Columns[e.ColumnIndex].DataPropertyName
                   .EndsWith("Utc", StringComparison.Ordinal))
        {
            var local = DateTime.SpecifyKind(stamp, DateTimeKind.Utc)
                                .ToLocalTime();

            e.Value = local.ToString(
                string.IsNullOrEmpty(e.CellStyle?.Format)
                    ? "dd/MM/yyyy HH:mm"
                    : e.CellStyle.Format,
                System.Globalization.CultureInfo.CurrentCulture);
            e.FormattingApplied = true;
            return;
        }

        if (e.Value is not bool flag) return;
        if (e.DesiredType != typeof(string)) return;

        e.Value = flag ? "Yes" : "No";
        e.FormattingApplied = true;
    }
}
