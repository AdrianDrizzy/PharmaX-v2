using System.Globalization;
using System.Text;

namespace PharmaX.Domain.Patients;

/// <summary>How a recorded allergy relates to the medicine in hand.</summary>
public enum AllergyMatchKind
{
    /// The allergy names the medicine, or an ingredient in it.
    Direct = 1,

    /// The allergy names a drug class the medicine belongs to - a
    /// penicillin allergy against amoxicillin - or another member of the
    /// same class.
    SameClass = 2,

    /// A related class with a known, smaller cross-sensitivity - a
    /// penicillin allergy against a cephalosporin. Worth a pharmacist's
    /// look, not a hard stop.
    CrossSensitivity = 3
}

/// <summary>One recorded allergy that the medicine in hand runs into.</summary>
public sealed record AllergyMatch(Allergy Allergy, AllergyMatchKind Kind,
                                  string Explanation)
{
    public bool IsHardStop => Kind != AllergyMatchKind.CrossSensitivity;
}

/// <summary>
/// Checks a medicine against a patient's recorded allergies by what the
/// medicine is, not by how its name happens to be spelt.
/// </summary>
/// <remarks>
/// The check this replaces compared the allergy text with the product
/// description as two strings. "Penicillin" is not a substring of
/// "Amoxicillin 500mg capsules 21s", so a penicillin-allergic patient was
/// handed amoxicillin with no warning at all (DEF-0006). Allergies are
/// recorded the way people say them - a class ("penicillin", "sulfa"), a
/// substance ("amoxicillin") or a brand ("Augmentin") - and a product is
/// described the way a wholesaler lists it, so the screen has to know
/// which ingredients belong to which class and which brands contain what.
///
/// The table is deliberately conservative and small: the classes behind
/// the great majority of recorded drug allergies at a community
/// pharmacy. A product whose generic name is filled in is matched on that
/// as well as its description, so extending the catalogue's generic names
/// extends the screen without a code change. Anything the table does not
/// know still falls back to matching the allergy's own words against the
/// product, which is what the old check did.
/// </remarks>
public static class AllergyScreen
{
    private sealed record DrugClass(string Name, string[] Aliases,
                                    string[] Members);

    private static readonly DrugClass[] Classes =
    {
        new("penicillin",
            new[] { "penicillin", "penicillins", "pcn", "penicilin" },
            new[]
            {
                "penicillin", "amoxicillin", "amoxycillin", "ampicillin",
                "flucloxacillin", "cloxacillin", "dicloxacillin",
                "benzylpenicillin", "phenoxymethylpenicillin",
                "benzathine", "piperacillin", "ticarcillin", "co-amoxiclav",
                "coamoxiclav", "pivampicillin", "bacampicillin", "temocillin"
            }),

        new("cephalosporin",
            new[] { "cephalosporin", "cephalosporins", "cephalosporine" },
            new[]
            {
                "cefalexin", "cephalexin", "cefadroxil", "cefaclor",
                "cefuroxime", "cefprozil", "cefixime", "cefpodoxime",
                "ceftriaxone", "cefotaxime", "ceftazidime", "cefazolin",
                "cephazolin", "cefepime", "cefoxitin", "cefradine",
                "cephradine", "ceftaroline", "cefdinir"
            }),

        new("carbapenem",
            new[] { "carbapenem", "carbapenems" },
            new[] { "imipenem", "meropenem", "ertapenem", "doripenem" }),

        new("sulfonamide",
            new[]
            {
                "sulfa", "sulpha", "sulfonamide", "sulfonamides",
                "sulphonamide", "sulphonamides", "sulfur drugs"
            },
            new[]
            {
                "sulfamethoxazole", "sulphamethoxazole", "co-trimoxazole",
                "cotrimoxazole", "sulfasalazine", "sulphasalazine",
                "sulfadiazine", "sulphadiazine", "sulfacetamide",
                "sulfadoxine"
            }),

        new("macrolide",
            new[] { "macrolide", "macrolides" },
            new[]
            {
                "erythromycin", "azithromycin", "clarithromycin",
                "roxithromycin"
            }),

        new("tetracycline",
            new[] { "tetracycline", "tetracyclines" },
            new[]
            {
                "tetracycline", "doxycycline", "minocycline",
                "oxytetracycline", "lymecycline"
            }),

        new("fluoroquinolone",
            new[]
            {
                "quinolone", "quinolones", "fluoroquinolone",
                "fluoroquinolones"
            },
            new[]
            {
                "ciprofloxacin", "levofloxacin", "moxifloxacin",
                "ofloxacin", "norfloxacin"
            }),

        new("aminoglycoside",
            new[] { "aminoglycoside", "aminoglycosides" },
            new[]
            {
                "gentamicin", "amikacin", "tobramycin", "neomycin",
                "streptomycin"
            }),

        new("nsaid",
            new[]
            {
                "nsaid", "nsaids", "anti-inflammatory",
                "anti-inflammatories", "non-steroidal"
            },
            new[]
            {
                "ibuprofen", "diclofenac", "naproxen", "aspirin",
                "acetylsalicylic", "mefenamic", "indomethacin",
                "indometacin", "ketoprofen", "piroxicam", "meloxicam",
                "celecoxib", "etoricoxib", "ketorolac", "flurbiprofen"
            }),

        new("salicylate",
            new[] { "salicylate", "salicylates", "aspirin" },
            new[] { "aspirin", "acetylsalicylic", "salicylic", "salsalate" }),

        new("opioid",
            new[] { "opioid", "opioids", "opiate", "opiates", "narcotic" },
            new[]
            {
                "codeine", "morphine", "tramadol", "oxycodone",
                "hydrocodone", "dihydrocodeine", "pethidine", "fentanyl",
                "tapentadol", "methadone", "buprenorphine"
            }),

        new("ace inhibitor",
            new[] { "ace inhibitor", "ace inhibitors", "ace-inhibitor" },
            new[]
            {
                "enalapril", "lisinopril", "perindopril", "captopril",
                "ramipril", "quinapril", "fosinopril"
            }),

        new("statin",
            new[] { "statin", "statins" },
            new[]
            {
                "simvastatin", "atorvastatin", "rosuvastatin",
                "pravastatin", "fluvastatin", "lovastatin"
            })
    };

