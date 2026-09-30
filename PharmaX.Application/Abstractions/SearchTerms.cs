namespace PharmaX.Application.Abstractions;

/// <summary>
/// How every search box in PharmaX reads what was typed: word by word,
/// each word matching anywhere, in any order.
/// </summary>
/// <remarks>
/// A search matched the whole of what was typed as one piece, so "amox
/// 500" found nothing although "Amoxicillin 500mg capsules" was on the
/// shelf, and "Moyo T" missed Tendai Moyo. Every word now has to appear
/// in one of the fields, so the first few letters of a name and a
/// surname are enough - which is what the suggestions under a search box
/// rely on to offer a match before the whole name has been typed.
/// </remarks>
public static class SearchTerms
{
    /// "Moyo, Tendai" is typed as often as "Tendai Moyo".
    private static readonly char[] Separators = { ' ', ',', ';' };

    /// <summary>The words of a search, each of which has to match.</summary>
    public static IReadOnlyList<string> Words(string? term) =>
        (term ?? "")
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries
                               | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// A telephone number as numbers are compared: its last nine digits,
    /// so 0771 234 567 and +263 77 123 4567 are the same number.
    /// </summary>
    /// <returns>Null where there are too few digits to be one.</returns>
    public static string? Telephone(string? text, int minimumDigits = 6)
    {
        var digits = new string((text ?? "").Where(char.IsAsciiDigit).ToArray());

        if (digits.Length < minimumDigits) return null;

        return digits.Length > 9 ? digits[^9..] : digits;
    }

    /// <summary>
    /// Whether every word of the search appears in at least one of the
    /// fields, ignoring case. For lists already in memory.
    /// </summary>
    public static bool Matches(string? term, params string?[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        return Words(term).All(word => fields.Any(field =>
            field is not null
            && field.Contains(word, StringComparison.OrdinalIgnoreCase)));
    }
}
