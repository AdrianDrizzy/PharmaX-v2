using System.Globalization;
using System.Text.RegularExpressions;

namespace PharmaX.Application.Dispensing;

/// <summary>
/// What a dispensing label says in words: the directions with the dose
/// and how often written out ("Take TWO tablets THREE times a day after
/// meals"), the warnings that go with the medicine, and the line every
/// label carries.
/// </summary>
/// <remarks>
/// <para>
/// Applied where the label is drawn, so a label printed at the issue, a
/// reprint, a to-follow and a pre-pack all read the same way, including
/// reprints of dispensings recorded before the wording changed.
/// </para>
/// <para>
/// The warnings are chosen from the medicine's own name - the RSI
/// catalogue names most products by what is in them ("AMOXYCILLIN
/// CAPSULES 500MG", "CHLORPHENIRAMINE (VARICHEM) 4MG"), and the common
/// brands are listed beside their ingredient. A medicine that matches
/// none of them still carries a general warning, so no label goes out
/// without one.
/// </para>
/// </remarks>
public static class LabelWording
{
    /// Printed on every label.
    public const string KeepOutOfReach = "Keep out of reach of children.";

    /// For a medicine none of the rules below speaks to.
    public const string General = "Use only as directed. Do not share this medicine.";

    /// The most a label holds before the stock runs out of room.
    public const int MostWarnings = 3;

    // ---- Directions ------------------------------------------------------

    private static readonly string[] NumberWords =
    {
        "", "ONE", "TWO", "THREE", "FOUR", "FIVE", "SIX", "SEVEN", "EIGHT",
        "NINE", "TEN", "ELEVEN", "TWELVE"
    };

    private const string Number =
        @"\d+(?:\.\d+)?|½|1/2|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve";