    /// <summary>
    /// Brand names that are not self-describing, mapped to what is in
    /// them. The ones patients most often give as "what I'm allergic to".
    /// </summary>
    private static readonly Dictionary<string, string[]> Brands =
        new(StringComparer.Ordinal)
        {
            ["amoxil"] = new[] { "amoxicillin" },
            ["augmentin"] = new[] { "amoxicillin", "clavulanate" },
            ["moxypen"] = new[] { "amoxicillin" },
            ["betamox"] = new[] { "amoxicillin" },
            ["curam"] = new[] { "amoxicillin", "clavulanate" },
            ["augmaxcil"] = new[] { "amoxicillin", "clavulanate" },
            ["keflex"] = new[] { "cefalexin" },
            ["zinnat"] = new[] { "cefuroxime" },
            ["bactrim"] = new[] { "sulfamethoxazole", "trimethoprim" },
            ["septran"] = new[] { "sulfamethoxazole", "trimethoprim" },
            ["disprin"] = new[] { "aspirin" },
            ["ecotrin"] = new[] { "aspirin" },
            ["grandpa"] = new[] { "aspirin" },
            ["myprodol"] = new[] { "ibuprofen", "codeine", "paracetamol" },
            ["nurofen"] = new[] { "ibuprofen" },
            ["brufen"] = new[] { "ibuprofen" },
            ["voltaren"] = new[] { "diclofenac" },
            ["cataflam"] = new[] { "diclofenac" },
            ["naprosyn"] = new[] { "naproxen" },
            ["ponstan"] = new[] { "mefenamic" },
            ["zithromax"] = new[] { "azithromycin" },
            ["klacid"] = new[] { "clarithromycin" },
            ["cipro"] = new[] { "ciprofloxacin" },
            ["ciprobay"] = new[] { "ciprofloxacin" },
            ["tramal"] = new[] { "tramadol" },
            ["panado"] = new[] { "paracetamol" },
            ["lipitor"] = new[] { "atorvastatin" },
            ["crestor"] = new[] { "rosuvastatin" }
        };

    /// <summary>
    /// Dosage forms and packing words, which say nothing about what is in
    /// a product.
    /// </summary>
    private static readonly HashSet<string> Forms = new(StringComparer.Ordinal)
    {
        "tablet", "tablets", "capsule", "capsules", "caplet", "caplets",
        "syrup", "suspension", "solution", "mixture", "elixir", "drops",
        "cream", "creams", "ointment", "lotion", "powder", "sachet",
        "sachets", "lozenge", "lozenges", "spray", "inhaler", "injection",
        "injections", "ampoule", "ampoules", "vial", "vials", "patch",
        "patches", "effervescent", "refill", "assorted", "chewable",
        "dispersible", "soluble", "tabs", "caps", "strength", "extra",
        "forte", "junior", "paediatric", "adult", "infant", "sugar", "free"
    };

    /// <summary>
    /// Which classes carry a known cross-sensitivity to which.
    /// </summary>
    private static readonly (string From, string To)[] CrossSensitive =
    {
        ("penicillin", "cephalosporin"),
        ("penicillin", "carbapenem"),
        ("cephalosporin", "penicillin"),
        ("cephalosporin", "carbapenem")
    };

