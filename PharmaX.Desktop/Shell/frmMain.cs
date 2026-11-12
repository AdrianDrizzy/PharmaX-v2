using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Shell;

public partial class frmMain : PhxFormBase, IMainView
{
    private readonly MainPresenter? _presenter;
    private readonly Branding? _branding;

    // The status strip already carries the connectivity state, and the
    // shell is the one window that is never closed.
    protected override bool ShowsOfflineBanner => false;

    /// The menu carries the shell's keys.
    protected override bool AssignsFunctionKeys => false;

    /// Designer only. Never called at runtime.
    public frmMain()
    {
        InitializeComponent();
        WireTheme();
        WireView();
        WireMenu();
        WireMenuHover();
        WireHome();
        WireGoTo();
    }

    /// Runtime. Resolved by the container.
    public frmMain(MainPresenter presenter, Branding branding) : this()
    {
        _presenter = presenter;
        _branding = branding;
        _presenter.Attach(this);
        RefreshBranding();
    }

    /// <summary>
    /// The Theme menu at the right-hand end of the menu bar: light, dark
    /// and the other modes, the one in use ticked. A choice shows on every
    /// open window at once, and the presenter keeps it for this till.
    /// </summary>
    /// <remarks>
    /// The entries carry their mode rather than a string key in Tag, so
    /// the menu wiring, the permission check and Go to all pass them by:
    /// a theme is not a screen.
    /// </remarks>
    private void WireTheme()
    {
        // Opening leftwards, under the window rather than off its edge: the
        // entry sits at the far right, and a list opening to the right of it
        // landed past the window, on the next screen where there is one.
        var theme = new ToolStripMenuItem("&Theme")
        {
            Alignment = ToolStripItemAlignment.Right,
            DropDownDirection = ToolStripDropDownDirection.BelowLeft
        };

        foreach (var mode in ThemeMode.All)
        {
            var item = new ToolStripMenuItem(mode.Name) { Tag = mode };

            item.Click += (_, _) =>
            {
                if (mode == PhxTheme.Mode) return;

                PhxTheme.Use(PhxTheme.Palette, mode);
                ThemeChosen?.Invoke(this, mode.Id);
            };

            theme.DropDownItems.Add(item);
        }

        // The one in use in bold as well as ticked: beside a swatch the tick
        // is only a faint frame round the picture. Made again after a zoom,
        // at the menu's new size.
        var bold = new Font(mnuMain.Font, FontStyle.Bold);
        Disposed += (_, _) => bold.Dispose();

        theme.DropDownOpening += (_, _) =>
        {
            if (bold.Size != mnuMain.Font.Size)
            {
                bold.Dispose();
                bold = new Font(mnuMain.Font, FontStyle.Bold);
            }

            foreach (ToolStripMenuItem item in theme.DropDownItems)
            {
                item.Checked = Equals(item.Tag, PhxTheme.Mode);

                if (item.Checked) item.Font = bold;
                else item.ResetFont();
            }
        };

        mnuMain.Items.Add(theme);
    }

    /// The window sizes the View menu offers, by shape. Each is the
    /// window's whole size, frame and all, in the display's pixels.
    private static readonly (string Shape, int Across, int Down, Size[] Sizes)[] Shapes =
    {
        ("16:9", 16, 9, new Size[] { new(1920, 1080), new(1600, 900),
                                     new(1366, 768), new(1280, 720) }),
        ("16:10", 16, 10, new Size[] { new(1920, 1200), new(1680, 1050),
                                       new(1440, 900), new(1280, 800) }),
        ("4:3", 4, 3, new Size[] { new(1600, 1200), new(1280, 960),
                                   new(1024, 768) }),
        ("5:4", 5, 4, new Size[] { new(1280, 1024) })
    };

