using System.Globalization;

namespace PharmaX.Desktop.Controls;

/// <summary>Which way a list's pager turns.</summary>
public enum PageMove { First, Previous, Next, Last }

/// <summary>
/// The bar under every paged list: where the list is up to on the left,
/// First, Previous, Next and Last on the right.
/// </summary>
/// <remarks>
/// <para>
/// One bar for every list, whether the screen holds the whole list and
/// pages it itself (GridPager) or asks for a page at a time (the product
/// and patient lists), so a list turns its pages the same way everywhere.
/// </para>
/// <para>
/// Every size here follows the caption. The bars these replaced were
/// measured in pixels at 100 per cent, and at the 150 per cent a pharmacy
/// PC is commonly set to the captions outgrew them: "&lt; Previous" showed
/// only its "&lt;", and the bottom of every button was cut off.
/// </para>
/// </remarks>
public class PagerBar : TableLayoutPanel
{
    private readonly Label _shown = new();
    private readonly Button _first;
    private readonly Button _previous;
    private readonly Button _next;
    private readonly Button _last;
    private readonly FlowLayoutPanel _buttons;
    private readonly ToolTip _tips = new();

    // The captions in full and cut to their arrows, for a narrow list.
    private static readonly (string Full, string Short)[] Captions =
    {
        ("|< First", "|<"), ("< Pre&vious", "<"), ("Ne&xt >", ">"), ("Last >|", ">|")
    };

    private bool _arranging;

    /// What the bar says when there is nothing on the list.
    public string Empty { get; set; } = "Nothing to list";

    /// Raised for First, Previous, Next or Last, by button or key.
    public event EventHandler<PageMove>? PageRequested;

    public PagerBar(string name)
    {
        Name = name;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        ColumnCount = 2;
        RowCount = 1;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Dock = DockStyle.Bottom;
        Padding = new Padding(0, LogicalToDeviceUnits(4), 0, LogicalToDeviceUnits(4));

        _shown.AutoSize = true;
        _shown.Anchor = AnchorStyles.Left;
        _shown.Margin = new Padding(LogicalToDeviceUnits(4), 0, LogicalToDeviceUnits(8), 0);
        _shown.Name = name + "Shown";

        _first = Make(Captions[0].Full, "btnFirstPage", "First page (Ctrl+Shift+Page Up)", PageMove.First);
        _previous = Make(Captions[1].Full, "btnPreviousPage", "Previous page (Ctrl+Page Up)", PageMove.Previous);
        _next = Make(Captions[2].Full, "btnNextPage", "Next page (Ctrl+Page Down)", PageMove.Next);
        _last = Make(Captions[3].Full, "btnLastPage", "Last page (Ctrl+Shift+Page Down)", PageMove.Last);

        _buttons = new FlowLayoutPanel
        {
            Anchor = AnchorStyles.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = Padding.Empty,
            Name = name + "Buttons",
            WrapContents = false
        };

        _buttons.Controls.AddRange(new Control[] { _first, _previous, _next, _last });

        Controls.Add(_shown, 0, 0);
        Controls.Add(_buttons, 1, 0);

        ShowPage(0, 1, 0, 0);
    }

    /// <summary>
    /// Under a narrow list - the day-end's lines beside its batches - the
    /// count goes above the buttons, and where even the buttons will not
    /// fit side by side they are cut to their arrows, the words staying in
    /// their tool tips. Never off the edge of the list.
    /// </summary>
    protected override void OnLayout(LayoutEventArgs levent)
    {
        // Laid out while it is still being built, before its buttons are.
        if (!_arranging && Width > 0 && _buttons is not null)
        {
            _arranging = true;

            try
            {
                Arrange();
            }
            finally
            {
                _arranging = false;
            }
        }

        base.OnLayout(levent);
    }

