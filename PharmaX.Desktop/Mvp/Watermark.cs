namespace PharmaX.Desktop.Mvp;

/// <summary>
/// The pharmacy's logo, faint, across the middle of a screen - and in a
/// mode with a glow, a lit grid running to a horizon - on every screen big
/// enough to carry it.
/// </summary>
/// <remarks>
/// Drawn on a window of its own laid over the screen, see-through and
/// click-through, rather than under the screen's controls: on most screens
/// a list fills the window, and anything beneath it would never be seen.
/// </remarks>
internal sealed class Watermark : Form
{
    private const int TransparentToMouse = 0x20;
    private const int ToolWindow = 0x80;
    private const int Layered = 0x80000;
    private const int NoActivate = 0x08000000;

    private static Branding? _branding;

    private readonly Form _screen;

    /// The logo every watermark draws. Set at start-up; none until then.
    public static void Use(Branding branding) => _branding = branding;

    private Watermark(Form screen)
    {
        _screen = screen;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        Recolour();
    }

    /// <summary>
    /// Lays a watermark over the screen once it is shown, if there is a logo
    /// and the screen is bigger than a prompt.
    /// </summary>
    public static void Attach(Form screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        screen.Shown += (_, _) =>
        {
            if (_branding is not { HasLogo: true }
                || screen.ClientSize.Width < 480 || screen.ClientSize.Height < 300)
                return;

            var mark = new Watermark(screen);

            void Follow(object? sender, EventArgs e)
            {
                if (!mark.IsDisposed) mark.Follow();
            }

            screen.LocationChanged += Follow;
            screen.SizeChanged += Follow;

            // A dialog is hidden before it is disposed, and Windows hides an
            // owned window with a minimised owner but not a hidden one.
            screen.VisibleChanged += (_, _) =>
            {
                if (!mark.IsDisposed) mark.Visible = screen.Visible;
            };

            mark.Follow();
            mark.Show(screen);
        };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= TransparentToMouse | ToolWindow | Layered | NoActivate;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        PhxTheme.Changed += OnThemeChanged;
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        PhxTheme.Changed -= OnThemeChanged;
        base.OnHandleDestroyed(e);
    }

    private void OnThemeChanged(object? sender, IReadOnlyDictionary<Color, Color> e)
    {
        if (IsDisposed) return;

        if (InvokeRequired)
        {
            BeginInvoke(() => OnThemeChanged(sender, e));
            return;
        }

        Recolour();
        Invalidate();
    }

    /// <summary>
    /// The page's colour is the one let through, so the soft edges of the
    /// logo blend into the screen rather than a colour of their own; the
    /// rest shows at a strength that leaves the screen readable through it.
    /// </summary>
    private void Recolour()
    {
        BackColor = TransparencyKey = PhxTheme.Surface;
        Opacity = PhxTheme.Mode.Glow is not null ? 0.12
            : PhxTheme.Mode.IsDark ? 0.1 : 0.07;
    }

    /// Over the screen's client area, wherever it has moved to.
    private void Follow()
    {
        Bounds = _screen.RectangleToScreen(_screen.ClientRectangle);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var area = ClientRectangle;
        if (area.Width <= 0 || area.Height <= 0) return;

        var mode = PhxTheme.Mode;
        if (mode.Glow is Color glow) PaintHorizon(e.Graphics, area, glow);

        // The middle three fifths each way. On a dark page the logo is a
        // glow - its dark parts lit - since its own colours would sink.
        _branding?.DrawWatermark(e.Graphics,
            Rectangle.Inflate(area, -area.Width / 5, -area.Height / 5),
            mode.Glow ?? (mode.IsDark ? PhxTheme.Ink : null));
    }

    /// <summary>
    /// A floor of lines running to a horizon across the lower half, with a
    /// glow where they meet. Drawn strong: the window's opacity fades it.
    /// </summary>
    private static void PaintHorizon(Graphics graphics, Rectangle area, Color glow)
    {
        var horizon = area.Top + area.Height * 11 / 20;
        var centre = area.Left + area.Width / 2;

        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using (var halo = new System.Drawing.Drawing2D.GraphicsPath())
        {
            halo.AddEllipse(area.Left, horizon - area.Height / 3,
                            area.Width, area.Height * 2 / 3);

            using var light = new System.Drawing.Drawing2D.PathGradientBrush(halo)
            {
                CenterColor = Color.FromArgb(90, glow),
                SurroundColors = new[] { Color.FromArgb(0, glow) }
            };

            graphics.FillPath(light, halo);
        }

        using var line = new Pen(Color.FromArgb(150, glow));

        for (var i = -14; i <= 14; i++)
            graphics.DrawLine(line, centre + i * 10, horizon,
                              centre + i * area.Width / 7, area.Bottom);

        // Closer together towards the horizon, as a floor recedes.
        for (var step = 1; step <= 10; step++)
        {
            var y = horizon + (int)((area.Bottom - horizon) * Math.Pow(step / 10.0, 2));
            graphics.DrawLine(line, area.Left, y, area.Right, y);
        }

        using var edge = new Pen(glow, 2);
        graphics.DrawLine(edge, area.Left, horizon, area.Right, horizon);
    }
}
