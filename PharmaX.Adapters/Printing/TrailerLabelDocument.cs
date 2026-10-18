using System.Drawing.Printing;
using System.Globalization;
using PharmaX.Application.Dispensing;

namespace PharmaX.Adapters.Printing;

/// <summary>
/// The trailer label, on the same 102 by 36 millimetre stock as the item
/// labels and printed straight after them: the script number and the
/// patient, every medicine on the script with how many were dispensed,
/// the prescriber and the date, and the pharmacy.
/// </summary>
/// <remarks>
/// Laid out as the pharmacy's existing trailer labels are, on the same 48
/// characters by 8 lines as the item labels:
/// <code>
/// RX#: 13049 JOSEPHAT NYAKUNHUWA (001)
/// ENALAPRIL +5 10MG TAB (15)
/// SPIRONOLACTONE ALDAC 25MG TAB (5)
/// DR: madondoro D                       08/09/2026
/// Bethrapha Pharmacy, 28 Woodstock Road
/// Keep out of reach of children.
/// </code>
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

        // The item labels' stock, so the trailer comes off the same roll.
        DefaultPageSettings.PaperSize = new PaperSize("Label", 400, 144);
        DefaultPageSettings.Margins = new Margins(4, 4, 4, 4);
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

        return new[] { Heading(trailer, continued: false) }
            .Concat(trailer.Items.Select(Item))
            .Concat(Foot(trailer, dispensedBy, telephone))
            .ToList();
    }

    /// <summary>
    /// What closes every trailer label: the prescriber and the date, the
    /// pharmacy, its telephone and "Keep out of reach of children", which
    /// every label carries.
    /// </summary>
    private static List<string> Foot(TrailerLabelDto trailer,
        string? dispensedBy, string? telephone)
    {
        var foot = new List<string> { Spread(Doctor(trailer), Dated(trailer)) };

        if (!string.IsNullOrWhiteSpace(dispensedBy))
            foot.Add(Fit(dispensedBy.Trim(), Columns));

        if (!string.IsNullOrWhiteSpace(telephone))
            foot.Add(Fit($"Tel: {telephone.Trim()}", Columns));

        foot.Add(LabelWording.KeepOutOfReach);
        return foot;
    }

    /// <summary>
    /// Draws one label and says whether the script carries on to another.
    /// Separate from OnPrintPage so a test can render it to a bitmap.
    /// </summary>
    internal bool RenderPage(Graphics g, Rectangle bounds)
    {
        ArgumentNullException.ThrowIfNull(g);

        // The heading and the foot on every label; the medicines between.
        var room = Math.Max(1, LabelDocument.LineCount - 1 - _foot.Count);
        var items = _trailer.Items;
        var rows = new List<string> { Heading(_trailer, continued: _page > 0) };

        for (var last = Math.Min(items.Count, _next + room); _next < last; _next++)
            rows.Add(Item(items[_next]));

        rows.AddRange(_foot);

        var size = FixedPitch.Size(g, Columns, bounds.Width, 12f,
            Math.Max(LabelDocument.LineCount, rows.Count), bounds.Height);

        using var font = new Font(FixedPitch.Family, size);
        using var exact = FixedPitch.Format();

        var height = font.GetHeight(g);
        float y = bounds.Top;

        foreach (var row in rows)
        {
            g.DrawString(row, font, Brushes.Black, bounds.Left, y, exact);
            y += height;
        }

        _page++;

        return _next < items.Count;
    }

    /// <summary>
    /// "RX#:", the script, the patient and which issue it is, shortening
    /// the name rather than losing the issue number.
    /// </summary>
    private static string Heading(TrailerLabelDto trailer, bool continued)
    {
        var culture = CultureInfo.CurrentCulture;
        var lead = $"RX#: {trailer.ScriptNumber} ";
        var tail = string.Format(culture, " ({0:000}){1}", trailer.FillNumber,
                                 continued ? " cont." : "");

        return (lead + Fit(trailer.PatientName.Trim(),
                           Columns - lead.Length - tail.Length) + tail).Trim();
    }

    /// <summary>
    /// A medicine and how many, shortening the name rather than the
    /// quantity: the quantity is what the bag is checked against.
    /// </summary>
    private static string Item(TrailerItemDto item)
    {
        var quantity = $" ({item.Quantity.ToString("0.##", CultureInfo.CurrentCulture)})";

        return Fit(item.Description.Trim(), Columns - quantity.Length) + quantity;
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