    /// <summary>
    /// The View menu: how large everything is drawn, and the shape and
    /// size of this window. Kept for this till, as the theme is.
    /// </summary>
    /// <remarks>
    /// Resolution here is the window's, not the display's: PharmaX does
    /// not change a setting that belongs to every program on the machine.
    /// A size larger than the screen is offered but greyed, so the list
    /// reads the same on every till. As with the theme, the entries carry
    /// no string key in Tag, so the menu wiring and Go to pass them by.
    /// </remarks>
    private void WireView()
    {
        var view = new ToolStripMenuItem("&View");

        // ---- Zoom
        var zoom = new ToolStripMenuItem("&Zoom");

        var zoomIn = new ToolStripMenuItem("Zoom &in")
        {
            ShortcutKeys = Keys.Control | Keys.Oemplus,
            ShortcutKeyDisplayString = "Ctrl++"
        };
        var zoomOut = new ToolStripMenuItem("Zoom &out")
        {
            ShortcutKeys = Keys.Control | Keys.OemMinus,
            ShortcutKeyDisplayString = "Ctrl+-"
        };
        var actual = new ToolStripMenuItem("&Actual size (100%)")
        {
            ShortcutKeys = Keys.Control | Keys.D0,
            ShortcutKeyDisplayString = "Ctrl+0"
        };

        zoomIn.Click += (_, _) => Zoom(PhxZoom.Step(+1));
        zoomOut.Click += (_, _) => Zoom(PhxZoom.Step(-1));
        actual.Click += (_, _) => Zoom(100);

        zoom.DropDownItems.AddRange(new ToolStripItem[]
        {
            zoomIn, zoomOut, actual, new ToolStripSeparator()
        });

        foreach (var percent in PhxZoom.Steps)
        {
            var step = new ToolStripMenuItem($"{percent}%") { Tag = percent };
            step.Click += (_, _) => Zoom(percent);
            zoom.DropDownItems.Add(step);
        }

        zoom.DropDownOpening += (_, _) =>
        {
            zoomIn.Enabled = PhxZoom.Percent < PhxZoom.Steps[^1];
            zoomOut.Enabled = PhxZoom.Percent > PhxZoom.Steps[0];

            foreach (var item in zoom.DropDownItems.OfType<ToolStripMenuItem>())
                if (item.Tag is int percent) item.Checked = percent == PhxZoom.Percent;
        };

        // ---- Aspect ratio
        var shape = new ToolStripMenuItem("&Aspect ratio");
        var fill = new ToolStripMenuItem("&Fill the screen");
        fill.Click += (_, _) => ChooseWindowSize(null);

        shape.DropDownItems.AddRange(new ToolStripItem[] { fill, new ToolStripSeparator() });

        foreach (var (name, across, down, _) in Shapes)
        {
            var item = new ToolStripMenuItem(name) { Tag = (across, down) };
            item.Click += (_, _) => ChooseWindowSize(Largest(across, down));
            shape.DropDownItems.Add(item);
        }

        shape.DropDownOpening += (_, _) =>
        {
            fill.Checked = WindowState == FormWindowState.Maximized;

            foreach (var item in shape.DropDownItems.OfType<ToolStripMenuItem>())
                if (item.Tag is ValueTuple<int, int> ratio)
                    item.Checked = WindowState == FormWindowState.Normal
                                   && IsShape(Size, ratio.Item1, ratio.Item2);
        };

        // ---- Resolution
        var resolution = new ToolStripMenuItem("&Resolution");
        var full = new ToolStripMenuItem("F&ull screen");
        full.Click += (_, _) => ChooseWindowSize(null);

        resolution.DropDownItems.Add(full);

        foreach (var (name, _, _, sizes) in Shapes)
        {
            resolution.DropDownItems.Add(new ToolStripSeparator());

            foreach (var size in sizes)
            {
                var item = new ToolStripMenuItem(
                    $"{size.Width} × {size.Height}   ({name})") { Tag = size };
                item.Click += (_, _) => ChooseWindowSize(size);
                resolution.DropDownItems.Add(item);
            }
        }

        resolution.DropDownOpening += (_, _) =>
        {
            var work = Screen.FromControl(this).WorkingArea.Size;
            full.Checked = WindowState == FormWindowState.Maximized;

            foreach (var item in resolution.DropDownItems.OfType<ToolStripMenuItem>())
            {
                if (item.Tag is not Size size) continue;

                item.Enabled = size.Width <= work.Width && size.Height <= work.Height;
                item.Checked = WindowState == FormWindowState.Normal && Size == size;
            }
        };

        view.DropDownItems.AddRange(new ToolStripItem[] { zoom, shape, resolution });

        // Before Help, where Windows programs keep it.
        var help = mnuMain.Items.OfType<ToolStripMenuItem>()
            .FirstOrDefault(i => i.Text == "&Help");
        mnuMain.Items.Insert(help is null ? mnuMain.Items.Count
                                          : mnuMain.Items.IndexOf(help), view);

        PhxZoom.Changed += OnZoomChanged;
        Disposed += (_, _) => PhxZoom.Changed -= OnZoomChanged;
    }

