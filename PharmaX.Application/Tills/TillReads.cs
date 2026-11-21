using System.Globalization;
using PharmaX.Application.Sales;
using PharmaX.Application.Tills.Dtos;
using PharmaX.Domain.Sales;

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
        lines.Add(Rule());

        foreach (var tender in Statements(read.Expectations))
        {
            lines.Add(Label(tender.TenderType));
            Amount(lines, "  Expected", tender.Expected);
        }

        lines.Add(Rule());
        Amount(lines, "Total", read.GrandTotal);

        PaidOut(lines, payouts);

        lines.Add("");
        lines.Add("Interim read. The session is still open.");

        // Thirty-two across, the receipt's width. Forty-two ran the
        // declared and variance columns off the 75 mm roll.
        return new PrintableDocument($"X-read {read.ReadNumber}", lines, CashUpSlip.Width);
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
        lines.Add(Rule());

        foreach (var line in Statements(declaration.Variances))
        {
            lines.Add(Label(line.TenderType));
            Amount(lines, "  Expected", line.Expected);
            Amount(lines, "  Declared", line.Declared);
            Amount(lines, "  Variance", line.Variance);
        }

        lines.Add(Rule());
        Amount(lines, "DECLARED", declaration.TotalDeclared);
        Amount(lines, "VARIANCE", declaration.TotalVariance);

        PaidOut(lines, payouts);

        if (!string.IsNullOrWhiteSpace(varianceReason))
        {
            lines.Add("");
            lines.Add($"Variance reason: {varianceReason}");
        }

        lines.Add("");
        lines.Add("Session closed.");

        return new PrintableDocument("Z-read", lines, CashUpSlip.Width);
    }

    /// Cash, EcoCash and card, even at zero, then whatever else was taken.
    private static List<T> Statements<T>(IReadOnlyList<T> lines) where T : class
    {
        // The two shapes share a tender name. Kept here so a read that
        // recorded neither still prints the lines the cashier checks.
        static string Name(T row) => row switch
        {
            VarianceLineDto variance => variance.TenderType,
            TenderExpectationDto expected => expected.TenderType,
            _ => ""
        };

        var all = lines.ToList();

        foreach (var name in new[] { nameof(TenderType.Cash), nameof(TenderType.EcoCash), nameof(TenderType.Card) })
        {
            if (all.Exists(row => Name(row) == name)) continue;

            object blank = typeof(T) == typeof(VarianceLineDto)
                ? new VarianceLineDto(name, 0m, 0m, 0m, false)
                : new TenderExpectationDto(name, 0m, 0);
            all.Add((T)blank);
        }

        int Rank(string tender) => tender switch
        {
            "Cash" => 0,
            "EcoCash" => 1,
            "Card" => 2,
            _ => 3
        };

        all.Sort((a, b) => Rank(Name(a)).CompareTo(Rank(Name(b))));
        return all;
    }

    private static string Label(string tender) =>
        Enum.TryParse<TenderType>(tender, out var parsed) ? parsed.DisplayName() : tender;

    private static string Rule() => new('-', CashUpSlip.Width);

    private static void Amount(List<string> lines, string label, decimal value)
    {
        var amount = value.ToString("N2", CultureInfo.CurrentCulture);
        var room = CashUpSlip.Width - amount.Length - 1;
        var pieces = SlipLayout.Wrap(label, Math.Max(1, room), Math.Max(1, room));
        lines.AddRange(pieces.SkipLast(1));
        lines.Add(pieces[^1].PadRight(room + 1) + amount);
    }

    /// <summary>
    /// The money taken out of the drawer, each with what it was for, and
    /// the total the cash expected is short of the takings by.
    /// </summary>
    private static void PaidOut(List<string> lines, IReadOnlyList<PayoutDto>? payouts)
    {
        var paid = (payouts ?? Array.Empty<PayoutDto>()).Where(p => !p.IsVoided).ToList();
        if (paid.Count == 0) return;

        lines.Add("");
        lines.Add("Paid out of the drawer");

        foreach (var p in paid)
            Amount(lines, string.Format(CultureInfo.CurrentCulture, "{0:HH:mm} {1}: {2}",
                p.PaidAtLocal, p.KindName, p.Note), -p.Amount);

        lines.Add(Rule());
        Amount(lines, "Paid out", -PayoutDto.Total(paid));
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
