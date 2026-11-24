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
            Amount(lines, Label(tender.TenderType), tender.Expected);

        lines.Add(Rule());
        Amount(lines, "Total", read.GrandTotal);

        PaidOut(lines, payouts);
        SalesTally(lines, session.OpeningFloat,
            read.Expectations.Select(e => (e.TenderType, e.Expected, (decimal?)null)).ToList(),
            PayoutDto.Total(payouts), counted: false);

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
            var name = Label(line.TenderType);
            Amount(lines, name, line.Expected);

            // A tender that was counted as expected is one line. Declared
            // and variance are only worth a line when they differ.
            if (line.Variance == 0m) continue;

            Amount(lines, "Declared", line.Declared, indent: 2);
            Amount(lines, "Variance", line.Variance, indent: 2);
        }

        lines.Add(Rule());
        Amount(lines, "DECLARED", declaration.TotalDeclared);
        Amount(lines, "VARIANCE", declaration.TotalVariance);

        PaidOut(lines, payouts);
        SalesTally(lines, session.OpeningFloat,
            declaration.Variances
                .Select(v => (v.TenderType, v.Expected, (decimal?)v.Declared))
                .ToList(),
            PayoutDto.Total(payouts), counted: true);

        if (!string.IsNullOrWhiteSpace(varianceReason))
        {
            lines.Add("");
            lines.Add($"Variance reason: {varianceReason}");
        }

        lines.Add("");
        lines.Add("Session closed.");

        return new PrintableDocument("Z-read", lines, CashUpSlip.Width);
    }

    /// <summary>
    /// Cash, even at zero, then every other tender that has an amount.
    /// A zero EcoCash, card or medical-aid line is left off: it only
    /// lengthens the roll.
    /// </summary>
    private static List<T> Statements<T>(IReadOnlyList<T> lines) where T : class
    {
        static string Name(T row) => row switch
        {
            VarianceLineDto variance => variance.TenderType,
            TenderExpectationDto expected => expected.TenderType,
            _ => ""
        };

        static bool HasAmount(T row) => row switch
        {
            VarianceLineDto variance => variance.TenderType == nameof(TenderType.Cash)
                || variance.Expected != 0m || variance.Declared != 0m || variance.Variance != 0m,
            TenderExpectationDto expected => expected.TenderType == nameof(TenderType.Cash)
                || expected.Expected != 0m,
            _ => true
        };

        var all = lines.Where(HasAmount).ToList();

        if (!all.Exists(row => Name(row) == nameof(TenderType.Cash)))
        {
            object blank = typeof(T) == typeof(VarianceLineDto)
                ? new VarianceLineDto(nameof(TenderType.Cash), 0m, 0m, 0m, false)
                : new TenderExpectationDto(nameof(TenderType.Cash), 0m, 0);
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

    private static void Amount(List<string> lines, string label, decimal value, int indent = 0)
    {
        var amount = value.ToString("N2", CultureInfo.CurrentCulture);
        var room = CashUpSlip.Width - amount.Length - 1 - indent;
        var pieces = SlipLayout.Wrap(label, Math.Max(1, room), Math.Max(1, room));
        var pad = new string(' ', indent);
        lines.AddRange(pieces.SkipLast(1).Select(piece => pad + piece));
        lines.Add(pad + pieces[^1].PadRight(room + 1) + amount);
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

        Amount(lines, "Paid out", -PayoutDto.Total(paid));
    }

    /// <summary>
    /// The shift's sales, in the same figure Daily Sales and Sales by day
    /// use for this till.
    /// </summary>
    /// <remarks>
    /// Cash expected is the drawer: the float is still in it, and drawings
    /// and expenses have already left it. Taking the float out and adding
    /// the paid-out money back leaves what was received for sales. The
    /// other tenders never sat in the drawer, so they are already that
    /// figure. Counted runs the same sum on what was declared, and the
    /// difference is the variance.
    /// </remarks>
    private static void SalesTally(List<string> lines, decimal openingFloat,
        IReadOnlyList<(string Tender, decimal Expected, decimal? Declared)> taken,
        decimal paidOut, bool counted)
    {
        var rows = taken.ToList();

        if (!rows.Exists(row => row.Tender == nameof(TenderType.Cash)))
            rows.Add((nameof(TenderType.Cash), openingFloat - paidOut,
                counted ? openingFloat - paidOut : null));

        rows.Sort((a, b) => Rank(a.Tender).CompareTo(Rank(b.Tender)));

        lines.Add("");
        lines.Add("Sales tally");

        var system = 0m;
        var declaredSales = 0m;

        foreach (var row in rows)
        {
            var cash = row.Tender == nameof(TenderType.Cash);
            var received = cash ? row.Expected - openingFloat + paidOut : row.Expected;
            var countedTender = cash
                ? (row.Declared ?? row.Expected) - openingFloat + paidOut
                : row.Declared ?? row.Expected;

            // A tender with nothing on it is not printed. Its zero still
            // adds nothing to the sales total.
            if (!cash && received == 0m && countedTender == 0m)
            {
                system += received;
                if (counted) declaredSales += row.Declared ?? 0m;
                continue;
            }

            // The amount on the tender name is what adds into the sales
            // total. The float and the drawings sit under cash, because
            // they explain that figure and are not added again.
            Amount(lines, cash ? "Cash sales" : Label(row.Tender), received);
            system += received;

            if (cash && openingFloat != 0m) Amount(lines, "Less float", -openingFloat, indent: 2);
            if (cash && paidOut != 0m) Amount(lines, "Add paid out", paidOut, indent: 2);

            if (counted && row.Declared is not null)
            {
                declaredSales += countedTender;
                if (countedTender != received)
                    Amount(lines, "Counted", countedTender, indent: 2);
            }
        }

        lines.Add(Rule());
        Amount(lines, "SALES TOTAL", system);

        if (counted && declaredSales != system)
        {
            Amount(lines, "COUNTED", declaredSales);
            Amount(lines, "DIFFERENCE", declaredSales - system);
        }

        lines.Add("Matches Daily Sales.");
    }

    private static int Rank(string tender) => tender switch
    {
        "Cash" => 0,
        "EcoCash" => 1,
        "Card" => 2,
        _ => 3
    };

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
