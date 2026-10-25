using PharmaX.Adapters.Printing;

namespace PharmaX.Desktop.Controls;

/// <summary>
/// A till-roll slip on screen: white paper, the printer's columns in a
/// fixed-pitch type, centred lines centred and bold lines bold - the rows
/// the printer is sent, so what is shown is what prints.
/// </summary>
/// <remarks>
/// The receipt template's preview, shared so the cash-up slip, the X-read
/// and the Z-read can be looked at the same way before they print.
/// </remarks>
public sealed class ReceiptPreview : Control
{
    private IReadOnlyList<ReceiptRow> _rows = Array.Empty<ReceiptRow>();
    private readonly Font _body = new("Consolas", 10f);
    private readonly Font _bold = new("Consolas", 10f, FontStyle.Bold);
    private readonly Font _title = new("Consolas", 12f, FontStyle.Bold);

    public ReceiptPreview()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
    }

    /// The rows the roll holds, and how many characters across it is.
    public IReadOnlyList<ReceiptRow> Rows => _rows;

    /// <param name="columns">
    /// Thirty-two for the receipt and the cash-up slip, forty-two for the
    /// X-read, the Z-read and the other plain documents.
    /// </param>
    public void Show(IReadOnlyList<ReceiptRow> rows, int columns = 32)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _rows = rows;

        using var g = CreateGraphics();
        var line = (int)Math.Ceiling(_body.GetHeight(g)) + 2;
        var column = TextRenderer.MeasureText(g, new string('0', columns), _body,
            Size.Empty, TextFormatFlags.NoPadding).Width;

        Size = new Size(column + 32, 24 + _rows.Sum(r => r.Style == ReceiptStyle.Title ? line + 4 : line) + 24);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        var y = 24;
        var width = ClientSize.Width - 32;

        foreach (var row in _rows)
        {
            var font = row.Style switch
            {
                ReceiptStyle.Title => _title,
                ReceiptStyle.Bold => _bold,
                _ => _body
            };
            var height = (int)Math.Ceiling(font.GetHeight(g)) + 2;
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine
                        | (row.Centred ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left);

            TextRenderer.DrawText(g, row.Text, font, new Rectangle(16, y, width, height),
                Color.Black, flags);
            y += height;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _body.Dispose();
            _bold.Dispose();
            _title.Dispose();
        }

        base.Dispose(disposing);
    }
}
