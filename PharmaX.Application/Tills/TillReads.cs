using System.Globalization;
using PharmaX.Application.Sales;
using PharmaX.Application.Tills.Dtos;

namespace PharmaX.Application.Tills;

/// <summary>
/// The X-read and the Z-read as they print on the till roll.
/// </summary>
/// <remarks>
/// Closing a session said "The Z-read has printed" and nothing printed:
/// there was no print path for either read anywhere in the till code
/// (DEF-0018). These are the documents; the till session screen sends them
/// to the receipt printer and reports what really happened.
/// </remarks>
public static class TillReads
{
    /// <summary>
    /// An interim read: what the session should hold so far, by tender.
    /// The session stays open.
    /// </summary>
    /// <param name="payouts">
    /// Money taken out of the drawer so far, which the cash expected is
    /// already net of.
    /// </param>
    public static PrintableDocument XRead(TillSessionDto session,
                                          XReadDto read,
                                          IReadOnlyList<PayoutDto>? payouts = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(read);

        var culture = CultureInfo.CurrentCulture;
        var lines = Header(session);

        lines.Add(string.Format(culture, "X-read {0}   {1:dd/MM/yyyy HH:mm}",
            read.ReadNumber, read.ReadAtUtc.ToLocalTime()));
        lines.Add(new string('-', 42));
        lines.Add(string.Format(culture, "{0,-18}{1,8}{2,16}", "Tender",
            "Count", "Expected"));

        foreach (var tender in read.Expectations)
            lines.Add(string.Format(culture, "{0,-18}{1,8}{2,16:N2}",
                tender.TenderType, tender.TransactionCount, tender.Expected));

        lines.Add(new string('-', 42));
        lines.Add(string.Format(culture, "{0,-26}{1,16:N2}", "Total",
            read.GrandTotal));

        PaidOut(lines, payouts);

        lines.Add("");
        lines.Add("Interim read. The session is still open.");

        return new PrintableDocument($"X-read {read.ReadNumber}", lines);
    }

    /// <summary>
    /// The closing read: expected against declared, tender by tender, and
    /// why any variance happened.
    /// </summary>
    /// <param name="payouts">
    /// Money taken out of the drawer during the shift, which the cash
    /// expected is already net of.
    /// </param>
    public static PrintableDocument ZRead(TillSessionDto session,
        DeclarationDto declaration, string? varianceReason,
        DateTime closedAtLocal, IReadOnlyList<PayoutDto>? payouts = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(declaration);

        var culture = CultureInfo.CurrentCulture;
        var lines = Header(session);

        lines.Add(string.Format(culture, "Z-read   closed {0:dd/MM/yyyy HH:mm}",
            closedAtLocal));
        lines.Add(new string('-', 42));
        lines.Add(string.Format(culture, "{0,-12}{1,10}{2,10}{3,10}",
            "Tender", "Expected", "Declared", "Variance"));

        foreach (var line in declaration.Variances)
            lines.Add(string.Format(culture, "{0,-12}{1,10:N2}{2,10:N2}{3,10:N2}",
                line.TenderType.Length <= 12
                    ? line.TenderType
                    : line.TenderType[..12],
                line.Expected, line.Declared, line.Variance));

        lines.Add(new string('-', 42));
        lines.Add(string.Format(culture, "{0,-26}{1,16:N2}", "Declared",
            declaration.TotalDeclared));
        lines.Add(string.Format(culture, "{0,-26}{1,16:N2}", "Variance",
            declaration.TotalVariance));

        PaidOut(lines, payouts);

        if (!string.IsNullOrWhiteSpace(varianceReason))
        {
            lines.Add("");
            lines.Add($"Variance reason: {varianceReason}");
        }

        lines.Add("");
        lines.Add("Session closed.");

        return new PrintableDocument("Z-read", lines);
    }

    /// <summary>
    /// The money taken out of the drawer, each with what it was for, and
    /// the total the cash expected is short of the takings by.
    /// </summary>
    private static void PaidOut(List<string> lines, IReadOnlyList<PayoutDto>? payouts)
    {
        var paid = (payouts ?? Array.Empty<PayoutDto>()).Where(p => !p.IsVoided).ToList();
        if (paid.Count == 0) return;

        var culture = CultureInfo.CurrentCulture;
        const int amount = 12;
        const int room = 42 - amount;

        lines.Add("");
        lines.Add("Paid out of the drawer");

        foreach (var p in paid)
        {
            var pieces = SlipLayout.Wrap(string.Format(culture, "{0:HH:mm} {1}: {2}",
                p.PaidAtLocal, p.KindName, p.Note), room, room);

            lines.AddRange(pieces.SkipLast(1));
            lines.Add(pieces[^1].PadRight(room)
                      + (-p.Amount).ToString("N2", culture).PadLeft(amount));
        }

        lines.Add(new string('-', 42));
        lines.Add(string.Format(culture, "{0,-26}{1,16:N2}", "Paid out", -PayoutDto.Total(paid)));
        lines.Add("Cash expected is after the money paid out.");
    }

    private static List<string> Header(TillSessionDto session) => new()
    {
        string.Format(CultureInfo.CurrentCulture,
            "Till {0}   opened {1:dd/MM/yyyy HH:mm}", session.TillNumber,
            session.OpenedAtUtc.ToLocalTime()),
        $"Opened by {session.OpenedByName}",
        string.Format(CultureInfo.CurrentCulture, "Float {0:N2}",
            session.OpeningFloat)
    };
}
