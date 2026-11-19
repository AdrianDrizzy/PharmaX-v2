using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Shell;

/// <summary>
/// One card on the shell's home screen: the name of a job, the key that
/// does it, and a click for the people who reach for the mouse.
/// </summary>
/// <remarks>
/// The shell opened on an empty rectangle before this. An empty
/// rectangle tells a new cashier nothing about what the machine in
/// front of them is for, and an experienced one still has the menu and
/// the function keys, so nothing is taken away by putting the same six
/// jobs where they can be seen.
/// </remarks>
internal sealed class HomeTile : Panel
{
    private readonly Label _title = new();
    private readonly Label _hint = new();

    /// When the job was last done, under the shortcut, where it says.
    private readonly Label _note = new();
    private readonly string _shortcut;
    private readonly string _caption;
    private readonly PhxIcon _icon;
    private bool _hot;

    /// The figure in the top right-hand corner, or null for none.
    private string? _count;

    private static readonly Font CountFont =
        new("Segoe UI", 8.25f, FontStyle.Bold);

    private static readonly Font NoteFont = new("Segoe UI", 8.25f);

    internal HomeTile(string title, string hint, string menuKey,
                      string permissionGroup, PhxIcon icon)
    {
        MenuKey = menuKey;
        PermissionGroup = permissionGroup;
        _shortcut = hint;
        _caption = title;
        _icon = icon;

        BackColor = PhxTheme.Card;
        Cursor = Cursors.Hand;
        Margin = new Padding(0, 0, 14, 14);
        Size = new Size(212, 132);

        // Named after its own caption. A panel has no name of its own, so
        // assistive technology borrowed the text before it in the window
        // and every tile was announced as the one before - Collect as
        // "Sell", Dispense as "Collect" (ADM-OBS-19). A button, because
        // that is what it does, and one the keyboard can reach and press.
        AccessibleName = title;
        AccessibleDescription = hint;
        AccessibleRole = AccessibleRole.PushButton;
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;

        _title.AutoSize = false;
        _title.BackColor = Color.Transparent;
        _title.Font = PhxTheme.Subheading;
        _title.ForeColor = PhxTheme.Title;

        // Under the row that OnPaint gives the icon and the figure, so a
        // long figure - "7,047 · 8 new today" - never lands on the title.
        _title.AutoEllipsis = true;
        _title.Location = new Point(16, 54);
        _title.Size = new Size(184, 26);
        _title.Text = title;

        _hint.AutoSize = false;
        _hint.BackColor = Color.Transparent;
        _hint.Font = PhxTheme.Body;
        _hint.ForeColor = PhxTheme.Muted;
        _hint.Location = new Point(16, 82);
        _hint.Size = new Size(184, 22);
        _hint.Text = hint;

        _note.AutoEllipsis = true;
        _note.AutoSize = false;
        _note.BackColor = Color.Transparent;
        _note.Font = NoteFont;
        _note.ForeColor = PhxTheme.Muted;
        _note.Location = new Point(16, 104);
        _note.Size = new Size(184, 20);

        Controls.Add(_note);
        Controls.Add(_hint);
        Controls.Add(_title);

        // The labels cover most of the card, so they have to answer to
        // the mouse as though they were part of it.
        foreach (var control in new Control[] { this, _title, _hint, _note })
        {
            control.Click += (_, _) => Choose();
            control.MouseEnter += (_, _) => Hot(true);
            control.MouseLeave += (_, _) => Hot(false);
        }
    }

    internal string MenuKey { get; }

    /// The key the presenter uses when it enables or hides a menu, so a
    /// card disappears with the permission that governs it.
    internal string PermissionGroup { get; }

    internal event EventHandler? Chosen;

    /// <summary>
    /// Puts a figure in the card's top right-hand corner - how many
    /// patients are on file, how many scripts are waiting - or takes it
    /// away. Read out with the card's name.
    /// </summary>
    internal void ShowCount(string? count)
    {
        _count = string.IsNullOrWhiteSpace(count) ? null : count;
        AccessibleName = _count is null ? _caption : $"{_caption}, {_count}";
        Invalidate(true);
    }

    /// <summary>
    /// When the job was last done - "Last posted 12/10/2026" on Stocktake -
    /// under the shortcut, or nothing. Read out after the shortcut.
    /// </summary>
    internal void ShowNote(string? note)
    {
        _note.Text = note ?? "";
        AccessibleDescription = Enabled && _note.Text.Length > 0
            ? $"{_shortcut}. {_note.Text}"
            : _hint.Text;
    }

    private void Choose()
    {
        if (Enabled) Chosen?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            Choose();
            e.Handled = true;
            return;
        }

        // Up and down a row, to the card nearest in the row above or
        // below. Left and right already step card to card.
        if (e.KeyCode is Keys.Up or Keys.Down && Parent is not null)
        {
            var down = e.KeyCode == Keys.Down;

            var next = Parent.Controls.OfType<HomeTile>()
                .Where(t => t.Visible && t.Enabled
                            && (down ? t.Top > Top : t.Top < Top))
                .OrderBy(t => Math.Abs(t.Top - Top))
                .ThenBy(t => Math.Abs(t.Left - Left))
                .FirstOrDefault();

            next?.Focus();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Enter or Keys.Space or Keys.Up or Keys.Down
        || base.IsInputKey(keyData);

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Hot(true);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        _hot = false;
        BackColor = Enabled ? PhxTheme.Card : PhxTheme.Band;
        Invalidate();
    }

