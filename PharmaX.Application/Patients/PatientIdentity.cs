using System.Globalization;

namespace PharmaX.Application.Patients;

/// <summary>
/// Who a patient file really is, as the phone number and the address
/// together say: two files with the same of both are the same person
/// entered twice.
/// </summary>
/// <remarks>
/// <para>
/// Both are compared as the counter would read them, not as they were
/// typed: "+263 77 641 5663" is 0776415663, and "8,  Thorngrove,Bulawayo"
/// is "8, thorngrove, bulawayo".
/// </para>
/// <para>
/// A file without a usable number or address cannot be matched, so it is
/// counted as a person of its own. Most of the imported files have no
/// phone number, and an address alone ("Makokoba, Makokoba, Bulawayo")
/// is shared by whole streets; merging on it, or merging all the blanks
/// into one, would count dozens of people as one.
/// </para>
/// </remarks>
public static class PatientIdentity
{
    /// Fewer digits than this is a placeholder ("0", "07"), not a number.
    private const int ShortestNumber = 7;

    /// <summary>
    /// The number as digits only, in the local 0... form; null where what
    /// was typed is not a phone number.
    /// </summary>
    public static string? Phone(string? telephone)
    {
        if (string.IsNullOrWhiteSpace(telephone)) return null;

        var digits = new string(telephone.Where(char.IsAsciiDigit).ToArray());

        if (digits.StartsWith("00263", StringComparison.Ordinal))
            digits = "0" + digits[5..];
        else if (digits.StartsWith("263", StringComparison.Ordinal))
            digits = "0" + digits[3..];
        else if (digits.Length == 9 && digits[0] == '7')
            digits = "0" + digits; // a mobile keyed without its leading 0

        return digits.Length >= ShortestNumber ? digits : null;
    }

    /// <summary>
    /// The address in lower case, one space between words and after each
    /// comma, no stray punctuation at the ends; null where it is blank.
    /// </summary>
    public static string? Address(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;

        var parts = address
            .ToLower(CultureInfo.InvariantCulture)
            .Split(',')
            .Select(p => string.Join(' ',
                p.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
            .Where(p => p.Length > 0);

        var text = string.Join(", ", parts).Trim(' ', '.', ',', ';', '-');
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// What a file is matched on, or null where it cannot be matched and
    /// is a person of its own.
    /// </summary>
    public static string? Key(string? telephone, string? address) =>
        Phone(telephone) is string phone && Address(address) is string place
            ? $"{phone}|{place}"
            : null;

    /// <summary>
    /// How many people the files are: one for each phone number and
    /// address shared, and one for each file that cannot be matched.
    /// </summary>
    public static int CountPeople(
        IEnumerable<(string? Telephone, string? Address)> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var unmatched = 0;
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (telephone, address) in files)
        {
            if (Key(telephone, address) is string key) keys.Add(key);
            else unmatched++;
        }

        return keys.Count + unmatched;
    }
}
