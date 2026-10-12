using System.Drawing.Printing;
using System.Globalization;
using PharmaX.Application.Dispensing;

namespace PharmaX.Adapters.Printing;

/// <summary>
/// The trailer label, on the same 50 by 70 millimetre stock as the item
/// labels and printed straight after them: the script number and the
/// patient, every medicine on the script with how many were dispensed,
/// the prescriber, and the pharmacy with the date.
/// </summary>
/// <remarks>
/// Laid out as the pharmacy's existing trailer labels are:
/// <code>
/// RX#: 13049 JOSEPHAT NYAKUNHUWA (001)
/// ENALAPRIL +5 10MG TAB (15)
/// SPIRONOLACTONE ALDAC 25MG TAB (5)
/// DR: madondoro D
/// ----------------------------------------
/// Bethrapha Pharmacy, 28 Woodstock Road  08/09/2026
/// </code>
/// A script with more medicines than one label holds carries on to a
/// second, headed the same way, so nothing on the script is left off.
/// </remarks>
public sealed class TrailerLabelDocument : PrintDocument
{
    private readonly TrailerLabelDto _trailer;
    private readonly string? _dispensedBy;
    private readonly string? _telephone;
    private readonly Font _body = new("Consolas", 7f);

    /// For the pharmacy's line when it will not fit beside the date.
    private readonly Font _small = new("Consolas", 6f);
    private readonly Pen _rule = new(Color.Black, 1f);
    private int _next;
    private int _page;

    /// <param name="dispensedBy">
    /// The pharmacy's name and address on one line, as the item labels
    /// carry it (LabelDocument.DispensedBy).
    /// </param>
    /// <param name="telephone">The pharmacy's number, under its name.</param>
    public TrailerLabelDocument(TrailerLabelDto trailer, string? dispensedBy,
                                string? telephone = null)
    {
        _trailer = trailer;
        _dispensedBy = string.IsNullOrWhiteSpace(dispensedBy)
            ? null
            : dispensedBy.Trim();
        _telephone = Telephone(telephone);

        // The item labels' stock, so the trailer comes off the same roll.
        DefaultPageSettings.PaperSize = new PaperSize("Label", 276, 197);
        DefaultPageSettings.Margins = new Margins(8, 8, 8, 8);
    }

    protected override void OnBeginPrint(PrintEventArgs e)
    {
        base.OnBeginPrint(e);
        _next = 0;
        _page = 0;
    }

    protected override void OnPrintPage(PrintPageEventArgs e)
    {
        base.OnPrintPage(e);
        ArgumentNullException.ThrowIfNull(e);

        e.HasMorePages = RenderPage(e.Graphics!, e.MarginBounds);
    }

    /// <summary>
    /// The trailer as text, top to bottom, every medicine included. What
    /// the Labels screen shows before it prints, and what a test reads.
    /// </summary>
    public static IReadOnlyList<string> Lines(TrailerLabelDto trailer,
                                              string? dispensedBy,
                                              string? telephone = null)
    {
        ArgumentNullException.ThrowIfNull(trailer);

        var lines = new List<string> { Heading(trailer, continued: false) };

        lines.AddRange(trailer.Items.Select(Item));
        lines.Add(Doctor(trailer));
        lines.Add(new string('-', 40));
        lines.Add(string.IsNullOrWhiteSpace(dispensedBy)
            ? Dated(trailer)
            : $"{dispensedBy.Trim()}  {Dated(trailer)}");

        if (Telephone(telephone) is string tel) lines.Add(tel);
        lines.Add(LabelWording.KeepOutOfReach);

        return lines;
    }

    private static string? Telephone(string? telephone) =>
        string.IsNullOrWhiteSpace(telephone) ? null : $"Tel: {telephone.Trim()}";