    private void Zoom(int percent)
    {
        if (percent == PhxZoom.Percent) return;

        PhxZoom.Change(percent);
        ZoomChosen?.Invoke(this, percent);
    }

    /// The icons are pictures at a size, and the logo's room is worked
    /// out from the header's height, so both are done again.
    private void OnZoomChanged(object? sender, EventArgs e)
    {
        ShowIcons();
        RefreshBranding();
        Invalidate(true);
    }

    /// <summary>
    /// Sizes the window and keeps it for this till - or, given nothing,
    /// fills the screen with it again.
    /// </summary>
    private void ChooseWindowSize(Size? size)
    {
        var value = size is Size chosen
            ? FormattableString.Invariant($"{chosen.Width}x{chosen.Height}")
            : "max";

        UseWindowSize(value);
        WindowSizeChosen?.Invoke(this, value);
    }

    public void UseWindowSize(string? size)
    {
        if (ParseSize(size) is not Size wanted)
        {
            WindowState = FormWindowState.Maximized;
            return;
        }

        // Never past the screen it is on; centred in what is left of it
        // once the taskbar has its share.
        var work = Screen.FromControl(this).WorkingArea;
        var fits = new Size(Math.Min(wanted.Width, work.Width),
                            Math.Min(wanted.Height, work.Height));

        WindowState = FormWindowState.Normal;
        Bounds = new Rectangle(work.Left + (work.Width - fits.Width) / 2,
                               work.Top + (work.Height - fits.Height) / 2,
                               fits.Width, fits.Height);
    }

    private static Size? ParseSize(string? value)
    {
        var parts = value?.Split('x');

        return parts is { Length: 2 }
               && int.TryParse(parts[0], System.Globalization.NumberStyles.None,
                               System.Globalization.CultureInfo.InvariantCulture, out var across)
               && int.TryParse(parts[1], System.Globalization.NumberStyles.None,
                               System.Globalization.CultureInfo.InvariantCulture, out var down)
               && across >= 640 && down >= 480
            ? new Size(across, down)
            : null;
    }

    /// The largest window of a shape that the screen it is on can hold.
    private Size Largest(int across, int down)
    {
        var work = Screen.FromControl(this).WorkingArea.Size;
        var width = work.Width;
        var height = width * down / across;

        if (height > work.Height)
        {
            height = work.Height;
            width = height * across / down;
        }

        return new Size(width, height);
    }

    /// Within a pixel or two of the shape, which the arithmetic leaves.
    private static bool IsShape(Size size, int across, int down) =>
        Math.Abs(size.Width * down - size.Height * across) <= across + down;

    /// <summary>
    /// Gives the logo the room it needs, or none at all.
    /// </summary>
    /// <remarks>
    /// Sized from the image so that a tall logo and a wide one both sit
    /// properly beside the pharmacy's name, and collapsed to nothing
    /// when there is no logo, rather than leaving a hole where one
    /// would have gone.
    /// </remarks>
    public void RefreshBranding()
    {
        if (_branding is null) return;

        pnlHomeLogo.Width = _branding.Logo is Image logo
            ? Math.Clamp(
                (int)((pnlHomeHeader.Height - pnlHomeHeader.Padding.Vertical)
                      * (float)logo.Width / logo.Height),
                72, 320) + pnlHomeLogo.Padding.Right
            : 0;

        pnlHomeLogo.Invalidate();
    }

