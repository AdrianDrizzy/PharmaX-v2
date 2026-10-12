using PharmaX.Domain.Reference;

namespace PharmaX.Application.Reference;

/// <summary>
/// Writes dosage shorthand out in full: "T1 TDS PC" becomes "Take 1
/// tablet three times a day after meals".
/// </summary>
/// <remarks>
/// <para>
/// The dispensary has typed directions in ciphers for years, and a
/// patient cannot read them. Each word found in the Dosage Codes list is
/// replaced by its meaning; anything else - a number, a plain word, a
/// code nobody has defined - is left exactly as typed, so a code with no
/// meaning yet is never guessed at.
/// </para>
/// <para>
/// Applied where directions are recorded, in the script and dispensing
/// services, so the label, the dispensing screen and the patient's
/// history all carry the full wording the pharmacist checks before
/// issuing.
/// </para>
/// </remarks>
public static class DosageShorthand
{
    public static string Expand(string directions,
        IReadOnlyDictionary<string, string> codes)
    {
        ArgumentNullException.ThrowIfNull(codes);

        if (string.IsNullOrWhiteSpace(directions) || codes.Count == 0)
            return directions?.Trim() ?? "";

        var words = directions.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var changed = false;

        for (var i = 0; i < words.Length; i++)
        {
            if (codes.TryGetValue(words[i].ToUpperInvariant(), out var meaning))
            {
                words[i] = meaning;
                changed = true;
            }
        }

        if (!changed) return directions.Trim();

        var text = string.Join(" ", words);
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    /// <summary>The directions written out with the active Dosage Codes.</summary>
    public static async Task<string> ExpandDirectionsAsync(
        this IReferenceRepository reference, string directions,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reference);

        var codes = (await reference.ListAsync(ReferenceKind.DosageCode, "",
                includeInactive: false, ct))
            .GroupBy(c => c.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);

        return Expand(directions, codes);
    }

    /// <summary>
    /// The shorthand every dispensary uses the same way, seeded so the
    /// list is never empty. A pharmacy's own codes - RSI's "AF", "UF" - are
    /// added under Dispensary > Dosage Codes; the import report lists the
    /// ones its prescriptions used.
    /// </summary>
    public static IReadOnlyList<(string Code, string Meaning)> Standard { get; } = new[]
    {
        ("T1", "take 1 tablet"), ("T2", "take 2 tablets"), ("T3", "take 3 tablets"),
        ("C1", "take 1 capsule"), ("C2", "take 2 capsules"),
        ("OD", "once a day"), ("BD", "twice a day"), ("TDS", "three times a day"),
        ("QID", "four times a day"), ("QDS", "four times a day"),
        ("MANE", "in the morning"), ("NOCTE", "at night"),
        ("AC", "before meals"), ("PC", "after meals"),
        ("PRN", "when required"), ("STAT", "immediately"),
        ("PO", "by mouth"), ("UD", "as directed"),
        ("Q4H", "every 4 hours"), ("Q6H", "every 6 hours"), ("Q8H", "every 8 hours"),

        // The shorthand RSI's scripts were typed in: "1 T TDS AF". Only the
        // ones with one meaning; RSI's "UF" is left for the pharmacy to say.
        ("T", "tablet"), ("TAB", "tablet"), ("C", "capsule"), ("CAP", "capsule"),
        ("AF", "after food"), ("BF", "before food")
    };
}
