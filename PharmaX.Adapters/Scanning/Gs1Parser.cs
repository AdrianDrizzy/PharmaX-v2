using System.Globalization;

namespace PharmaX.Adapters.Scanning;

/// <summary>
/// What a GS1 DataMatrix on a medicine pack carries: the product code,
/// and often the batch and expiry as well. Reading them off the barcode
/// rather than asking the operator to type them is the difference
/// between a two-second dispense and a twenty-second one.
/// </summary>
public sealed record Gs1Data(string? Gtin, string? BatchNumber,
                             DateOnly? Expiry, string? Serial)
{
    public bool HasBatchDetail => BatchNumber is not null || Expiry is not null;
}

/// <summary>
/// Parses the GS1 application identifiers PharmaX cares about. Only four
/// of the hundreds defined, because parsing an identifier nothing reads
/// is code that can only ever be wrong.
/// </summary>
public static class Gs1Parser
{
    /// The group separator that ends a variable-length field.
    private const char GroupSeparator = '';

    private static readonly Dictionary<string, int> FixedLengths =
        new(StringComparer.Ordinal)
        {
            ["01"] = 14,    // GTIN
            ["17"] = 6      // Expiry, YYMMDD
        };

    /// <summary>
    /// Returns null where the input is not a GS1 string, which is the
    /// common case: most scans at a till are a plain retail barcode.
    /// </summary>
    public static Gs1Data? TryParse(string? scanned)
    {
        if (string.IsNullOrWhiteSpace(scanned)) return null;

        var input = scanned.Trim().TrimStart(']', 'd', '2', 'Q', '3');

        if (input.Length < 4 || !char.IsDigit(input[0])) return null;

        string? gtin = null, batch = null, serial = null;
        DateOnly? expiry = null;
        var position = 0;
        var recognised = false;

        while (position + 2 <= input.Length)
        {
            var ai = input.Substring(position, 2);
            position += 2;

            if (FixedLengths.TryGetValue(ai, out var length))
            {
                if (position + length > input.Length) break;

                var value = input.Substring(position, length);
                position += length;
                recognised = true;

                if (ai == "01") gtin = value;
                else if (TryParseExpiry(value) is DateOnly parsed)
                    expiry = parsed;
            }
            else if (ai is "10" or "21")
            {
                // Variable length: runs to the group separator or the end.
                var end = input.IndexOf(GroupSeparator, position);
                if (end < 0) end = input.Length;

                var value = input[position..end];
                position = end < input.Length ? end + 1 : end;
                recognised = true;

                if (ai == "10") batch = value;
                else serial = value;
            }
            else
            {
                // An identifier PharmaX does not read. Without knowing its
                // length there is no safe way to skip it, so stop here and
                // return what was understood.
                break;
            }
        }

        return recognised ? new Gs1Data(gtin, batch, expiry, serial) : null;
    }

    /// <summary>
    /// GS1 expiry is YYMMDD, and a day of 00 means "end of month", which
    /// is how most medicine packs are printed.
    /// </summary>
    private static DateOnly? TryParseExpiry(string yymmdd)
    {
        if (yymmdd.Length != 6
            || !int.TryParse(yymmdd[..2], NumberStyles.None,
                             CultureInfo.InvariantCulture, out var yy)
            || !int.TryParse(yymmdd.Substring(2, 2), NumberStyles.None,
                             CultureInfo.InvariantCulture, out var mm)
            || !int.TryParse(yymmdd.Substring(4, 2), NumberStyles.None,
                             CultureInfo.InvariantCulture, out var dd))
            return null;

        if (mm is < 1 or > 12) return null;

        // GS1 rule: 51 to 99 is the previous century, 00 to 50 this one.
        var year = yy <= 50 ? 2000 + yy : 1900 + yy;
        var day = dd == 0 ? DateTime.DaysInMonth(year, mm) : dd;

        if (day > DateTime.DaysInMonth(year, mm)) return null;

        return new DateOnly(year, mm, day);
    }
}