    private void PaintHomeLogo(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var box = new Rectangle(0, 4,
            Math.Max(0, pnlHomeLogo.Width - pnlHomeLogo.Padding.Right),
            Math.Max(0, pnlHomeLogo.Height - 8));

        _branding?.Draw(e.Graphics, box);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ShowIcons();
        if (_presenter is not null) await _presenter.InitialiseAsync();
        tmrWaiting.Start();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Opened?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Opened;

    public event EventHandler? AssistantNoticeClicked;

    /// As the alerts: shown only while drafts are waiting.
    public void SetAssistantNotice(string notice)
    {
        lblAssistant.Text = notice;
        lblAssistant.Visible = !string.IsNullOrWhiteSpace(notice);
    }

    protected override void OnPaletteChanged() => ShowIcons();

    /// Each menu entry's icon, by the key it opens. The home tiles use the
    /// same, so a job looks the same wherever it is reached from.
    private static readonly Dictionary<string, PhxIcon> MenuIcons =
        new(StringComparer.Ordinal)
    {
        ["printer-setup"] = PhxIcon.Printer, ["till"] = PhxIcon.CashRegister,
        ["cash-up"] = PhxIcon.Coins, ["end-of-day"] = PhxIcon.CashBanknote,
        ["logoff"] = PhxIcon.Logout, ["change-password"] = PhxIcon.Key,
        ["exit"] = PhxIcon.DoorExit,
        ["dispensary"] = PhxIcon.Pill, ["dispensing"] = PhxIcon.Pill,
        ["patients"] = PhxIcon.Users, ["reprint-script"] = PhxIcon.Printer,
        ["script-maintenance"] = PhxIcon.Prescription,
        ["dispensary-quote"] = PhxIcon.FileDollar, ["labels"] = PhxIcon.Tags,
        ["tariffs"] = PhxIcon.ReportMoney, ["merge-patients"] = PhxIcon.GitMerge,
        ["schemes"] = PhxIcon.HeartHandshake, ["prescribers"] = PhxIcon.Stethoscope,
        ["dosage-codes"] = PhxIcon.Language, ["drug-returns"] = PhxIcon.ArrowBackUp,
        ["pos"] = PhxIcon.ShoppingCart, ["collect"] = PhxIcon.Receipt,
        ["returns"] = PhxIcon.ReceiptRefund, ["quotation"] = PhxIcon.FileDollar,
        ["inventory"] = PhxIcon.Package, ["alerts"] = PhxIcon.AlertTriangle,
        ["departments"] = PhxIcon.BuildingStore, ["categories"] = PhxIcon.Category,
        ["suppliers"] = PhxIcon.Truck, ["purchase-orders"] = PhxIcon.FileInvoice,
        ["goods-receipt"] = PhxIcon.TruckDelivery,
        ["manufacturers"] = PhxIcon.BuildingFactory, ["products"] = PhxIcon.Package,
        ["stock"] = PhxIcon.Stack, ["stock-adjust"] = PhxIcon.Adjustments,
        ["supplier-return"] = PhxIcon.ArrowBackUp, ["move-stock"] = PhxIcon.ArrowsExchange,
        ["prepack"] = PhxIcon.Package,
        ["transfers"] = PhxIcon.ArrowsExchange, ["breakages"] = PhxIcon.TrashX,
        ["expired-stock"] = PhxIcon.CalendarX, ["price-changes"] = PhxIcon.Tags, ["mass-price"] = PhxIcon.Percentage,
        ["accounts"] = PhxIcon.Calculator, ["trial-balance"] = PhxIcon.Scale,
        ["journal"] = PhxIcon.Notebook, ["accounts-receivable"] = PhxIcon.UserDollar,
        ["claims"] = PhxIcon.HeartHandshake, ["chart-of-accounts"] = PhxIcon.ListTree,
        ["day-end"] = PhxIcon.CalendarCheck, ["reports"] = PhxIcon.ChartBar,
        ["stocktake"] = PhxIcon.ClipboardCheck, ["stocktake-new"] = PhxIcon.ClipboardPlus,
        ["stocktake-capture"] = PhxIcon.Barcode,
        ["stocktake-variance"] = PhxIcon.ClipboardList, ["stocktake-post"] = PhxIcon.Send,
        ["admin"] = PhxIcon.Settings, ["vat-codes"] = PhxIcon.Percentage,
        ["users"] = PhxIcon.Users, ["role-permissions"] = PhxIcon.Lock,
        ["branches"] = PhxIcon.Building,
        ["transaction-types"] = PhxIcon.ListDetails, ["system-setup"] = PhxIcon.Palette,
        ["system-console"] = PhxIcon.Terminal, ["data-import"] = PhxIcon.DatabaseImport,
        ["backup"] = PhxIcon.DatabaseExport, ["to-follow"] = PhxIcon.Clock,
        ["control-variables"] = PhxIcon.AdjustmentsHorizontal,
        ["about"] = PhxIcon.InfoCircle
    };

    /// <summary>
    /// The menu entries and the status bar with their icons, drawn at the
    /// size the strips' own scaling asks for, in the palette in use.
    /// </summary>
    private void ShowIcons()
    {
        var size = mnuMain.ImageScalingSize.Height;

        foreach (var heading in mnuMain.Items.OfType<ToolStripMenuItem>())
        {
            heading.Image = MenuIcon(heading, size);

            foreach (var item in heading.DropDownItems.OfType<ToolStripMenuItem>())
                item.Image = MenuIcon(item, size);
        }

        var small = staMain.ImageScalingSize.Height;

        lblUser.Image = PhxIcons.Image(PhxIcon.User, small, PhxTheme.Muted);
        lblTill.Image = PhxIcons.Image(PhxIcon.CashRegister, small, PhxTheme.Muted);
        lblSession.Image = PhxIcons.Image(PhxIcon.Clock, small, PhxTheme.Muted);
        lblWaiting.Image = PhxIcons.Image(PhxIcon.Prescription, small, PhxTheme.Title);
        lblAlerts.Image = PhxIcons.Image(PhxIcon.AlertTriangle, small,
                                         PhxTheme.ProblemText);
        lblAssistant.Image = PhxIcons.Image(PhxIcon.FileInvoice, small, PhxTheme.Title);
    }

    private static Bitmap? MenuIcon(ToolStripMenuItem item, int size)
    {
        if (item.Tag is ThemeMode mode) return Swatch(mode, size);

        // File and Help open nothing themselves, so they have no key.
        var icon = item.Tag is string key && MenuIcons.TryGetValue(key, out var mapped)
            ? mapped
            : item.Text?.Replace("&", "", StringComparison.Ordinal) switch
            {
                "File" => PhxIcon.File,
                "Help" => PhxIcon.HelpCircle,
                "Theme" => PhxIcon.Palette,
                "View" => PhxIcon.Eye,
                _ => (PhxIcon?)null
            };

        return icon is PhxIcon chosen
            ? PhxIcons.Image(chosen, size, PhxTheme.Title)
            : null;
    }

    /// <summary>
    /// A theme in miniature: its page, a box on it with a line of its text,
    /// and its glow - or the pharmacy's highlight - along the foot. So the
    /// Theme menu shows what each choice looks like, not only its name.
    /// </summary>
    private static Bitmap Swatch(ThemeMode mode, int size)
    {
        var swatch = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(swatch);

        var step = Math.Max(2, size / 6);
        var whole = new Rectangle(0, 0, size - 1, size - 1);

        using (var paper = new SolidBrush(mode.Paper))
            graphics.FillRectangle(paper, whole);
        using (var field = new SolidBrush(mode.Field))
            graphics.FillRectangle(field, step, step, size - 2 * step, size - 3 * step);
        using (var ink = new SolidBrush(mode.Ink))
            graphics.FillRectangle(ink, 2 * step, 2 * step, size / 2, Math.Max(1, step / 2));
        using (var glow = new SolidBrush(mode.Glow ?? PhxTheme.Palette.Highlight))
            graphics.FillRectangle(glow, 0, size - step, size, step);

        // A light swatch on a light menu still needs an edge.
        using var edge = new Pen(Color.FromArgb(140, 128, 128, 128));
        graphics.DrawRectangle(edge, whole);

        return swatch;
    }

    /// <summary>
    /// How many scripts are waiting at the till, or nothing at all.
    /// </summary>
    /// <remarks>
    /// The label disappears when the queue empties rather than reading
    /// zero. A count that is nearly always zero is a count nobody looks
    /// at, and this one has to be noticed the one time it is not.
    /// </remarks>
    public void SetWaiting(string waiting)
    {
        lblWaiting.Text = waiting;
        lblWaiting.Visible = !string.IsNullOrWhiteSpace(waiting);
    }

    /// As above: shown only when there is something to show.
    public void SetAlerts(string alerts)
    {
        lblAlerts.Text = alerts;
        lblAlerts.Visible = !string.IsNullOrWhiteSpace(alerts);
    }

    /// <summary>
    /// Every menu item carries its key in Tag, so one handler serves the
    /// whole menu and the presenter never learns what a ToolStripItem is.
    /// </summary>
    /// <remarks>
    /// Headings are wired up as commands by everything except the check
    /// below, and they must not be. A heading's Tag names a permission
    /// group, and ApplyAccess enables it while anything under it is; it is
    /// not something the presenter can be asked to open. Treating it as one meant that clicking Dispensary,
    /// Inventory, Stocktake or Administration - just to read the menu -
    /// raised a command the presenter had no case for and answered with
    /// "That part of PharmaX is not built yet", and that clicking Point
    /// of Sale or Reports, whose group keys happen to match a real
    /// command, opened that screen over the menu the operator was
    /// reading.
    /// </remarks>
    private void WireMenu()
    {
        foreach (var item in AllMenuItems(mnuMain.Items))
        {
            if (item.Tag is not string key) continue;
            if (item is ToolStripMenuItem { HasDropDownItems: true }) continue;

            item.Click += (_, _) => MenuSelected?.Invoke(this, key);
        }
    }

    /// <summary>
    /// Opens a top-level menu on hover, whether or not the menu bar is
    /// already active.
    /// </summary>
    /// <remarks>
    /// A MenuStrip only tracks the mouse once something has been
    /// clicked: until then hovering Dispensary highlights it but shows
    /// nothing, so the operator has to click, and a second click on the
    /// same heading closes it again. Staff read that as the menu not
    /// working. Showing the drop-down on MouseEnter makes the first
    /// heading behave like every one after it.
    /// </remarks>
    private void WireMenuHover()
    {
        foreach (var top in mnuMain.Items.OfType<ToolStripMenuItem>())
        {
            if (top.DropDownItems.Count == 0) continue;

            top.MouseEnter += (sender, _) =>
            {
                var hovered = (ToolStripMenuItem)sender!;

                // A disabled heading has nothing to show, and an already
                // open one must not be reopened: ShowDropDown on the
                // open item would close it on the way past.
                if (!hovered.Enabled || hovered.DropDown.Visible) return;

                // The menu bar has to be in menu mode before a drop-down
                // opened from code will stay up. Without this the strip
                // shows it and dismisses it in the same breath, which
                // looks exactly like the click-to-open problem this is
                // here to fix. Focus is what puts it in that mode, and
                // the shell has nothing else that wants the keyboard.
                if (!mnuMain.ContainsFocus) mnuMain.Focus();

                CloseOtherMenus(hovered);
                hovered.Select();
                hovered.ShowDropDown();
            };
        }
    }

    /// <summary>
    /// Leaves at most one drop-down open, so sweeping the mouse along the
    /// bar does not leave a trail of them behind.
    /// </summary>
    private void CloseOtherMenus(ToolStripMenuItem keep)
    {
        foreach (var other in mnuMain.Items.OfType<ToolStripMenuItem>())
        {
            if (!ReferenceEquals(other, keep) && other.DropDown.Visible)
                other.HideDropDown();
        }
    }

    /// <summary>
    /// A card on the home screen is the menu entry in another shape, so
    /// it raises the same event with the same key and the presenter
    /// cannot tell which one the operator used.
    /// </summary>
    private void WireHome()
    {
        foreach (var tile in flpTiles.Controls.OfType<HomeTile>())
            tile.Chosen += (sender, _) =>
                MenuSelected?.Invoke(this, ((HomeTile)sender!).MenuKey);

        // The figures in the status bar open what they count.
        lblWaiting.Click += (_, _) => MenuSelected?.Invoke(this, "collect");
        lblAlerts.Click += (_, _) => MenuSelected?.Invoke(this, "alerts");
        lblWaiting.ToolTipText = "Click to open Scripts to Collect";
        lblAlerts.ToolTipText = "Click to open Stock Alerts";
        lblAssistant.Click += (_, _) => AssistantNoticeClicked?.Invoke(this, EventArgs.Empty);
        lblAssistant.ToolTipText = "The stock assistant drafted orders for stock running out. Click to see them";
    }

    public void SetTileCount(string menuKey, string? count)
    {
        foreach (var tile in flpTiles.Controls.OfType<HomeTile>()
                     .Where(t => t.MenuKey == menuKey))
            tile.ShowCount(count);
    }

    public void SetTileNote(string menuKey, string? note)
    {
        foreach (var tile in flpTiles.Controls.OfType<HomeTile>()
                     .Where(t => t.MenuKey == menuKey))
            tile.ShowNote(note);
    }

    /// <summary>
    /// Other words for a screen than its menu caption: what the staff call
    /// the job rather than what the menu calls the screen.
    /// </summary>
    private static readonly Dictionary<string, string> GoToWords =
        new(StringComparer.Ordinal)
    {
        ["pos"] = "till sale checkout counter shop",
        ["collect"] = "collection queue script pay waiting",
        ["returns"] = "refund credit note give back",
        ["dispensing"] = "dispense script label medicine",
        ["patients"] = "patient customer file allergy",
        ["till"] = "cash up float x-read z-read session declare drawer",
        ["cash-up"] = "cash up cashup shift takings tender slip my sales count",
        ["end-of-day"] = "end of day cash up all tills cashiers daily takings variance",
        ["goods-receipt"] = "receive delivery grn invoice book in",
        ["purchase-orders"] = "order po buy",
        ["products"] = "product item price catalogue",
        ["stock"] = "on hand quantity report",
        ["alerts"] = "low reorder expiring short",
        ["stock-adjust"] = "adjust correct breakage expired transfer",
        ["breakages"] = "broken damaged write off report",
        ["transfers"] = "transfer report moved",
        ["price-changes"] = "price report",
        ["expired-stock"] = "expiry expiring report",
        ["reports"] = "report daily sales vat",
        ["trial-balance"] = "books ledger",
        ["accounts-receivable"] = "debtor account statement",
        ["claims"] = "medical aid claim",
        ["day-end"] = "end of day",
        ["stocktake-new"] = "count stock take",
        ["stocktake-capture"] = "count stock take",
        ["stocktake-variance"] = "count stock take difference",
        ["stocktake-post"] = "count stock take",
        ["users"] = "staff user role",
        ["role-permissions"] = "permission grant role tick",
        ["system-setup"] = "logo colour color palette brand theme",
        ["printer-setup"] = "printer receipt label",
        ["receipts"] = "reprint receipt copy history slip",
        ["receipt-template"] = "receipt template layout slip footer",
        ["logoff"] = "sign out log out shift",
        ["schemes"] = "medical aid",
        ["to-follow"] = "owed owing balance",
        ["data-import"] = "import load csv excel spreadsheet change-over"
    };

    private SearchSuggestions? _goTo;

    /// <summary>
    /// The box above the tiles that opens any screen by name. Only what
    /// this user may open is offered, because only an enabled menu entry
    /// is; Ctrl+K reaches it from anywhere, and typing on the home screen
    /// starts in it.
    /// </summary>
    private void WireGoTo()
    {
        _goTo = new SearchSuggestions(txtGoTo,
            (term, _) => Task.FromResult(Screens(term)))
        {
            MinimumLength = 1,
            Maximum = 8
        };

        _goTo.Chosen += (_, chosen) => Open((string)chosen.Value);

        txtGoTo.KeyDown += (_, e) =>
        {
            if (e.Handled) return;

            // Enter with nothing highlighted takes the best match, so a
            // word and Enter is enough.
            if (e.KeyCode == Keys.Enter
                && Screens(txtGoTo.Text).FirstOrDefault() is { } best)
            {
                Open((string)best.Value);
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                txtGoTo.Clear();
                e.SuppressKeyPress = true;
            }
        };
    }

    private void Open(string key)
    {
        txtGoTo.Clear();
        MenuSelected?.Invoke(this, key);
    }

    /// <summary>
    /// The menu entries this user may open whose caption, heading or other
    /// words hold every word typed. Those whose caption starts with what
    /// was typed come first.
    /// </summary>
    private IReadOnlyList<Suggestion> Screens(string term)
    {
        var words = term.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return Array.Empty<Suggestion>();

        var screens = new List<(Suggestion Item, bool Starts)>();

        foreach (var heading in mnuMain.Items.OfType<ToolStripMenuItem>())
        {
            foreach (var item in heading.DropDownItems.OfType<ToolStripMenuItem>())
            {
                if (!item.Enabled || item.Tag is not string key) continue;

                var caption = Plain(item.Text);
                var haystack = $"{caption} {Plain(heading.Text)} "
                               + GoToWords.GetValueOrDefault(key, "");

                if (!words.All(w => haystack.Contains(w,
                        StringComparison.CurrentCultureIgnoreCase)))
                    continue;

                var keys = item.ShortcutKeys == Keys.None
                    ? ""
                    : "   " + new KeysConverter().ConvertToString(item.ShortcutKeys);

                screens.Add((new Suggestion(key, caption,
                        Plain(heading.Text) + keys),
                    caption.StartsWith(words[0],
                        StringComparison.CurrentCultureIgnoreCase)));
            }
        }

        return screens
            .OrderBy(s => s.Starts ? 0 : 1)
            .Select(s => s.Item)
            .DistinctBy(s => s.Value)
            .ToList();
    }

    private static string Plain(string? text) =>
        (text ?? "").Replace("&", "", StringComparison.Ordinal);

    /// <summary>
    /// Ctrl+K to the Go to box from anywhere on the shell.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.K))
        {
            txtGoTo.Focus();
            txtGoTo.SelectAll();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>
    /// A letter typed on the home screen, with a tile rather than a box
    /// under the keyboard, starts a Go to search with it.
    /// </summary>
    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (!e.Handled && char.IsLetterOrDigit(e.KeyChar)
            && ActiveControl is not TextBoxBase && !mnuMain.ContainsFocus)
        {
            txtGoTo.Focus();
            txtGoTo.Text = e.KeyChar.ToString();
            txtGoTo.SelectionStart = txtGoTo.TextLength;
            e.Handled = true;
            return;
        }

        base.OnKeyPress(e);
    }

    private static IEnumerable<ToolStripItem> AllMenuItems(
        ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            yield return item;

            if (item is ToolStripMenuItem menu)
            {
                foreach (var child in AllMenuItems(menu.DropDownItems))
                    yield return child;
            }
        }
    }

