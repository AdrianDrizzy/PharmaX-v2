namespace PharmaX.Application.Sales;

/// <summary>
/// Laying text out on the 42 characters of a receipt roll.
/// </summary>
public static class SlipLayout
{
    public const int Width = 42;

    /// <summary>
    /// Splits text at spaces: the first piece fits <paramref name="first"/>
    /// characters, the rest fit <paramref name="rest"/>. A word too long
    /// for either is split where it has to be.
    /// </summary>
    /// <remarks>
    /// Descriptions on the purchase order, quotation and credit note were
    /// cut to fit - "BethRapha Vitamin C 1000mg effe" - which on an order
    /// is the one line the supplier reads to know what to send
    /// (ADM-OBS-07).
    /// </remarks>
    public static IReadOnlyList<string> Wrap(string? text, int first, int rest)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(first, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rest, 1);

        var pieces = new List<string>();
        var remaining = (text ?? "").Trim();

        while (remaining.Length > 0)
        {
            var width = pieces.Count == 0 ? first : rest;

            if (remaining.Length <= width)
            {
                pieces.Add(remaining);
                break;
            }

            var cut = remaining.LastIndexOf(' ', width);
            if (cut <= 0) cut = width;

            pieces.Add(remaining[..cut].TrimEnd());
            remaining = remaining[cut..].TrimStart();
        }

        if (pieces.Count == 0) pieces.Add("");

        return pieces;
    }

    /// <summary>
    /// A description as slip lines: the first after <paramref name="lead"/>,
    /// the rest indented to line up under it.
    /// </summary>
    public static IEnumerable<string> Described(string lead, string? description)
    {
        ArgumentNullException.ThrowIfNull(lead);

        var indent = new string(' ', lead.Length);
        var pieces = Wrap(description, Width - lead.Length, Width - lead.Length);

        yield return lead + pieces[0];

        foreach (var more in pieces.Skip(1))
            yield return indent + more;
    }
}