    /// <summary>
    /// Draws one label and says whether the script carries on to another.
    /// Separate from OnPrintPage so a test can render it to a bitmap.
    /// </summary>
    internal bool RenderPage(Graphics g, Rectangle bounds)
    {
        ArgumentNullException.ThrowIfNull(g);

        var height = _body.GetHeight(g) + 1f;
        float y = bounds.Top;

        void Line(string text)
        {
            g.DrawString(Fit(g, text, bounds.Width), _body, Brushes.Black,
                         bounds.Left, y);
            y += height;
        }

        Line(Heading(_trailer, continued: _page > 0));

        // What is left under the medicines: the prescriber, the rule, the
        // pharmacy's line, its telephone and "Keep out of reach of
        // children", which every label carries.
        var foot = height * (_telephone is null ? 3 : 4) + 6f;
        var room = Math.Max(1, (int)((bounds.Bottom - foot - y) / height));

        var items = _trailer.Items;
        var last = Math.Min(items.Count, _next + room);

        for (; _next < last; _next++)
            Line(ItemFitted(g, items[_next], bounds.Width));

        Line(Doctor(_trailer));

        y += 2f;
        g.DrawLine(_rule, bounds.Left, y, bounds.Right, y);
        y += 4f;

        // The pharmacy on the left and the date on the right. The line
        // steps down a size before it loses any of the address, and is
        // shortened only if it still will not fit.
        var date = Dated(_trailer);
        var font = _body;

        if (_dispensedBy is not null
            && g.MeasureString(_dispensedBy, _body).Width
               + g.MeasureString(date, _body).Width + 6 > bounds.Width)
            font = _small;

        var dateWidth = g.MeasureString(date, font).Width;

        if (_dispensedBy is not null)
            g.DrawString(Fit(g, _dispensedBy, bounds.Width - dateWidth - 6,
                             font),
                         font, Brushes.Black, bounds.Left, y);

        g.DrawString(date, font, Brushes.Black, bounds.Right - dateWidth, y);
        y += height;

        if (_telephone is not null) Line(_telephone);
        Line(LabelWording.KeepOutOfReach);

        _page++;

        return _next < items.Count;
    }

    private static string Heading(TrailerLabelDto trailer, bool continued) =>
        string.Format(CultureInfo.CurrentCulture, "RX#: {0} {1} ({2:000}){3}",
            trailer.ScriptNumber, trailer.PatientName, trailer.FillNumber,
            continued ? " cont." : "").Trim();

    private static string Item(TrailerItemDto item) =>
        $"{item.Description.Trim()} ({Quantity(item)})";

    private static string Quantity(TrailerItemDto item) =>
        item.Quantity.ToString("0.##", CultureInfo.CurrentCulture);

    /// <summary>
    /// "DR:" and the prescriber, without a second "Dr" where the name was
    /// captured with one.
    /// </summary>
    private static string Doctor(TrailerLabelDto trailer)
    {
        var name = (trailer.PrescriberName ?? "").Trim();

        foreach (var title in new[] { "Dr.", "Dr " })
        {
            if (name.StartsWith(title, StringComparison.OrdinalIgnoreCase))
            {
                name = name[title.Length..].Trim();
                break;
            }
        }

        return $"DR: {name}".TrimEnd();
    }

    private static string Dated(TrailerLabelDto trailer) =>
        trailer.DispensedOn.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture);

    /// <summary>
    /// A medicine that fits the label, shortening the name rather than
    /// the quantity: the quantity is what the bag is checked against.
    /// </summary>
    private string ItemFitted(Graphics g, TrailerItemDto item, float width)
    {
        var quantity = $" ({Quantity(item)})";
        var name = item.Description.Trim();

        if (g.MeasureString(name + quantity, _body).Width <= width)
            return name + quantity;

        while (name.Length > 1
               && g.MeasureString(name + "…" + quantity, _body).Width > width)
            name = name[..^1];

        return name.TrimEnd() + "…" + quantity;
    }

    private string Fit(Graphics g, string text, float width,
                       Font? font = null)
    {
        font ??= _body;

        if (g.MeasureString(text, font).Width <= width) return text;

        var cut = text;

        while (cut.Length > 1
               && g.MeasureString(cut + "…", font).Width > width)
            cut = cut[..^1];

        return cut.TrimEnd() + "…";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _body.Dispose();
            _small.Dispose();
            _rule.Dispose();
        }

        base.Dispose(disposing);
    }
}
