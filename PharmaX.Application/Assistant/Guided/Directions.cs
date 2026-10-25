using System.Globalization;
using System.Text.RegularExpressions;

namespace PharmaX.Application.Assistant.Guided;

/// <summary>
/// Directions as the dispensary types them - "1 TDS 5/7", "T2 BD x 7
/// days", "5ml TDS 5/7" - written out for the label and counted, so the
/// quantity can be checked against the course.
/// </summary>
/// <param name="Text">"Take ONE capsule THREE times a day for 5 days".</param>
/// <param name="Needed">How many units the course takes, where the dose,
/// how often and how long are all given; null otherwise (PRN, as directed).</param>
public sealed record WrittenDirections(string Text, decimal? Needed);

public static partial class Directions
{
    private static readonly (string Code, int PerDay, string Said)[] Frequencies =
    {
        ("OD", 1, "ONCE a day"), ("DAILY", 1, "ONCE a day"), ("MANE", 1, "in the MORNING"),
        ("NOCTE", 1, "at NIGHT"), ("BD", 2, "TWICE a day"), ("BID", 2, "TWICE a day"),
        ("TDS", 3, "THREE times a day"), ("TID", 3, "THREE times a day"),
        ("QID", 4, "FOUR times a day"), ("QDS", 4, "FOUR times a day"),
        ("Q4H", 6, "every 4 hours"), ("Q6H", 4, "every 6 hours"), ("Q8H", 3, "every 8 hours"),
        ("Q12H", 2, "every 12 hours")
    };

    private static readonly (string Code, string Said)[] Extras =
    {
        ("PRN", "when required"), ("STAT", "immediately"), ("AC", "before meals"), ("PC", "after meals"),
        ("AF", "after food"), ("BF", "before food"), ("PO", "by mouth"), ("UD", "as directed")
    };

    private static readonly string[] Numbers =
        { "NONE", "ONE", "TWO", "THREE", "FOUR", "FIVE", "SIX", "SEVEN", "EIGHT", "NINE", "TEN" };

    /// <param name="unit">What one dose is counted in, from the pack:
    /// "capsule", "tablet", "ml".</param>
    public static WrittenDirections Read(string typed, string unit)
    {
        var text = Regex.Replace((typed ?? "").Trim(), @"\s+", " ");
        var upper = text.ToUpperInvariant();

        // Already words, not shorthand: kept as typed.
        var known = Frequencies.Any(f => HasWord(upper, f.Code)) || Extras.Any(e => HasWord(upper, e.Code));
        if (!known) return new WrittenDirections(text, null);

        // "T1", "C2" and "1 T" carry the dose and say it is a tablet or capsule.
        var dose = 1m;
        var form = unit;
        var ml = MlDose().Match(upper);
        var coded = CodedDose().Match(upper);
        var plain = PlainDose().Match(upper);

        if (ml.Success)
        {
            dose = decimal.Parse(ml.Groups["n"].Value, CultureInfo.InvariantCulture);
            form = "ml";
        }
        else if (coded.Success)
        {
            dose = decimal.Parse(coded.Groups["n"].Value, CultureInfo.InvariantCulture);
            form = coded.Groups["f"].Value == "C" ? "capsule" : "tablet";
        }
        else if (plain.Success)
        {
            dose = decimal.Parse(plain.Groups["n"].Value, CultureInfo.InvariantCulture);
        }

        var frequency = Frequencies.FirstOrDefault(f => HasWord(upper, f.Code));
        var days = Days(upper);

        var said = form == "ml"
            ? $"Take {dose.ToString("0.##", CultureInfo.InvariantCulture)} ml"
            : $"Take {Count(dose)} {(dose > 1m ? Plural(form) : form)}";

        if (frequency.Code is not null) said += " " + frequency.Said;

        foreach (var extra in Extras.Where(e => HasWord(upper, e.Code)))
            said += " " + extra.Said;

        if (days is int d) said += d == 1 ? " for 1 day" : $" for {d} days";

        decimal? needed = frequency.Code is not null && days is int n && !HasWord(upper, "PRN")
            ? dose * frequency.PerDay * n
            : null;

        return new WrittenDirections(said, needed);
    }

    /// The unit a dose of this pack is counted in: "CAP 500" is capsules.
    public static string UnitOf(string? packSize, string description)
    {
        var text = ((packSize ?? "") + " " + description).ToUpperInvariant();

        if (Regex.IsMatch(text, @"\b(CAP|CAPS|CAPSULES?)\b")) return "capsule";
        if (Regex.IsMatch(text, @"\b(SYR|SUS|SUSP|LIQ|SYRUP|SUSPENSION|MIXTURE|ML)\b|\d+ML\b")) return "ml";
        return "tablet";
    }

    private static int? Days(string upper)
    {
        // 5/7 = 5 days, 2/52 = 2 weeks, 1/12 = 1 month.
        var slash = Course().Match(upper);
        if (slash.Success)
        {
            var n = int.Parse(slash.Groups["n"].Value, CultureInfo.InvariantCulture);
            return slash.Groups["of"].Value switch { "7" => n, "52" => n * 7, _ => n * 30 };
        }

        var words = DaysWords().Match(upper);
        if (!words.Success) return null;

        var count = int.Parse(words.Groups["n"].Value, CultureInfo.InvariantCulture);
        return words.Groups["u"].Value.StartsWith('W') ? count * 7
            : words.Groups["u"].Value.StartsWith('M') ? count * 30
            : count;
    }

    private static string Count(decimal n) =>
        n == 0.5m ? "HALF a"
        : n == Math.Floor(n) && n is >= 0 and <= 10 ? Numbers[(int)n]
        : n.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Plural(string form) => form.EndsWith('s') ? form : form + "s";

    private static bool HasWord(string upper, string word) =>
        Regex.IsMatch(upper, $@"(^|[^A-Z0-9]){Regex.Escape(word)}([^A-Z0-9]|$)");

    [GeneratedRegex(@"(?<n>\d+(\.\d+)?)\s*ML\b")]
    private static partial Regex MlDose();

    [GeneratedRegex(@"\b(?<f>[TC])\s*(?<n>\d+(\.\d+)?)\b|\b(?<n>\d+(\.\d+)?)\s*(?<f>[TC])\b")]
    private static partial Regex CodedDose();

    [GeneratedRegex(@"^(?<n>\d+(\.\d+)?)\b(?!\s*/)")]
    private static partial Regex PlainDose();

    [GeneratedRegex(@"\b(?<n>\d+)\s*/\s*(?<of>7|52|12)\b")]
    private static partial Regex Course();

    [GeneratedRegex(@"(?:X|FOR)\s*(?<n>\d+)\s*(?<u>DAYS?|D|WEEKS?|WKS?|MONTHS?)\b")]
    private static partial Regex DaysWords();
}