    /// <summary>
    /// Every recorded allergy the medicine runs into, strongest match
    /// first. Empty where there is nothing to warn about.
    /// </summary>
    /// <param name="allergies">The patient's recorded allergies.</param>
    /// <param name="description">The product as it is listed.</param>
    /// <param name="genericName">
    /// The active ingredient, where the catalogue has it.
    /// </param>
    public static IReadOnlyList<AllergyMatch> Check(
        IEnumerable<Allergy> allergies, string description,
        string? genericName = null)
    {
        ArgumentNullException.ThrowIfNull(allergies);

        var product = Words(description + " " + genericName);
        var ingredients = IngredientsOf(product);
        var productClasses = Classes
            .Where(c => c.Members.Any(ingredients.Contains))
            .ToList();

        var matches = new List<AllergyMatch>();

        foreach (var allergy in allergies)
        {
            if (string.IsNullOrWhiteSpace(allergy.Substance)) continue;

            var match = Match(allergy, product, ingredients, productClasses,
                              description);

            if (match is not null) matches.Add(match);
        }

        return matches.OrderBy(m => m.Kind).ToList();
    }

    private static AllergyMatch? Match(Allergy allergy, string product,
        HashSet<string> ingredients, List<DrugClass> productClasses,
        string description)
    {
        var said = Words(allergy.Substance);
        var named = IngredientsOf(said);

        // What the allergy says, word for word, in the product - the old
        // check, kept for everything the tables do not know about (a
        // latex glove, a peanut-oil ointment).
        if (said.Trim().Length >= 3 && ContainsPhrase(product, said))
            return new AllergyMatch(allergy, AllergyMatchKind.Direct,
                $"Patient is recorded as allergic to {allergy.Substance}. "
                + $"{description} contains it.");

        // An ingredient the allergy names, or that its brand contains.
        var shared = named.Intersect(ingredients, StringComparer.Ordinal)
                          .FirstOrDefault();

        if (shared is not null)
            return new AllergyMatch(allergy, AllergyMatchKind.Direct,
                $"Patient is recorded as allergic to {allergy.Substance}. "
                + $"{description} contains {shared}.");

        // A class the allergy names outright, or a class one of the
        // allergy's own ingredients belongs to.
        var allergyClasses = Classes
            .Where(c => c.Aliases.Any(a => ContainsPhrase(said, a))
                        || c.Members.Any(named.Contains))
            .ToList();

        var sameClass = productClasses
            .FirstOrDefault(c => allergyClasses.Contains(c));

        if (sameClass is not null)
            return new AllergyMatch(allergy, AllergyMatchKind.SameClass,
                $"Patient is recorded as allergic to {allergy.Substance}. "
                + $"{description} is a {sameClass.Name} "
                + $"({string.Join(", ", sameClass.Members.Where(ingredients.Contains))}).");

        var related = CrossSensitive
            .Where(x => allergyClasses.Exists(c => c.Name == x.From))
            .Select(x => productClasses.Find(c => c.Name == x.To))
            .FirstOrDefault(c => c is not null);

        if (related is not null)
            return new AllergyMatch(allergy, AllergyMatchKind.CrossSensitivity,
                $"Patient is recorded as allergic to {allergy.Substance}. "
                + $"{description} is a {related.Name}, which carries a "
                + "known cross-sensitivity. Check before issuing.");

        return null;
    }

    /// <summary>
    /// The ingredients a piece of text mentions: class members named
    /// directly, and those behind any brand it names.
    /// </summary>
    private static HashSet<string> IngredientsOf(string words)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (var member in Classes.SelectMany(c => c.Members))
        {
            if (ContainsPhrase(words, member)) found.Add(member);
        }

        foreach (var (brand, contents) in Brands)
        {
            if (!ContainsPhrase(words, brand)) continue;
            foreach (var ingredient in contents) found.Add(ingredient);
        }

        // Every other word is a candidate ingredient too, so that a
        // generic name the tables do not list still matches itself -
        // but not the words every product shares, or a "penicillin
        // tablets" allergy would stop every tablet in the shop.
        foreach (var word in words.Split(' ',
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.Length >= 5 && !char.IsDigit(word[0])
                && !Forms.Contains(word))
                found.Add(word);
        }

        return found;
    }

    /// <summary>
    /// Lower case, accents and punctuation gone, single spaces, and
    /// padded so that a phrase can be found as whole words.
    /// </summary>
    private static string Words(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return " ";

        var builder = new StringBuilder(text.Length + 2).Append(' ');

        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c)
                == UnicodeCategory.NonSpacingMark) continue;

            builder.Append(char.IsLetterOrDigit(c) || c == '-'
                ? char.ToLowerInvariant(c)
                : ' ');
        }

        var collapsed = string.Join(' ', builder.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));

        return " " + collapsed + " ";
    }

    /// <summary>
    /// Whether the phrase appears as whole words, allowing a plural "s":
    /// "penicillin" is found in "penicillins" but "cef" is not found in
    /// "cefalexin".
    /// </summary>
    private static bool ContainsPhrase(string words, string phrase)
    {
        var p = Words(phrase).Trim();
        if (p.Length == 0) return false;

        return words.Contains(" " + p + " ", StringComparison.Ordinal)
               || words.Contains(" " + p + "s ", StringComparison.Ordinal)
               || (p.EndsWith('s')
                   && words.Contains(" " + p[..^1] + " ",
                                     StringComparison.Ordinal));
    }
}
