using PharmaX.Adapters.Printing;
using PharmaX.Application.Sales;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Tills;

/// <summary>
/// A till-roll document on screen before it prints - the cash-up slip,
/// the X-read, the Z-read - laid out row for row as the receipt printer
/// is sent it, under the pharmacy's own header.
/// </summary>
/// <remarks>
/// Printed straight away, a cash-up was only seen once it was on paper,
/// and a mistake in it - a payout entered twice, a till not closed - cost
/// a slip and a reprint (23 October 2026). Built in code; at 150% it
/// grows to the slip or to the screen, whichever is smaller, and scrolls.
/// </remarks>
public sealed class frmSlipPreview : PhxFormBase
{
    private readonly ReceiptPreview _preview = new();
    private readonly Button _print = new();
    private readonly Label _note = new();
    private readonly Panel _scroller = new();
    private readonly FlowLayoutPanel _buttons = new();

    /// Designer and tests only.
    public frmSlipPreview() : this("Preview", Array.Empty<ReceiptRow>(), 32, "&Print") { }

    private frmSlipPreview(string title, IReadOnlyList<ReceiptRow> rows, int columns,
                           string printText)
    {
        Text = $"PharmaX - {title} - preview";
        Name = "frmSlipPreview";
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(columns > 32 ? 470 : 380, 560);
        MinimumSize = new Size(360, 300);
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        _note.AutoSize = false;
        _note.BackColor = PhxTheme.Information;
        _note.Dock = DockStyle.Top;
        _note.Height = 44;
        _note.Name = "lblNote";
        _note.Padding = new Padding(10, 4, 10, 4);
        _note.Text = "As it will print on the till roll. Print it, or close and put it right first.";

        _scroller.AutoScroll = true;
        _scroller.BackColor = SystemColors.ControlDark;
        _scroller.Dock = DockStyle.Fill;
        _scroller.Name = "pnlRoll";
        _scroller.Padding = new Padding(12);
        _preview.Location = new Point(12, 12);
        _preview.Name = "pnlPreview";
        _scroller.Controls.Add(_preview);

        var buttons = _buttons;
        buttons.AutoSize = true;
        buttons.Dock = DockStyle.Bottom;
        buttons.FlowDirection = FlowDirection.RightToLeft;
        buttons.Padding = new Padding(10, 6, 10, 8);

        var close = new Button
        {
            AutoSize = true, DialogResult = DialogResult.Cancel, Name = "btnClose",
            Padding = new Padding(8, 2, 8, 2), Text = "Close"
        };

        _print.AutoSize = true;
        _print.DialogResult = DialogResult.OK;
        _print.Name = "btnPrint";
        _print.Padding = new Padding(8, 2, 8, 2);
        _print.Text = printText;

        buttons.Controls.Add(close);
        buttons.Controls.Add(_print);

        Controls.Add(_scroller);
        Controls.Add(_note);
        Controls.Add(buttons);

        AcceptButton = _print;
        CancelButton = close;

        _rows = rows;
        _columns = columns;
    }

    private readonly IReadOnlyList<ReceiptRow> _rows;
    private readonly int _columns;

    /// The rows on screen, for a test to read.
    internal IReadOnlyList<ReceiptRow> Rows => _preview.Rows;

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // After the window has scaled: the paper sizes itself from its own
        // type, and scaled again on top it came out half as wide again.
        _preview.Show(_rows, _columns);
        PhxTheme.MakePrimary(_print);
        ActiveControl = _print;

        FitToSlip();
    }

    /// <summary>
    /// As wide as the paper, so a forty-two column read is not scrolled
    /// sideways, and as tall as the slip or the screen, whichever is less.
    /// </summary>
    /// <remarks>
    /// Measured here rather than fixed in the constructor: the window is
    /// scaled by its font at load, so a figure written for 96 dpi was too
    /// narrow for the read and too short for the slip at 150%.
    /// </remarks>
    private void FitToSlip()
    {
        var width = _preview.Width + _scroller.Padding.Horizontal + SystemInformation.VerticalScrollBarWidth + 4;

        _note.Height = TextRenderer.MeasureText(_note.Text, _note.Font,
            new Size(width - _note.Padding.Horizontal, 0), TextFormatFlags.WordBreak).Height
            + _note.Padding.Vertical + 4;

        var height = _note.Height + _preview.Height + _scroller.Padding.Vertical
                     + _buttons.Height + 4;

        ClientSize = new Size(Math.Max(ClientSize.Width, width), height);

        // Never past the working area: the slip scrolls instead.
        PhxTheme.HoldToScreen(this);
        PhxTheme.MoveOnScreen(this);
    }

    /// <summary>
    /// Shows the document as it will print. True where the operator
    /// chose to print it.
    /// </summary>
    public static bool Confirm(IWin32Window owner, PrintableDocument document,
        ReceiptHeaderDto header, string printText = "&Print")
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(header);

        var (rows, columns) = DocumentPrinter.Layout(document, header);

        using var preview = new frmSlipPreview(document.Title, rows, columns, printText);
        return preview.ShowDialog(owner) == DialogResult.OK;
    }
}