    private string _who = "";
    private string _where = "";
    private string _session = "";

    public void SetUser(string username, string roles)
    {
        lblUser.Text = string.IsNullOrWhiteSpace(roles)
            ? username
            : $"{username}  ({roles})";

        _who = string.IsNullOrWhiteSpace(roles)
            ? username
            : $"{username}, {roles}";

        ShowContext();
    }

    public void SetTill(string branchAndTill)
    {
        lblTill.Text = branchAndTill;
        _where = branchAndTill;
        ShowContext();
    }

    public void SetSession(string sessionState)
    {
        lblSession.Text = sessionState;
        _session = sessionState;
        ShowContext();
    }

    public void SetPharmacy(string name, string particulars)
    {
        lblHomeTitle.Text = name;
        lblHomeParticulars.Text = particulars;
        lblHomeParticulars.Visible = !string.IsNullOrWhiteSpace(particulars);
    }

    /// The same three facts as the status bar, said once in a size a
    /// person reads from a metre away rather than from the keyboard.
    private void ShowContext() =>
        lblHomeContext.Text = string.Join("   -   ",
            new[] { _where, _who, _session, Format.Today() }
                .Where(part => !string.IsNullOrWhiteSpace(part)));

    public void SetConnectivity(string state)
    {
        if (!IsHandleCreated) { lblConnectivity.Text = state; return; }

        // The monitor raises on a timer thread, so this can arrive from
        // anywhere. One marshal here rather than in the presenter, which
        // has no business knowing that Windows Forms has a UI thread.
        BeginInvoke(() => lblConnectivity.Text = state);
    }

