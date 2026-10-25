using System.Drawing.Printing;
using System.Globalization;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Sales;

namespace PharmaX.Adapters.Printing;

/// <summary>
/// The trailer label, on the same page as the item labels and printed
/// straight after them: the script number and the
/// patient, every medicine on the script with how many were dispensed,
/// the prescriber and the date, and the pharmacy.
/// </summary>
/// <remarks>
/// Laid out as the pharmacy's existing trailer labels are, on the same
/// page and the same columns as the item labels:
/// <code>
/// RX#: 13049 JOSEPHAT NYAKUNHUWA (001)
/// ENALAPRIL +5 10MG TAB (15)
/// SPIRONOLACTONE ALDAC 25MG TAB (5)
/// DR: madondoro D                       08/09/2026
/// Bethrapha Pharmacy, 28 Woodstock Road, Thorngrove, Bulawayo
/// </code>
/// No "Keep out of reach of children": the bag the labels go on prints it.
/// A script with more medicines than one label holds carries on to a
/// second, headed the same way, so nothing on the script is left off.
/// Whether a trailer prints at all is the dispensing screen's choice
/// (TrailerLabels.WantedAsync).
/// </remarks>
public sealed class TrailerLabelDocument : PrintDocument
{
    private const int Columns = LabelDocument.Columns;

    private readonly TrailerLabelDto _trailer;
    private readonly IReadOnlyList<string> _foot;
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
        _foot = Foot(trailer, dispensedBy, telephone);

        // The item labels' page, so the trailer comes off the same roll.
        LabelDocument.ApplyStock(DefaultPageSettings);
    }

    protected override void OnQueryPageSettings(QueryPageSettingsEventArgs e)
    {
        base.OnQueryPageSettings(e);
        LabelDocument.ApplyStock(e.PageSettings);
    }

    protected override void OnBeginPrint(PrintEventArgs e)
    {
        base.OnBeginPrint(e);
        LabelDocument.ApplyStock(DefaultPageSettings);
        _next = 0;
        _page = 0;
    }

    protected override void OnPrintPage(PrintPageEventArgs e)
    {
        base.OnPrintPage(e);
        ArgumentNullException.ThrowIfNull(e);

        e.HasMorePages = RenderPage(e.Graphics!, FixedPitch.Area(e));
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

        return HeadingLines(trailer, continued: false)
            .Concat(trailer.Items.SelectMany(ItemLines))
            .Concat(Foot(trailer, dispensedBy, telephone))
            .ToList();
    }

    /// <summary>
    /// What closes every trailer label: the prescriber and the date, the
    /// pharmacy with its address, and its telephone.
    /// </summary>
    private static List<string> Foot(TrailerLabelDto trailer,
        string? dispensedBy, string? telephone)
    {
        var foot = new List<string> { Spread(Doctor(trailer), Dated(trailer)) };

        // The pharmacy and its address whole, over two lines where it
        // takes them, as the item labels carry it.
        if (!string.IsNullOrWhiteSpace(dispensedBy))
            foot.AddRange(SlipLayout.Wrap(dispensedBy.Trim(), Columns, Columns)
                                    .Where(piece => piece.Length > 0));

        if (!string.IsNullOrWhiteSpace(telephone))
            foot.Add(Fit($"Tel: {telephone.Trim()}", Columns));

        return foot;
    }

    /// <summary>
    /// Draws one label and says whether the script carries on to another.
    /// Separate from OnPrintPage so a test can render it to a bitmap.
    /// </summary>
    internal bool RenderPage(Graphics g, Rectangle bounds)
    {
        ArgumentNullException.ThrowIfNull(g);

        // The heading and the foot on every label; the medicines between,
        // printed as the item labels are. In the smaller face: in the full
        // one the date against the right edge lost the last digit of its
        // year to the edge of what the printer reaches.
        var rows = TakePage();

        LabelPrint.Draw(g, bounds,
            rows.Select(row => new LabelRow(row, LabelStyle.Detail, Compact: true)).ToList(),
            Columns, Math.Max(LabelDocument.LineCount, rows.Count));

        return _next < _trailer.Items.Count;
    }

    /// <summary>
    /// The next sticker's rows, and advances the count of medicines used.
    /// </summary>
    private List<string> TakePage()
    {
        var room = Math.Max(1, LabelDocument.LineCount - 1 - _foot.Count);
        var items = _trailer.Items;
        var rows = new List<string>();
        rows.AddRange(HeadingLines(_trailer, continued: _page > 0));
        var used = 0;

        while (_next < items.Count)
        {
            var lines = ItemLines(items[_next]).ToList();
            if (used > 0 && used + lines.Count > room)
                break;

            rows.AddRange(lines);
            used += Math.Max(1, lines.Count);
            _next++;
        }

        rows.AddRange(_foot);
        _page++;

        return rows;
    }

    /// <summary>
    /// "RX#:", the script, the patient and which issue it is. A long
    /// name wraps; the issue number is not dropped.
    /// </summary>
    private static IEnumerable<string> HeadingLines(TrailerLabelDto trailer, bool continued)
    {
        var culture = CultureInfo.CurrentCulture;
        var text = string.Format(culture, "RX#: {0} {1} ({2:000}){3}",
            trailer.ScriptNumber, trailer.PatientName.Trim(), trailer.FillNumber,
            continued ? " cont." : "");

        return SlipLayout.Wrap(text.Trim(), Columns, Columns)
                         .Where(piece => piece.Length > 0);
    }

    /// <summary>
    /// A medicine and how many, wrapping the name onto the next line
    /// rather than dropping characters. The quantity stays with the name.
    /// </summary>
    private static IEnumerable<string> ItemLines(TrailerItemDto item)
    {
        var quantity = $" ({item.Quantity.ToString("0.##", CultureInfo.CurrentCulture)})";

        return SlipLayout.Wrap(item.Description.Trim() + quantity, Columns, Columns)
                         .Where(piece => piece.Length > 0);
    }

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

    /// The left text and the right one at the far edge of the line.
    private static string Spread(string left, string right)
    {
        var room = Columns - right.Length - 1;

        return Fit(left, room).PadRight(room + 1) + right;
    }

    /// Text cut to a width, with an ellipsis where it was cut.
    private static string Fit(string text, int width)
    {
        if (width < 1) return "";

        return text.Length <= width ? text : text[..(width - 1)].TrimEnd() + "…";
    }
}
