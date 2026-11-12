using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Controls;

/// <summary>
/// One match offered under a search box.
/// </summary>
/// <param name="Value">
/// What choosing it hands back: the patient's id, the product's code.
/// </param>
/// <param name="Text">What it is called, in bold.</param>
/// <param name="Detail">
/// What tells it apart from others of the same name - a file number, a
/// telephone number, an address, a price.
/// </param>
public sealed record Suggestion(object Value, string Text, string? Detail = null);

/// <summary>
/// Matches offered under a search box while the operator types, so a
/// patient or a product can be picked after the first few letters rather
/// than once the whole name has been typed.
/// </summary>
/// <remarks>
/// <para>
/// Two ways of being fed. A list screen already searches as it is typed
/// into, so it hands its own results over (<see cref="Offer"/>) and the
/// database is asked once. A box with no list of its own - the till's
/// scan bar - gives a source instead, asked a quarter of a second after
/// the typing stops.
/// </para>
/// <para>
/// The keyboard stays in the box: Down and Up move through the matches,
/// Enter takes the highlighted one, Escape closes the list and nothing
/// else. With nothing highlighted, Enter does what it always did, so a
/// scanned barcode is never intercepted.
/// </para>
/// </remarks>
public sealed class SearchSuggestions : IDisposable
{
    private readonly TextBox _box;
    private readonly SuggestionList _list = new();
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 250 };
    private readonly KeyFilter _keys;
    private CancellationTokenSource? _pending;
    private bool _disposed;
    private bool _open;

    /// The text the list was closed or answered for, so a screen that
    /// refreshes on a timer does not open it again under the same words.
    private string? _settledFor;

    public SearchSuggestions(TextBox box,
        Func<string, CancellationToken, Task<IReadOnlyList<Suggestion>>>? source = null)
    {
        _box = box ?? throw new ArgumentNullException(nameof(box));
        _keys = new KeyFilter(this);
        Source = source;

        _box.PreviewKeyDown += OnPreviewKeyDown;
        _box.KeyDown += OnKeyDown;
        _box.Leave += (_, _) => Hide();
        _box.Disposed += (_, _) => Dispose();
        _box.TextChanged += (_, _) => Restart();
        _debounce.Tick += async (_, _) => await AskAsync();
        _list.Picked += (_, suggestion) => Choose(suggestion);
    }

    /// <summary>
    /// Where matches come from for a box with no list of its own. May be
    /// given after the box is built, once the screen has its services.
    /// </summary>
    public Func<string, CancellationToken,
        Task<IReadOnlyList<Suggestion>>>? Source { get; set; }

    /// Letters typed before anything is offered.
    public int MinimumLength { get; init; } = 2;

    /// How many matches at most.
    public int Maximum { get; init; } = 8;

    /// <summary>
    /// Whether a typed text is one to offer matches for. A source that
    /// is not worth asking - a barcode, which the scanner finishes on
    /// its own - can be turned away here.
    /// </summary>
    public Func<string, bool> Wanted { get; init; } = _ => true;

    public event EventHandler<Suggestion>? Chosen;

    public bool IsOpen => _open;

    /// For a test, which has no window to give the box the focus in.
    internal bool AssumeFocused { get; set; }

    public bool HasHighlight => IsOpen && _list.Highlighted is not null;

    /// What is on offer now, top to bottom.
    public IReadOnlyList<Suggestion> Showing => _list.Items;

    /// <summary>
    /// Offers these matches, when the box is being typed into.
    /// </summary>
    /// <remarks>
    /// For a screen that searched already: nothing is shown once the
    /// operator has moved on from the box, and nothing for a search too
    /// short to mean anything.
    /// </remarks>
    public void Offer(IEnumerable<Suggestion> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);

        var text = _box.Text.Trim();

        if (_disposed || (!AssumeFocused && !_box.ContainsFocus)
            || text.Length < MinimumLength
            || !Wanted(text) || text == _settledFor)
        {
            Hide();
            return;
        }

        Open(matches.Take(Maximum).ToList());
    }

    public void Hide()
    {
        if (_disposed) return;
        _open = false;
        _list.Visible = false;
        _list.Highlight(-1);
        _keys.Listen(false);
    }

    /// <summary>
    /// Takes the highlighted match, if there is one. For a box that
    /// handles Enter itself and needs to ask first.
    /// </summary>
    public bool TryChooseHighlighted()
    {
        if (!HasHighlight) return false;

        Choose(_list.Highlighted!);
        return true;
    }

    private void Choose(Suggestion suggestion)
    {
        _settledFor = _box.Text.Trim();
        Hide();
        Chosen?.Invoke(this, suggestion);

        // A screen that writes the choice into the box has answered it.
        _debounce.Stop();
        if (!_disposed) _settledFor = _box.Text.Trim();
    }

    private void Open(IReadOnlyList<Suggestion> matches)
    {
        if (matches.Count == 0)
        {
            Hide();
            return;
        }

        var form = _box.FindForm();
        if (form is null || _box.Parent is null) return;

        if (_list.Parent != form) form.Controls.Add(_list);

        _list.Show(matches);

        // Under the box, in the form's own coordinates: as wide as the
        // widest match needs, so the address or the price on the end is
        // read rather than cut off, never narrower than the box and never
        // past the right-hand edge of the window.
        var below = form.PointToClient(
            _box.Parent.PointToScreen(new Point(_box.Left, _box.Bottom + 1)));

        var room = Math.Max(_box.Width, form.ClientSize.Width - below.X - 8);
        var width = Math.Min(Math.Max(_box.Width, _list.WidthFor(matches)),
                             room);

        _list.Bounds = new Rectangle(below.X, below.Y, width,
                                     _list.HeightFor(matches.Count));
        _open = true;
        _list.Visible = true;
        _list.BringToFront();
        _keys.Listen(true);
    }

    private void Restart()
    {
        _debounce.Stop();

        // New words: whatever was closed was closed for the old ones.
        if (_box.Text.Trim() != _settledFor) _settledFor = null;

        if (_box.Text.Trim().Length < MinimumLength || !Wanted(_box.Text.Trim()))
        {
            Hide();
            return;
        }

        if (Source is not null) _debounce.Start();
    }

    private async Task AskAsync()
    {
        _debounce.Stop();

        if (Source is null || _disposed) return;

        var term = _box.Text.Trim();
        if (term.Length < MinimumLength || !Wanted(term)) return;

        _pending?.Cancel();
        _pending?.Dispose();
        _pending = new CancellationTokenSource();
        var token = _pending.Token;

        IReadOnlyList<Suggestion> found;

        try
        {
            found = await Source(term, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
#pragma warning disable CA1031 // A suggestion is a convenience: a failed one shows nothing.
        catch (Exception)
#pragma warning restore CA1031
        {
            return;
        }

        // Typed on while the answer was on its way: it answers a search
        // nobody is making any more.
        if (token.IsCancellationRequested || _disposed
            || !string.Equals(_box.Text.Trim(), term, StringComparison.Ordinal))
            return;

        Offer(found);
    }

    private void OnPreviewKeyDown(object? sender, PreviewKeyDownEventArgs e)
    {
        // Kept for the list, not given to the form: otherwise Enter would
        // press the form's default button and Escape would close it.
        if (!IsOpen) return;

        if (e.KeyCode is Keys.Down or Keys.Up or Keys.Escape
            || (e.KeyCode == Keys.Enter && HasHighlight))
            e.IsInputKey = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!Press(e.KeyCode)) return;

        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    /// <summary>
    /// A key pressed in the box while the list is open. True where the
    /// list used it, so the box and the form do not.
    /// </summary>
    internal bool Press(Keys key)
    {
        if (!IsOpen) return false;

        switch (key)
        {
            case Keys.Down:
                _list.Move(+1);
                return true;

            case Keys.Up:
                _list.Move(-1);
                return true;

            case Keys.Escape:
                _settledFor = _box.Text.Trim();
                Hide();
                return true;

            case Keys.Enter when HasHighlight:
                Choose(_list.Highlighted!);
                return true;

            default:
                return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _keys.Listen(false);
        _pending?.Cancel();
        _pending?.Dispose();
        _debounce.Dispose();
        _list.Dispose();
    }

    /// <summary>
    /// Down, Up, Enter and Escape for the list while it is open, taken
    /// before anything else sees them.
    /// </summary>
    /// <remarks>
    /// A grid's cell editor is a text box too, and a list under a product
    /// code cell is as useful as one under a search box - but the grid
    /// takes those four keys for itself, to move between rows and to end
    /// the edit, before the editor ever hears of them. Read off the
    /// message queue they reach the list first, in a cell or in a box.
    /// </remarks>
    private sealed class KeyFilter : IMessageFilter
    {
        private const int KeyDown = 0x0100;
        private readonly SearchSuggestions _owner;
        private bool _listening;

        public KeyFilter(SearchSuggestions owner) => _owner = owner;

        public void Listen(bool on)
        {
            if (on == _listening) return;

            _listening = on;
            if (on) System.Windows.Forms.Application.AddMessageFilter(this);
            else System.Windows.Forms.Application.RemoveMessageFilter(this);
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != KeyDown || !_owner._box.IsHandleCreated
                || m.HWnd != _owner._box.Handle) return false;

            var key = (Keys)(int)(long)m.WParam & Keys.KeyCode;

            return key is Keys.Down or Keys.Up or Keys.Enter or Keys.Escape
                   && _owner.Press(key);
        }
    }

    /// <summary>
    /// The drawn list itself. Never takes the focus, so typing carries on
    /// in the search box while it is open and a click on it does not
    /// close it before the click lands.
    /// </summary>
    private sealed class SuggestionList : Control
    {
        private const int RowHeight = 26;
        private readonly List<Suggestion> _items = new();

        /// The keyboard's choice, which Enter takes.
        private int _highlight = -1;

        /// The row under the mouse, shaded only. Kept apart from the
        /// keyboard's choice because the list can open under a pointer
        /// resting where it happens to be, and Enter must not then take
        /// whatever row that is.
        private int _hot = -1;
        private readonly Font _bold = new(PhxTheme.Body, FontStyle.Bold);

        public SuggestionList()
        {
            SetStyle(ControlStyles.Selectable, false);
            SetStyle(ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint
                     | ControlStyles.ResizeRedraw, true);

            TabStop = false;
            Visible = false;
            BackColor = PhxTheme.Card;
            Font = PhxTheme.Body;
            Name = "lstSuggestions";
            AccessibleName = "Suggestions";
            AccessibleRole = AccessibleRole.List;
        }

        public event EventHandler<Suggestion>? Picked;

        public IReadOnlyList<Suggestion> Items => _items;

        public Suggestion? Highlighted =>
            _highlight >= 0 && _highlight < _items.Count ? _items[_highlight] : null;

        public int HeightFor(int count) => count * RowHeight + 2;

        /// The width that shows every match whole.
        public int WidthFor(IEnumerable<Suggestion> items) =>
            items.Select(item =>
                    TextRenderer.MeasureText(item.Text, _bold).Width + 14
                    + (string.IsNullOrWhiteSpace(item.Detail)
                        ? 0
                        : TextRenderer.MeasureText(item.Detail, Font).Width)
                    + 28)
                .DefaultIfEmpty(0)
                .Max();

        public void Show(IReadOnlyList<Suggestion> items)
        {
            _items.Clear();
            _items.AddRange(items);
            _highlight = -1;
            _hot = -1;
            Invalidate();
        }

        public void Highlight(int index)
        {
            _highlight = index;
            Invalidate();
        }

        public void Move(int by)
        {
            if (_items.Count == 0) return;

            var next = _highlight < 0
                ? (by > 0 ? 0 : _items.Count - 1)
                : (_highlight + by + _items.Count) % _items.Count;

            Highlight(next);
            AccessibilityNotifyClients(AccessibleEvents.Focus, next);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            var row = RowAt(e.Y);
            if (row == _hot) return;

            _hot = row;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hot = -1;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button != MouseButtons.Left) return;

            var row = RowAt(e.Y);
            if (row >= 0) Picked?.Invoke(this, _items[row]);
        }

        private int RowAt(int y)
        {
            var row = (y - 1) / RowHeight;
            return row >= 0 && row < _items.Count ? row : -1;
        }

        internal Rectangle RowBounds(int index) =>
            new(1, 1 + index * RowHeight, Width - 2, RowHeight);

        protected override void OnPaint(PaintEventArgs e)
        {
            ArgumentNullException.ThrowIfNull(e);

            var g = e.Graphics;
            g.Clear(BackColor);

            for (var i = 0; i < _items.Count; i++)
            {
                var row = RowBounds(i);

                if (i == _highlight)
                    using (var fill = new SolidBrush(PhxTheme.Information))
                        g.FillRectangle(fill, row);
                else if (i == _hot)
                    using (var fill = new SolidBrush(PhxTheme.Band))
                        g.FillRectangle(fill, row);

                var item = _items[i];
                var textArea = new Rectangle(row.X + 8, row.Y, row.Width - 16,
                                             row.Height);

                var flags = TextFormatFlags.VerticalCenter
                            | TextFormatFlags.EndEllipsis
                            | TextFormatFlags.SingleLine
                            | TextFormatFlags.NoPrefix;

                TextRenderer.DrawText(g, item.Text, _bold, textArea,
                    PhxTheme.Title, flags);

                if (string.IsNullOrWhiteSpace(item.Detail)) continue;

                var used = TextRenderer.MeasureText(g, item.Text, _bold,
                    textArea.Size, flags).Width + 14;

                if (used >= textArea.Width - 40) continue;

                TextRenderer.DrawText(g, item.Detail, Font,
                    new Rectangle(textArea.X + used, row.Y,
                                  textArea.Width - used, row.Height),
                    PhxTheme.Muted, flags);
            }

            using var border = new Pen(PhxTheme.Line);
            g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        }

        protected override AccessibleObject CreateAccessibilityInstance() =>
            new ListAccessible(this);

        protected override void Dispose(bool disposing)
        {
            if (disposing) _bold.Dispose();
            base.Dispose(disposing);
        }

        /// The matches as list items, for a screen reader and for UI
        /// automation, which cannot see a drawn list otherwise.
        private sealed class ListAccessible : ControlAccessibleObject
        {
            private readonly SuggestionList _owner;

            public ListAccessible(SuggestionList owner) : base(owner) =>
                _owner = owner;

            public override AccessibleRole Role => AccessibleRole.List;

            public override int GetChildCount() => _owner._items.Count;

            public override AccessibleObject? GetChild(int index) =>
                index >= 0 && index < _owner._items.Count
                    ? new ItemAccessible(_owner, index, this)
                    : null;
        }

        private sealed class ItemAccessible : AccessibleObject
        {
            private readonly SuggestionList _owner;
            private readonly int _index;
            private readonly AccessibleObject _parent;

            public ItemAccessible(SuggestionList owner, int index,
                                  AccessibleObject parent)
            {
                _owner = owner;
                _index = index;
                _parent = parent;
            }

            private Suggestion Item => _owner._items[_index];

            public override string? Name =>
                string.IsNullOrWhiteSpace(Item.Detail)
                    ? Item.Text
                    : $"{Item.Text}  {Item.Detail}";

            public override AccessibleRole Role => AccessibleRole.ListItem;

            public override AccessibleObject Parent => _parent;

            public override Rectangle Bounds =>
                _owner.RectangleToScreen(_owner.RowBounds(_index));

            public override AccessibleStates State =>
                AccessibleStates.Selectable
                | (_owner._highlight == _index
                    ? AccessibleStates.Selected | AccessibleStates.Focused
                    : AccessibleStates.None);

            public override string DefaultAction => "Choose";

            public override void DoDefaultAction() =>
                _owner.Picked?.Invoke(_owner, Item);
        }
    }
}
