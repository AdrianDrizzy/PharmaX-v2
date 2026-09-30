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
    private readonly string _shortcut;
    private bool _hot;

    internal HomeTile(string title, string hint, string menuKey,
                      string permissionGroup)
    {
        MenuKey = menuKey;
        PermissionGroup = permissionGroup;
        _shortcut = hint;

        BackColor = Color.White;
        Cursor = Cursors.Hand;
        Margin = new Padding(0, 0, 14, 14);
        Size = new Size(212, 94);

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
        _title.Location = new Point(16, 18);
        _title.Size = new Size(180, 26);
        _title.Text = title;

        _hint.AutoSize = false;
        _hint.BackColor = Color.Transparent;
        _hint.Font = PhxTheme.Body;
        _hint.ForeColor = PhxTheme.Muted;
        _hint.Location = new Point(16, 48);
        _hint.Size = new Size(180, 22);
        _hint.Text = hint;

        Controls.Add(_hint);
        Controls.Add(_title);

        // The labels cover most of the card, so they have to answer to
        // the mouse as though they were part of it.
        foreach (var control in new Control[] { this, _title, _hint })
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

        base.OnKeyDown(e);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Hot(true);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        _hot = false;
        BackColor = Enabled ? Color.White : PhxTheme.Band;
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
        BackColor = hot && Enabled
            ? Color.FromArgb(243, 247, 253)
            : Color.White;

        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);

        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        BackColor = Enabled ? Color.White : PhxTheme.Band;
        _title.ForeColor = Enabled ? SystemColors.ControlText : PhxTheme.Muted;

        _hint.Text = Enabled ? _shortcut : "Not permitted for this user";
        AccessibleDescription = _hint.Text;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        ArgumentNullException.ThrowIfNull(e);

        using var pen = new Pen(_hot && Enabled
            ? PhxTheme.Accent
            : PhxTheme.Line);

        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}