    public void ApplyAccess(Func<string, bool> canOpen)
    {
        ArgumentNullException.ThrowIfNull(canOpen);

        // The entries first, one by one: disabling an item is what turns
        // its shortcut key off too, which disabling only its heading never
        // did - F4 opened Patients under a greyed-out Dispensary.
        foreach (var item in AllMenuItems(mnuMain.Items))
        {
            if (item.Tag is not string key) continue;
            if (item is ToolStripMenuItem { HasDropDownItems: true }) continue;

            item.Enabled = canOpen(key);
        }

        // Then each heading, open while anything under it is.
        foreach (var heading in mnuMain.Items.OfType<ToolStripMenuItem>()
                     .Where(m => m.HasDropDownItems))
            heading.Enabled = heading.DropDownItems
                .OfType<ToolStripMenuItem>()
                .Any(child => child.Enabled);

        foreach (var tile in flpTiles.Controls.OfType<HomeTile>())
            tile.Enabled = canOpen(tile.MenuKey);
    }

    /// <summary>
    /// Read by Program.cs once the message loop ends: true means show
    /// the login again, false means the application is finished.
    /// </summary>
    public bool LogOffRequested { get; private set; }

    public void LogOff()
    {
        LogOffRequested = true;
        Close();
    }

    public void MarkAsTestEnvironment()
    {
        Text = "PharmaX  -  TEST DATA (seeded accounts)";
        staMain.BackColor = Color.FromArgb(255, 244, 214);

        // Black on the yellow in every theme, not a dark mode's light ink -
        // and the waiting count and the alerts in the light mode's navy and
        // red, which a dark mode's brighter shades would wash out on yellow.
        foreach (var label in new[] { lblUser, lblTill, lblSession, lblConnectivity })
            label.ForeColor = Color.Black;

        lblWaiting.ForeColor = Color.FromArgb(33, 56, 134);
        lblAlerts.ForeColor = Color.FromArgb(168, 32, 32);
        lblAssistant.LinkColor = Color.FromArgb(33, 56, 134);

        // A warning, so it stays this yellow whatever the theme does - the
        // light mode's attention colour, which a change of mode would swap.
        PhxTheme.Exclude(staMain);
    }

    public event EventHandler<string>? MenuSelected;
    public event EventHandler<string>? ThemeChosen;
    public event EventHandler<int>? ZoomChosen;
    public event EventHandler<string>? WindowSizeChosen;
    public event EventHandler? WaitingTick;
}