    // Singular and plural of each unit a dose is counted in.
    private static readonly Dictionary<string, (string One, string Many)> Units =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["tablet"] = ("tablet", "tablets"), ["tab"] = ("tablet", "tablets"),
            ["capsule"] = ("capsule", "capsules"), ["cap"] = ("capsule", "capsules"),
            ["drop"] = ("drop", "drops"), ["puff"] = ("puff", "puffs"),
            ["spray"] = ("spray", "sprays"), ["sachet"] = ("sachet", "sachets"),
            ["suppository"] = ("suppository", "suppositories"),
            ["pessary"] = ("pessary", "pessaries"), ["lozenge"] = ("lozenge", "lozenges"),
            ["patch"] = ("patch", "patches"), ["teaspoon"] = ("teaspoon", "teaspoons"),
            ["teaspoonful"] = ("teaspoonful", "teaspoonfuls"),
            ["spoonful"] = ("spoonful", "spoonfuls"), ["measure"] = ("measure", "measures"),
            ["ampoule"] = ("ampoule", "ampoules"), ["application"] = ("application", "applications"),
            ["dose"] = ("dose", "doses"), ["inhalation"] = ("inhalation", "inhalations")
        };

    // Swallowed, so the directions start "Take"; anything else is "Use".
    private static readonly HashSet<string> Swallowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "tablet", "tablets", "capsule", "capsules", "sachet", "sachets", "lozenge",
        "lozenges", "teaspoon", "teaspoons", "teaspoonful", "teaspoonfuls",
        "spoonful", "spoonfuls", "measure", "measures", "dose", "doses", "ml", "mls"
    };

    private static readonly Regex Dose = new(
        $@"\b(?<n>{Number})(?:\s*(?:-|to)\s*(?<m>{Number}))?\s+(?<unit>tablets?|tabs?|capsules?|caps?|drops?|puffs?|sprays?|sachets?|suppositor(?:y|ies)|pessar(?:y|ies)|lozenges?|patch(?:es)?|teaspoonfuls?|teaspoons?|spoonfuls?|measures?|ampoules?|applications?|doses?|inhalations?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Times = new(
        $@"\b(?<n>{Number})\s+times\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex OnceTwice = new(
        @"\b(once|twice|thrice)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex EveryHours = new(
        $@"\bevery\s+(?<n>{Number})\s+hours?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Leading = new(
        $@"^(?<n>{Number})(?:\s*(?:-|to)\s*(?:{Number}))?\s+(?<unit>\S+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// The directions in plain words, the dose and how often in capitals:
    /// "take 2 tablets three times a day after meals" prints as "Take TWO
    /// tablets THREE times a day after meals". A volume stays in figures
    /// ("10 ml"), as a measuring spoon is marked.
    /// </summary>
    public static string Directions(string? directions)
    {
        if (string.IsNullOrWhiteSpace(directions)) return "";

        // "½" is no letter or digit, so it is matched as "1/2".
        var text = Regex.Replace(directions.Trim(), @"\s+", " ").Replace("½", "1/2");

        // Directions that open on the dose get the verb a patient needs.
        var leading = Leading.Match(text);

        if (leading.Success)
        {
            var unit = leading.Groups["unit"].Value.TrimEnd('.', ',');
            var verb = Swallowed.Contains(unit)
                       || Units.TryGetValue(unit.TrimEnd('s'), out var u) && Swallowed.Contains(u.One)
                ? "Take"
                : "Use";
            text = $"{verb} {text}";
        }

        text = Dose.Replace(text, m =>
        {
            var n = Words(m.Groups["n"].Value, out var count);
            var many = count > 1m;
            var range = "";

            if (m.Groups["m"].Success)
            {
                range = " to " + Words(m.Groups["m"].Value, out _);
                many = true;
            }

            var unit = UnitOf(m.Groups["unit"].Value);
            return $"{n}{range} {(many ? unit.Many : unit.One)}";
        });

        text = Times.Replace(text, m => $"{Words(m.Groups["n"].Value, out _)} times");
        text = OnceTwice.Replace(text, m => m.Value.ToUpperInvariant());
        text = EveryHours.Replace(text, m =>
        {
            var n = Words(m.Groups["n"].Value, out var count);
            return $"every {n} {(count == 1m ? "hour" : "hours")}";
        });

        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static (string One, string Many) UnitOf(string word)
    {
        var key = word.ToLowerInvariant();
        if (Units.TryGetValue(key, out var unit)) return unit;

        // A plural: drop the ending to find the unit it counts.
        foreach (var (singular, forms) in Units)
            if (string.Equals(forms.Many, key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(singular + "s", key, StringComparison.OrdinalIgnoreCase))
                return forms;

        return (word, word);
    }

    /// <summary>A count in capital words, with its value for the plural.</summary>
    private static string Words(string number, out decimal value)
    {
        var text = number.Trim().ToLowerInvariant();

        if (text is "½" or "1/2" or "0.5")
        {
            value = 0.5m;
            return "HALF a";
        }

        var index = Array.FindIndex(NumberWords,
            w => w.Length > 0 && string.Equals(w, text, StringComparison.OrdinalIgnoreCase));

        if (index > 0)
        {
            value = index;
            return NumberWords[index];
        }

        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
            return number;

        if (value == 1.5m) return "ONE AND A HALF";

        return value == Math.Floor(value) && value >= 1m && value < NumberWords.Length
            ? NumberWords[(int)value]
            : number;
    }

    // ---- Warnings --------------------------------------------------------

    private sealed record Rule(string Warning, string[] Words, bool Topical = false,
                               bool SwallowedOnly = false);

    // In the order a patient most needs them: a label holds three.
    private static readonly Rule[] Rules =
    {
        new("Do not drink alcohol while taking this, or for 2 days after.",
            new[] { "metronidazole", "tinidazole", "flagyl" }),
        new("May cause drowsiness. Do not drive or use machines. Avoid alcohol.",
            new[] { "chlorphen", "promethazine", "phenergan", "hydroxyzine", "cyproheptadine",
                    "periactin", "diphenhydramine", "benylin original", "cyclizine", "valoid",
                    "meclizine", "dimenhydrinate", "allergex", "cetirizine", "pholcodine", "pcd ",
                    "diazepam", "lorazepam", "alprazolam", "bromazepam", "clonazepam", "nitrazepam",
                    "temazepam", "zolpidem", "codeine", "tramadol", "morphine", "pethidine",
                    "amitriptyline", "imipramine", "clomipramine", "trazodone", "mirtazapine",
                    "olanzapine", "quetiapine", "chlorpromazine", "haloperidol", "carbamazepine",
                    "phenobarb", "gabapentin", "pregabalin", "baclofen", "orphenadrine" },
            SwallowedOnly: true),
        new("For the eyes only.", new[] { " eye ", "eye drop", "eye oint", "ophthalmic", "opthalmic" }, Topical: true),
        new("For the ears only.", new[] { " ear ", "ear drop", "otic" }, Topical: true),
        new("For the nose only.", new[] { "nasal" }, Topical: true),
        new("For external use only. Do not swallow.",
            new[] { "cream", "ointment", " oint", "lotion", "shampoo", "soap", "liniment",
                    "dusting powder", " gel", "emulgel", "antiseptic", "varicide", "betadine",
                    "povidone", "surgical spirit", "calamine" },
            Topical: true),
        new("Finish the whole course, even if you feel better.",
            new[] { "cillin", "amox", "clav", "cledomox", "augmentin", "cefalexin", "cephalexin",
                    "cefuroxime", "cefixime", "cefaclor", "cefadroxil", "ceftriaxone",
                    "azithromycin", "erythromycin", "clarithromycin", "clindamycin",
                    "doxycycline", "tetracycline", "minocycline", "floxacin", "metronidazole",
                    "tinidazole", "flagyl", "co-trimoxazole", "cotrimoxazole", "bactrim",
                    "sulfamethoxazole", "nitrofurantoin", "aciclovir", "acyclovir" }),
        new("Do not take with milk, antacids or iron at the same time.",
            new[] { "doxycycline", "tetracycline", "minocycline", "floxacin" },
            SwallowedOnly: true),
        new("Take with or after food.",
            new[] { "ibuprofen", "brufen", "nurofen", "diclofenac", "voltaren", "naproxen",
                    "mefenamic", "ponstan", "indomethacin", "indometacin", "piroxicam",
                    "meloxicam", "ketoprofen", "aspirin", "asprin", "disprin", "prednisolone",
                    "prednisone", "dexamethasone", "metformin" },
            SwallowedOnly: true),
        new("Contains paracetamol. Do not take other medicines that contain paracetamol.",
            new[] { "paracetamol", "panado", "panadol", "calpol", "flucor" },
            SwallowedOnly: true),
        new("Do not take aspirin or anti-inflammatory painkillers without advice.",
            new[] { "warfarin", "coumadin" }),
        new("Shake the bottle well before use.",
            new[] { " susp", "suspension", "mixture", "emulsion", "/5ml", "/ 5ml" })
    };

    // An oral gel for teething is swallowed, whatever "gel" says.
    private static readonly string[] Oral = { "teejel", "teething", "oral gel" };

    /// <summary>
    /// The warnings for a medicine, most important first and no more than
    /// a label holds; the general warning when none applies.
    /// </summary>
    public static IReadOnlyList<string> Warnings(string? description, string? genericName = null)
    {
        // Padded and lower-cased so " eye " and " gel" match whole words
        // at either end of the name as well as inside it.
        var name = $" {description} {genericName} "
            .ToLowerInvariant()
            .Replace('(', ' ').Replace(')', ' ').Replace(',', ' ');

        var oral = Oral.Any(name.Contains);
        var topical = !oral && Rules.Any(r => r.Topical && r.Words.Any(name.Contains));

        var warnings = Rules
            .Where(r => r.Words.Any(name.Contains))
            .Where(r => !(r.Topical && oral))
            .Where(r => !(r.SwallowedOnly && topical))
            .Select(r => r.Warning)
            .Distinct()
            .Take(MostWarnings)
            .ToList();

        // The external-use line says the same as the eye, ear or nose one.
        if (warnings.Count(w => w.StartsWith("For ", StringComparison.Ordinal)) > 1)
            warnings.Remove("For external use only. Do not swallow.");

        return warnings.Count > 0 ? warnings : new[] { General };
    }
}
