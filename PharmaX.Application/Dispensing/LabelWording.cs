using System.Text.RegularExpressions;

namespace PharmaX.Application.Dispensing;

/// <summary>
/// What a dispensing label says in words: the directions as they were
/// typed, in capitals ("TAKE 2 TABLETS AFTER MEALS THREE TIMES DAILY"),
/// and the warnings that go with the medicine.
/// </summary>
/// <remarks>
/// <para>
/// Applied where the label is drawn, so a label printed at the issue, a
/// reprint, a to-follow and a pre-pack all read the same way, including
/// reprints of dispensings recorded before the wording changed.
/// </para>
/// <para>
/// The warnings are chosen from the medicine's own name - the imported
/// catalogue names most products by what is in them ("AMOXYCILLIN
/// CAPSULES 500MG", "CHLORPHENIRAMINE (VARICHEM) 4MG"), and the common
/// brands are listed beside their ingredient. A medicine that matches
/// none of them still carries a general warning, so no label goes out
/// without one.
/// </para>
/// </remarks>
public static class LabelWording
{
    /// Printed on every trailer label.
    public const string KeepOutOfReach = "Keep out of reach of children.";

    /// For a medicine none of the rules below speaks to.
    public const string General = "Use only as directed. Do not share this medicine.";

    /// The most a label holds before the stock runs out of room.
    public const int MostWarnings = 3;

    // ---- Directions ------------------------------------------------------

    /// <summary>
    /// The directions as the label prints them: as they were typed, on one
    /// line, in capitals - the way the pharmacy's own labels read.
    /// </summary>
    public static string Directions(string? directions) =>
        Regex.Replace((directions ?? "").Trim(), @"\s+", " ").ToUpperInvariant();

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
