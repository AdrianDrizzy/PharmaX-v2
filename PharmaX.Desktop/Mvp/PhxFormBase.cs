using PharmaX.Application.Offline;
using PharmaX.Desktop.Controls;

namespace PharmaX.Desktop.Mvp;

/// <summary>
/// Every form inherits this, which is how the offline banner, the
/// function-key routing and the permission gate arrive for free on all
/// forty screens.
/// </summary>
public class PhxFormBase : Form, IView
{
    private Label? _offlineBanner;
    private bool _sharedDatabase;


    protected IConnectivityMonitor? Connectivity { get; private set; }

    /// The pharmacy's capsule in the title bar and on the taskbar, rather
    /// than the Windows Forms default every window used to carry.
    public PhxFormBase() => Icon = PhxIcons.AppIcon;

    /// <summary>
    /// Follows a change of palette while open: the colours the screen was
    /// built with are swapped for the new ones, and the icons redrawn.
    /// </summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        PhxTheme.Changed += OnPaletteChange;
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        PhxTheme.Changed -= OnPaletteChange;
        base.OnHandleDestroyed(e);
    }

    private void OnPaletteChange(object? sender,
                                 IReadOnlyDictionary<Color, Color> changes)
    {
        if (IsDisposed) return;

        if (InvokeRequired)
        {
            BeginInvoke(() => OnPaletteChange(sender, changes));
            return;
        }

        PhxTheme.Recolour(this, changes);
        PhxTheme.TitleBar(this);
        PhxIcons.Decorate(this);
        OnPaletteChanged();
        Invalidate(true);
    }

    /// Where a screen draws icons of its own - the shell's menus and tiles
    /// - it draws them again here, in the new palette.
    protected virtual void OnPaletteChanged() { }

    // The title as the screen asked for it, before branding. Kept so the
    // window can be re-titled either way when the switch is changed.
    private string _requestedTitle = "";

    /// <summary>
    /// The window title, with "PharmaX" replaced by the trading name
    /// when the pharmacy has chosen that at the System Console.
    /// </summary>
    /// <remarks>
    /// Setting still takes "PharmaX - Sign in" exactly as every designer
    /// writes it; only what the window shows changes. Reading returns
    /// what is shown, which is what the operator sees.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => base.Text;
        set
        {
            _requestedTitle = value ?? "";
            base.Text = WindowTitles.Brand(_requestedTitle);
        }
    }

    /// <summary>
    /// Re-applies the pharmacy's branding to this window after the
    /// details have changed.
    /// </summary>
    internal void RefreshBrand()
    {
        base.Text = WindowTitles.Brand(_requestedTitle);
        OnBrandChanged();
    }

    /// Where a screen shows the product's name somewhere other than its
    /// title - the sign-in heading - it re-reads it here.
    protected virtual void OnBrandChanged() { }

    /// Override in a derived form to require a permission to open it.
    protected virtual string? RequiredPermission => null;

    /// <summary>
    /// The permission this screen needs, read by ViewFactory before the
    /// window is shown.
    /// </summary>
    /// <remarks>
    /// Screens declared RequiredPermission from the start and nothing ever
    /// read it, so any form could be opened by anyone who could reach its
    /// menu key - a shortcut under a disabled heading, a home tile - and
    /// only the service behind it said no, after the operator had filled
    /// the screen in (DEF-0027).
    /// </remarks>
    internal string? PermissionToOpen => RequiredPermission;

    /// <summary>
    /// Whether this user may open the screen at all. The declared
    /// permission by default - one a supervisor could authorise still
    /// opens, because the screen is where the authorisation is asked for -
    /// and overridden where a screen serves more than one kind of user.
    /// </summary>
    protected virtual bool MayOpen(
        Application.Security.IPermissionService permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        return RequiredPermission is not string code
               || permissions.Check(code)
                  != Application.Security.PermissionOutcome.Denied;
    }

    internal bool MayOpenWith(
        Application.Security.IPermissionService permissions) =>
        MayOpen(permissions);

    private OperationQueue? _operations;

    /// <summary>
    /// This screen's operations, one at a time. See OperationQueue.
    /// </summary>
    protected OperationQueue Operations => _operations ??= new OperationQueue(
        ex =>
        {
            ErrorHandler.Log(ex);
            if (!IsDisposed) ShowError(ErrorHandler.Describe(ex));
        });

    /// <summary>
    /// The shell reports connectivity in its status strip, so it turns
    /// the banner off rather than saying the same thing twice on the
    /// one window that is always open.
    /// </summary>
    protected virtual bool ShowsOfflineBanner => true;

    /// <summary>
    /// Escape backs out of a screen. Overridden to false where the key
    /// already means something else - at the till it voids a line.
    /// </summary>
    protected virtual bool ClosesOnEscape => true;

    /// <summary>
    /// Whether the buttons are given function keys (see FunctionKeys).
    /// Off where a screen keeps a map of its own that people already know
    /// - the till's - and on the shell, whose menu carries the keys.
    /// </summary>
    protected virtual bool AssignsFunctionKeys => true;

    private Dictionary<Keys, Button>? _functionKeys;

    /// <summary>
    /// The keys and the buttons they press. Handed out when the screen
    /// opens; a test can hand them out without opening it.
    /// </summary>
    internal IReadOnlyDictionary<Keys, Button> FunctionKeyMap
    {
        get
        {
            AssignFunctionKeys();
            return _functionKeys ?? new Dictionary<Keys, Button>();
        }
    }

    internal void AssignFunctionKeys()
    {
        if (_functionKeys is not null || !AssignsFunctionKeys) return;

        _functionKeys = FunctionKeys.Assign(this, escape: ClosesOnEscape);
    }

    public void UseConnectivity(IConnectivityMonitor monitor,
                                bool sharedDatabase = false)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        Connectivity = monitor;
        _sharedDatabase = sharedDatabase;
        monitor.StateChanged += OnConnectivityChanged;

        // The handler outlives the form otherwise: the monitor is a
        // singleton and every screen ever opened would keep receiving.
        Disposed += (_, _) => monitor.StateChanged -= OnConnectivityChanged;
    }

    private void OnConnectivityChanged(object? sender, ConnectivityState state)
    {
        if (IsHandleCreated && !IsDisposed)
            BeginInvoke(() => RenderBanner(state));
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // Applied here rather than in each designer: forty screens each
        // choosing their own font is how a product ends up looking like
        // forty products.
        PhxTheme.Apply(this);

        if (Connectivity is not null) RenderBanner(Connectivity.State);
        KeyPreview = true;

        // After the theme, which sets the buttons' font, and before the
        // window is fitted: the keys make some captions longer.
        AssignFunctionKeys();

        // After the keys, which are part of the caption the icon must fit
        // beside.
        PhxIcons.Decorate(this);

        FitWindowToContents();

        if (ShowsWatermark) Watermark.Attach(this);
    }

    /// Whether the logo is laid faintly across this screen. Off where the
    /// screen shows the logo itself.
    protected virtual bool ShowsWatermark => true;

    /// <summary>
    /// Grows the window if the contents have ended up bigger than it is.
    /// </summary>
    /// <remarks>
    /// The designers are hand-written and position controls at absolute
    /// coordinates chosen at one scale, while the window is drawn at
    /// whatever the display is set to - a pharmacy PC at 125 or 150 per
    /// cent is ordinary. The controls scale; the ClientSize the designer
    /// wrote does not, so the contents outgrow the frame and the bottom
    /// and right of the screen are simply cut off. The sign-in window
    /// lost its logo, both boxes and both buttons that way.
    ///
    /// Measuring the children and growing to fit is the one fix that
    /// holds for every screen and every display, and it only ever grows:
    /// a window that was already big enough is left exactly as its
    /// designer laid it out. Runs last, once the theme font and the
    /// offline banner are both in place, because both change what the
    /// contents measure.
    /// </remarks>
    private void FitWindowToContents()
    {
        // The shell is maximized and owns the screen; a dialog is what
        // this is for.
        if (WindowState != FormWindowState.Normal) return;

        // Several passes: the layout has to be settled before the
        // children can be measured, and giving the window more room
        // re-flows anchored and auto-sizing children, which can ask for
        // a little more again. It converges in two; three is slack.
        for (var pass = 0; pass < 3; pass++)
        {
            PerformLayout();

            var needed = Size.Empty;

            foreach (Control child in Controls)
            {
                // A filled child takes the size it is given, so it can
                // never be the reason the window is too small.
                if (!child.Visible || child.Dock == DockStyle.Fill) continue;

                needed.Width = Math.Max(needed.Width,
                    child.Right + child.Margin.Right);
                needed.Height = Math.Max(needed.Height,
                    child.Bottom + child.Margin.Bottom);
            }

            if (needed.Width <= ClientSize.Width
                && needed.Height <= ClientSize.Height) return;

            ClientSize = new Size(
                Math.Max(ClientSize.Width, needed.Width + Padding.Right),
                Math.Max(ClientSize.Height, needed.Height + Padding.Bottom));
        }
    }

    /// <remarks>
    /// Only on a dialog, and only where the screen has not already named
    /// its own cancel button: the shell is not a dialog and Escape there
    /// would shut the application.
    /// </remarks>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && Modal && ClosesOnEscape
            && CancelButton is null)
        {
            CloseView(false);
            return true;
        }

        if (_functionKeys is not null && PressFunctionKey(keyData))
            return true;

        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>
    /// F1 lists the screen's keys; any other key presses its button,
    /// where the button can be pressed at all.
    /// </summary>
    /// <remarks>
    /// A hidden or disabled button stays unpressed from the keyboard as
    /// it is from the mouse: a key must never do what the screen has just
    /// decided this user may not. Escape is left to the form's own cancel
    /// button, and F2 and F4 to a grid or a list that has the focus.
    /// </remarks>
    internal bool PressFunctionKey(Keys key)
    {
        if (_functionKeys is null) return false;

        if (key == Keys.F1)
        {
            frmTextViewer.Show(this, "Keyboard shortcuts",
                FunctionKeys.Describe(_functionKeys));
            return true;
        }

        if (key == Keys.Escape
            || !_functionKeys.TryGetValue(key, out var button)
            || FunctionKeys.BelongsToControl(Focused(), key)
            || !button.Visible || !button.Enabled)
            return false;

        button.PerformClick();
        return true;
    }

    /// The control that actually has the focus, inside any container.
    private Control? Focused()
    {
        Control? control = ActiveControl;

        while (control is ContainerControl container
               && container.ActiveControl is not null)
            control = container.ActiveControl;

        return control;
    }

    private void RenderBanner(ConnectivityState state)
    {
        if (!ShowsOfflineBanner) return;

        _offlineBanner ??= CreateBanner();

        // The wording comes from the monitor because it depends on how
        // the site is set up: offline is a normal working state on a
        // standalone till and a stoppage on a shared database. The
        // colour follows the same logic - informative where work
        // continues, a problem where it cannot.
        var text = Connectivity?.DescribeState(state) ?? "";
        var stopped = _sharedDatabase && state != ConnectivityState.Degraded;

        (_offlineBanner.Text, _offlineBanner.BackColor, _offlineBanner.Visible)
            = state switch
        {
            ConnectivityState.Connected => ("", Color.Transparent, false),
            ConnectivityState.Degraded => (text, PhxTheme.Attention, true),
            _ => (text, stopped ? PhxTheme.Problem : PhxTheme.Information,
                  true)
        };
    }

    /// <summary>
    /// Docked across the very top of the window, above everything the
    /// designer laid out.
    /// </summary>
    /// <remarks>
    /// Child index matters here and is easy to get backwards. Docking is
    /// resolved from the highest child index down, so the control added
    /// last claims the outermost edge. Forcing the banner to index 0 -
    /// the obvious "put it first" - docks it last instead, which leaves
    /// it sharing whatever the Fill panel already took and painting over
    /// that panel's first row. Appending is what puts it across the top.
    /// </remarks>
    private Label CreateBanner()
    {
        var label = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Top,
            Font = PhxTheme.Emphasis,
            Padding = new Padding(0, 4, 0, 4),
            TextAlign = ContentAlignment.MiddleCenter,
            Visible = false
        };

        // Height measured from the font, not the fixed 28 pixels this
        // used to carry. That number was chosen against a 96-DPI display
        // and on a pharmacy PC at 125 or 150 per cent the banner's own
        // text no longer fitted inside it - the tops and tails of the
        // letters were sliced off on the sign-in window, which is the
        // first thing anyone sees. Width is left to the dock so the band
        // still runs the full way across.
        label.Height = label.PreferredHeight + label.Padding.Vertical;

        Controls.Add(label);
        return label;
    }

    // ---- IView -----------------------------------------------------------

    public void ShowError(string message) =>
        frmMessage.Show(this, message,
            MessageBoxButtons.OK, MessageBoxIcon.Warning);

    public void ShowInfo(string message) =>
        frmMessage.Show(this, message,
            MessageBoxButtons.OK, MessageBoxIcon.Information);

    public bool Confirm(string question) =>
        frmMessage.Show(this, question,
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    public void ShowSaved(string path) => GridExport.Saved(this, path);

    public void SetBusy(bool busy) =>
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;

    public void CloseView(bool success)
    {
        DialogResult = success ? DialogResult.OK : DialogResult.Cancel;
        Close();
    }
}
