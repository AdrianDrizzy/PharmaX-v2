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
        WireMenu();
        WireMenuHover();
        WireHome();
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
                (int)(pnlHomeHeader.Height * (float)logo.Width / logo.Height),
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
        if (_presenter is not null) await _presenter.InitialiseAsync();
        tmrWaiting.Start();
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
    }

    public event EventHandler<string>? MenuSelected;
    public event EventHandler? WaitingTick;
}