    protected override AccessibleObject CreateAccessibilityInstance() =>
        new TileAccessibleObject(this);

    /// <summary>
    /// Lets a screen reader, or UI Automation, press the tile the way a
    /// click does.
    /// </summary>
    private sealed class TileAccessibleObject : ControlAccessibleObject
    {
        private readonly HomeTile _tile;

        public TileAccessibleObject(HomeTile tile) : base(tile) => _tile = tile;

        public override AccessibleRole Role => AccessibleRole.PushButton;

        public override string DefaultAction => "Open";

        public override void DoDefaultAction() => _tile.Choose();
    }

    /// <remarks>
    /// Leaving the card for one of its own labels raises MouseLeave, so
    /// the pointer is asked where it actually is rather than trusted.
    /// Without this the card flickers as the mouse crosses the text.
    /// </remarks>
    private void Hot(bool entering)
    {
        var hot = entering
            || ClientRectangle.Contains(PointToClient(MousePosition));

        if (hot == _hot) return;

        _hot = hot;
        BackColor = hot && Enabled ? PhxTheme.Hover : PhxTheme.Card;

        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);

        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        BackColor = Enabled ? PhxTheme.Card : PhxTheme.Band;
        _title.ForeColor = Enabled ? PhxTheme.Title : PhxTheme.Muted;

        _hint.Text = Enabled ? _shortcut : "Not permitted for this user";
        _note.Visible = Enabled;
        ShowNote(_note.Text);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        ArgumentNullException.ThrowIfNull(e);

        var radius = LogicalToDeviceUnits(8);
        using var edge = PhxTheme.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), radius);

        // The corners outside the card take the page's colour; the smoothed
        // outline drawn over their edge hides the steps.
        using (var outside = new Region(ClientRectangle))
        using (var page = new SolidBrush(PhxTheme.Behind(this)))
        {
            outside.Exclude(edge);
            e.Graphics.FillRegion(page, outside);
        }

        PaintIcon(e.Graphics);
        if (_count is not null && Enabled) PaintCount(e.Graphics);

        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        // The card under the mouse is outlined in the highlight, two
        // pixels wide; a pen that wide straddles its line, so it is drawn
        // one pixel in to stay inside the card.
        if (_hot && Enabled)
        {
            var ring = new Rectangle(1, 1, Width - 3, Height - 3);
            using var sweep = PhxTheme.Sweep(ring, PhxTheme.Brand, PhxTheme.Highlight);
            using var hot = new Pen(sweep, 2);
            using var inner = PhxTheme.Rounded(ring, radius);
            e.Graphics.DrawPath(hot, inner);
        }
        else
        {
            using var pen = new Pen(PhxTheme.Line);
            e.Graphics.DrawPath(pen, edge);
        }

        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
    }

    /// <summary>
    /// The icon's rounded square, in the top row above the title. The
    /// figure sits level with it on the right.
    /// </summary>
    /// <remarks>
    /// Sized from the title rather than in pixels, so that it grows with
    /// the display's scaling exactly as the labels under it do.
    /// </remarks>
    private Rectangle IconBox()
    {
        var chip = (int)(_title.Height * 1.3);
        var gap = _title.Height / 4;
        return new Rectangle(_title.Left, _title.Top - gap - chip, chip, chip);
    }

    /// <summary>
    /// The job's icon on a rounded square of the brand's tint.
    /// </summary>
    private void PaintIcon(Graphics graphics)
    {
        var box = IconBox();
        var chip = box.Width;

        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        // A coloured chip, swept like the primary buttons, with a white icon.
        using (var path = PhxTheme.Rounded(box, chip / 4))
        using (Brush fill = Enabled
                   ? PhxTheme.Sweep(box, PhxTheme.Brand, PhxTheme.BrandSweep)
                   : new SolidBrush(PhxTheme.Line))
            graphics.FillPath(fill, path);

        var glyph = (int)(chip * 0.62);

        graphics.DrawImage(
            PhxIcons.Image(_icon, glyph, Enabled ? Color.White : PhxTheme.Muted),
            box.X + (chip - glyph) / 2, box.Y + (chip - glyph) / 2, glyph, glyph);
    }

    /// <summary>
    /// The figure as a pill in the brand's colour, level with the icon and
    /// against the card's right-hand margin, in a row of its own so that
    /// neither the figure nor the title has to give way to the other.
    /// </summary>
    private void PaintCount(Graphics graphics)
    {
        var icon = IconBox();

        // At the title's zoom: the labels' fonts are scaled with the
        // window, and this one is not a label's.
        using var font = new Font(CountFont.FontFamily,
            CountFont.Size * _title.Font.Size / PhxTheme.Subheading.Size,
            CountFont.Style);

        var text = TextRenderer.MeasureText(_count, font);
        var height = text.Height + 4;
        var room = _title.Right - icon.Right - icon.Width / 3;
        var width = Math.Min(room, Math.Max(height, text.Width + height));

        var pill = new Rectangle(_title.Right - width,
            icon.Top + (icon.Height - height) / 2, width, height);

        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using (var path = PhxTheme.Rounded(pill, height / 2))
        using (var fill = new SolidBrush(PhxTheme.Brand))
            graphics.FillPath(fill, path);

        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;

        var inside = Rectangle.Inflate(pill, -height / 3, 0);

        TextRenderer.DrawText(graphics, _count, font, inside, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}
