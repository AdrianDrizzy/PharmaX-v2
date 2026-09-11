using PharmaX.Application.Offline;

namespace PharmaX.Desktop.Mvp;

/// <summary>
/// Every form inherits this, which is how the offline banner, the
/// function-key routing and the permission gate arrive for free on all
/// forty screens.
/// </summary>
public class PhxFormBase : Form, IView
{
    private Label? _offlineBanner;


    protected IConnectivityMonitor? Connectivity { get; private set; }

    /// Override in a derived form to require a permission to open it.
    protected virtual string? RequiredPermission => null;

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

    public void UseConnectivity(IConnectivityMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        Connectivity = monitor;
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

        FitWindowToContents();
    }

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

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void RenderBanner(ConnectivityState state)
    {
        if (!ShowsOfflineBanner) return;

        _offlineBanner ??= CreateBanner();

        // Informative, not alarming: offline is a normal working state.
        (_offlineBanner.Text, _offlineBanner.BackColor, _offlineBanner.Visible)
            = state switch
        {
            ConnectivityState.Connected =>
                ("", Color.Transparent, false),
            ConnectivityState.Degraded =>
                ("Host slow to respond - working locally",
                 PhxTheme.Attention, true),
            _ => ("Working locally - transactions will sync when the " +
                  "host returns", PhxTheme.Information, true)
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
        MessageBox.Show(this, message, "PharmaX",
            MessageBoxButtons.OK, MessageBoxIcon.Warning);

    public void ShowInfo(string message) =>
        MessageBox.Show(this, message, "PharmaX",
            MessageBoxButtons.OK, MessageBoxIcon.Information);

    public bool Confirm(string question) =>
        MessageBox.Show(this, question, "PharmaX",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    public void SetBusy(bool busy) =>
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;

    public void CloseView(bool success)
    {
        DialogResult = success ? DialogResult.OK : DialogResult.Cancel;
        Close();
    }
}
