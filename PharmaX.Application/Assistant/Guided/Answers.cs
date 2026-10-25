using System.Globalization;
using System.Text.RegularExpressions;

namespace PharmaX.Application.Assistant.Guided;

/// <summary>
/// Reads the short answers a guided job gets: yes, no, skip, numbers,
/// money, dates, and a choice from a list by its number or its name.
/// </summary>
public static partial class Answers
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static readonly string[] Yes =
        { "y", "yes", "yeah", "yep", "ok", "okay", "sure", "correct", "right", "go ahead", "do it",
          "save", "save it", "confirm", "yes please" };

    private static readonly string[] No = { "n", "no", "nope", "not now", "no thanks", "don't", "dont" };

    private static readonly string[] Skip =
        { "", "skip", "none", "no", "n", "n/a", "na", "-", "nothing", "none known", "nil", "not known",
          "unknown", "don't know", "dont know" };

    private static readonly string[] Cancel = { "cancel", "stop", "quit", "abort", "never mind", "nevermind" };

    public static bool IsYes(string answer) => Yes.Contains(Plain(answer));

    public static bool IsNo(string answer) => No.Contains(Plain(answer));

    /// Nothing to say: Enter, "skip", "none".
    public static bool IsSkip(string answer) => Skip.Contains(Plain(answer));

    public static bool IsCancel(string answer) => Cancel.Contains(Plain(answer));

    private static string Plain(string answer) =>
        Regex.Replace((answer ?? "").Trim().TrimEnd('.', '!').ToLowerInvariant(), @"\s+", " ");

    public static IReadOnlyList<string> Words(string answer) =>
        Regex.Split(Plain(answer), @"[^\p{L}\p{N}]+").Where(w => w.Length > 0).ToList();

    /// A number, with or without thousands separators: "1,200", "30".
    public static decimal? Number(string answer)
    {
        var match = NumberPattern().Match(answer ?? "");
        return match.Success
               && decimal.TryParse(match.Value.Replace(",", "", StringComparison.Ordinal),
                   NumberStyles.Number, Invariant, out var n)
            ? n
            : null;
    }

    /// An amount of money, zero or more: "$12.50", "12.5".
    public static decimal? Money(string answer)
    {
        var text = (answer ?? "").Replace("$", "", StringComparison.Ordinal)
            .Replace("USD", "", StringComparison.OrdinalIgnoreCase).Trim();
        return Number(text) is decimal n && n >= 0m ? Math.Round(n, 2) : null;
    }

    private static readonly string[] DateFormats =
    {
        "d/M/yyyy", "d/M/yy", "d-M-yyyy", "d-M-yy", "d.M.yyyy", "yyyy-M-d", "d MMM yyyy", "d MMMM yyyy",
        "MMM yyyy", "MMMM yyyy", "M/yyyy", "MM/yy"
    };

    /// <summary>
    /// A date as the counter writes it, day first: 12/03/1985, 12 March
    /// 1985, 2027-03-31. A month and year alone ("03/2027", "Mar 2027") is
    /// the last day of that month, as an expiry printed that way means.
    /// </summary>
    public static DateOnly? Date(string answer)
    {
        var text = Regex.Replace((answer ?? "").Trim(), @"\s+", " ");
        if (text.Length == 0) return null;

        foreach (var format in DateFormats)
        {
            if (!DateTime.TryParseExact(text, format, Invariant, DateTimeStyles.None, out var parsed))
                continue;

            var date = DateOnly.FromDateTime(parsed);

            return format.Contains('d', StringComparison.Ordinal)
                ? date
                : new DateOnly(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));
        }

        return null;
    }

    /// "38 years old", "38 yrs", "aged 38", "6 months": the age said instead
    /// of the date of birth. Years and months.
    public static (int Years, int Months)? Age(string answer)
    {
        var match = AgePattern().Match(answer ?? "");
        if (!match.Success) return null;

        var n = int.Parse(match.Groups["n"].Value, Invariant);
        return match.Groups["unit"].Value.StartsWith("m", StringComparison.OrdinalIgnoreCase) ? (0, n) : (n, 0);
    }

    /// <summary>
    /// The one item an answer picks: its number in the list, or the words
    /// of its name. Null where none, or more than one, fits.
    /// </summary>
    public static T? Pick<T>(string answer, IReadOnlyList<T> items, Func<T, string> name) where T : class
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(name);

        var text = (answer ?? "").Trim();
        if (int.TryParse(text.TrimEnd('.'), NumberStyles.Integer, Invariant, out var n))
            return n >= 1 && n <= items.Count ? items[n - 1] : null;

        var exact = items.Where(i => string.Equals(name(i).Trim(), text, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (exact.Count == 1) return exact[0];

        var words = Words(text);
        if (words.Count == 0) return null;

        var fits = items.Where(i =>
        {
            var have = Words(name(i));
            return words.All(w => have.Any(h => h.StartsWith(w, StringComparison.Ordinal)));
        }).ToList();

        return fits.Count == 1 ? fits[0] : null;
    }

    /// <summary>
    /// A bare whole number given while a numbered list is showing, that is
    /// not on it: a slip, never a product code to look up.
    /// </summary>
    public static bool IsOffList(string answer, int listed) =>
        listed > 0
        && int.TryParse((answer ?? "").Trim().TrimEnd('.'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
        && (n < 1 || n > listed);

    /// A numbered list, one item to a line.
    public static string Numbered<T>(IEnumerable<T> items, Func<T, string> line) =>
        string.Join(Environment.NewLine, items.Select((item, i) => $"{i + 1}. {line(item)}"));

    /// A list said as a sentence: "a, b and c".
    public static IReadOnlyList<string> List(string answer) =>
        IsSkip(answer)
            ? Array.Empty<string>()
            : Regex.Split(answer.Trim(), @"\s*(?:,|;|\band\b|\n)\s*", RegexOptions.IgnoreCase)
                .Select(s => s.Trim().TrimEnd('.'))
                .Where(s => s.Length > 0 && !IsSkip(s))
                .ToList();

    public static string Show(DateOnly date) => date.ToString("dd/MM/yyyy", Invariant);

    public static string Show(decimal amount) => amount.ToString("N2", Invariant);

    public static string Quantity(decimal quantity) => quantity.ToString("#,0.##", Invariant);

    // Not digits run on from letters: "PN2611" is a batch, not 2611.
    [GeneratedRegex(@"(?<![A-WYZa-wyz0-9.,])-?\d[\d,]*(\.\d+)?")]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"^\s*(aged?\s+)?(?<n>\d{1,3})\s*(?<unit>y|yr|yrs|year|years|m|mo|mos|month|months)\b(\s*old)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AgePattern();
}