    private void Arrange()
    {
        var room = ClientSize.Width - Padding.Horizontal;

        // Full captions where they fit on their own; arrows otherwise.
        var full = ButtonsWidth(Captions.Select(c => c.Full)) <= room;
        var buttons = new[] { _first, _previous, _next, _last };

        for (var i = 0; i < buttons.Length; i++)
        {
            var text = full ? Captions[i].Full : Captions[i].Short;
            if (buttons[i].Text == text) continue;

            buttons[i].AutoSize = false;
            buttons[i].Text = text;
            buttons[i].Size = buttons[i].MinimumSize = full
                ? new Size(LogicalToDeviceUnits(84), LogicalToDeviceUnits(30))
                : new Size(LogicalToDeviceUnits(40), LogicalToDeviceUnits(30));
            buttons[i].AutoSize = true;
        }

        // The count beside the buttons, or above them.
        var beside = _shown.PreferredWidth + _shown.Margin.Horizontal
                     + _buttons.PreferredSize.Width <= room;
        var cell = beside ? new TableLayoutPanelCellPosition(1, 0)
                          : new TableLayoutPanelCellPosition(0, 1);

        if (GetCellPosition(_buttons) == cell) return;

        while (RowStyles.Count < 2) RowStyles.Add(new RowStyle(SizeType.AutoSize));
        RowCount = beside ? 1 : 2;
        SetCellPosition(_buttons, cell);
        SetColumnSpan(_shown, beside ? 1 : 2);
        SetColumnSpan(_buttons, beside ? 1 : 2);
    }

    /// How wide the four buttons come to with these captions.
    private int ButtonsWidth(IEnumerable<string> captions) =>
        captions.Sum(text => Math.Max(LogicalToDeviceUnits(84),
                                 TextRenderer.MeasureText(text, Font).Width
                                 + LogicalToDeviceUnits(20))
                             + LogicalToDeviceUnits(4));

    /// <summary>
    /// Sized by its caption at whatever scaling the display is set to,
    /// never smaller than the other buttons on a screen.
    /// </summary>
    private Button Make(string text, string name, string tip, PageMove move)
    {
        var button = new Button
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowOnly,
            Margin = new Padding(LogicalToDeviceUnits(4), 0, 0, 0),
            MinimumSize = new Size(LogicalToDeviceUnits(84), LogicalToDeviceUnits(30)),
            Name = Name + name,
            Text = text,
            UseVisualStyleBackColor = true
        };

        _tips.SetToolTip(button, tip);
        button.Click += (_, _) => PageRequested?.Invoke(this, move);
        return button;
    }

    /// <summary>Shows where the list is up to and which ways it can turn.</summary>
    /// <param name="page">From nought.</param>
    /// <param name="shown">How many are on this page.</param>
    public void ShowPage(int page, int pageSize, int shown, int total)
    {
        var pages = Pages(total, pageSize);
        var first = page * pageSize + 1;

        _shown.Text = total == 0
            ? Empty
            : pages == 1
                ? string.Format(CultureInfo.CurrentCulture, "{0:N0} shown", total)
                : string.Format(CultureInfo.CurrentCulture,
                    "{0:N0} to {1:N0} of {2:N0}  -  page {3:N0} of {4:N0}",
                    first, first + shown - 1, total, page + 1, pages);

        _first.Enabled = _previous.Enabled = page > 0;
        _next.Enabled = _last.Enabled = page + 1 < pages;
    }

    /// <summary>
    /// Ctrl+Page Up and Ctrl+Page Down for the page either side, with Shift
    /// for the first and the last, as long as the button would take it.
    /// </summary>
    /// <returns>True where the key turned the page.</returns>
    public bool Press(Keys keyData)
    {
        PageMove? move = keyData switch
        {
            Keys.Control | Keys.PageUp => PageMove.Previous,
            Keys.Control | Keys.PageDown => PageMove.Next,
            Keys.Control | Keys.Shift | Keys.PageUp => PageMove.First,
            Keys.Control | Keys.Shift | Keys.PageDown => PageMove.Last,
            _ => null
        };

        if (move is not PageMove turn || !Button(turn).Enabled) return false;

        PageRequested?.Invoke(this, turn);
        return true;
    }

    private Button Button(PageMove move) => move switch
    {
        PageMove.First => _first,
        PageMove.Previous => _previous,
        PageMove.Next => _next,
        _ => _last
    };

    /// How many pages a list of this many comes to: one, however short.
    public static int Pages(int total, int pageSize) =>
        Math.Max(1, (total + pageSize - 1) / Math.Max(1, pageSize));

    /// The page a move lands on, from nought, never past either end.
    public static int Target(int page, int total, int pageSize, PageMove move)
    {
        var last = Pages(total, pageSize) - 1;

        return Math.Clamp(move switch
        {
            PageMove.First => 0,
            PageMove.Previous => page - 1,
            PageMove.Next => page + 1,
            _ => last
        }, 0, last);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tips.Dispose();
        base.Dispose(disposing);
    }
}
