using System.Globalization;
using System.Text.RegularExpressions;

namespace PharmaX.Application.Assistant.Guided;

/// <summary>
/// The active ingredient behind a common product name, as a suggestion
/// for a new product's generic name. The operator always confirms it.
/// </summary>
public static class Ingredients
{
    private static readonly (string Word, string Ingredient)[] Known =
    {
        ("vitamin c", "ascorbic acid"), ("vit c", "ascorbic acid"), ("vitamin b12", "cyanocobalamin"),
        ("vitamin b1", "thiamine"), ("vitamin d", "colecalciferol"), ("folic", "folic acid"),
        ("panado", "paracetamol"), ("paracetamol", "paracetamol"), ("calpol", "paracetamol"),
        ("disprin", "aspirin"), ("aspirin", "aspirin"), ("brufen", "ibuprofen"), ("ibuprofen", "ibuprofen"),
        ("amoxil", "amoxicillin"), ("amoxicillin", "amoxicillin"), ("augmentin", "amoxicillin and clavulanic acid"),
        ("flagyl", "metronidazole"), ("metronidazole", "metronidazole"), ("cipro", "ciprofloxacin"),
        ("bactrim", "sulfamethoxazole and trimethoprim"), ("cotrimoxazole", "sulfamethoxazole and trimethoprim"),
        ("piriton", "chlorphenamine"), ("allergex", "chlorphenamine"), ("cetirizine", "cetirizine"),
        ("loratadine", "loratadine"), ("zinc", "zinc"), ("ors", "oral rehydration salts"),
        ("omeprazole", "omeprazole"), ("gaviscon", "sodium alginate"), ("eno", "sodium bicarbonate"),
        ("amlodipine", "amlodipine"), ("metformin", "metformin"), ("hctz", "hydrochlorothiazide"),
        ("salbutamol", "salbutamol"), ("ventolin", "salbutamol"), ("iron", "ferrous sulfate"),
        ("ferrous", "ferrous sulfate"), ("calcium", "calcium carbonate"), ("multivitamin", "multivitamins")
    };

    public static string? For(string? productName)
    {
        var name = " " + Regex.Replace((productName ?? "").ToLowerInvariant(), @"[^a-z0-9]+", " ") + " ";

        // Longest first, so "vitamin b12" is not read as "vitamin b1".
        return Known.OrderByDescending(k => k.Word.Length)
            .Where(k => name.Contains(" " + k.Word + " ", StringComparison.Ordinal)
                        || name.Contains(" " + k.Word, StringComparison.Ordinal) && k.Word.Length >= 5)
            .Select(k => k.Ingredient)
            .FirstOrDefault();
    }
}

/// <summary>
/// "30 tablets", "100 ml", "TAB 30, 30": how a product is packed, in the
/// shelf-label form the catalogue uses ("TAB 30"), and the units a pack
/// holds for pricing a part pack.
/// </summary>
public static partial class PackSize
{
    private static readonly (string Word, string Label)[] Forms =
    {
        ("tablet", "TAB"), ("tabs", "TAB"), ("tab", "TAB"), ("capsule", "CAP"), ("caps", "CAP"), ("cap", "CAP"),
        ("ml", "ML"), ("syrup", "SYR"), ("syr", "SYR"), ("sachet", "SACHET"), ("g", "G"), ("gram", "G"),
        ("cream", "CREAM"), ("tube", "TUBE"), ("bottle", "BOTTLE"), ("each", "EACH"), ("piece", "EACH"),
        ("strip", "STRIP"), ("vial", "VIAL"), ("ampoule", "AMP"), ("amp", "AMP"), ("drop", "DROPS"),
        ("suppositor", "SUPP"), ("pessar", "PESS"), ("inhaler", "INHALER"), ("dose", "DOSES")
    };

    public static (string Label, decimal? Units)? Read(string answer)
    {
        var text = (answer ?? "").Trim();
        if (text.Length == 0) return null;

        var numbers = Number().Matches(text).Select(m =>
            decimal.Parse(m.Value, NumberStyles.Number, CultureInfo.InvariantCulture)).ToList();
        if (numbers.Count == 0 || numbers[0] <= 0m) return null;

        var words = Regex.Split(text.ToLowerInvariant(), @"[^a-z]+").Where(w => w.Length > 0).ToList();
        var form = Forms.FirstOrDefault(f => words.Any(w => w == f.Word || w.StartsWith(f.Word, StringComparison.Ordinal)
                                                               && f.Word.Length > 2));

        var size = numbers[0];
        var label = form.Label is null
            ? Regex.Replace(text.ToUpperInvariant(), @"\s+", " ")
            : $"{form.Label} {size.ToString("0.##", CultureInfo.InvariantCulture)}";

        // A second number is the units per pack; otherwise the pack size
        // is the units, as "30 tablets" means.
        decimal? units = numbers.Count > 1 ? numbers[1] : size;
        if (units <= 0m) units = null;

        return (label, units);
    }

    [GeneratedRegex(@"\d+(\.\d+)?")]
    private static partial Regex Number();
}

/// <summary>
/// An expiry date as given at the counter: refused when already past,
/// said out loud when close.
/// </summary>
public static class ExpiryCheck
{
    public static Reply Read(string answer, DateOnly today, int warnWithinDays, Action<DateOnly> keep)
    {
        ArgumentNullException.ThrowIfNull(keep);

        if (Answers.Date(answer) is not DateOnly expiry)
            return Reply.Retry("Enter the expiry as dd/mm/yyyy, or month and year, e.g. 03/2027.");

        if (expiry <= today)
            return Reply.Retry($"{Answers.Show(expiry)} has already passed. Stock that has expired can't be "
                              + "taken in. Check the date on the pack.");

        var days = expiry.DayNumber - today.DayNumber;

        if (days < warnWithinDays)
            return Reply.YesNo(
                $"That expires in {days} days, on {Answers.Show(expiry)}. Take it in anyway?",
                _ =>
                {
                    keep(expiry);
                    return Task.FromResult(Reply.Ok());
                },
                "Then what is the expiry date?");

        keep(expiry);
        return Reply.Ok();
    }
}
