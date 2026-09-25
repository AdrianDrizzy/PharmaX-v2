using PharmaX.Application.Abstractions;

namespace PharmaX.Desktop.Mvp;

/// <summary>
/// What the sign-in window and the home screen say about whose pharmacy
/// this is: its trading name, and its address and VAT number beneath.
/// </summary>
/// <remarks>
/// One place for both screens, so that the first thing a cashier sees
/// and the screen they work from all day cannot word the pharmacy
/// differently.
/// </remarks>
internal static class PharmacyHeading
{
    /// <summary>
    /// The trading name, or the fallback while the pharmacy is still
    /// called by the product's own name.
    /// </summary>
    /// <remarks>
    /// Neither screen is the product's to head. A pharmacy that has not
    /// been named yet gets a plain heading and its logo, rather than
    /// "PharmaX" written across the top of every till.
    /// </remarks>
    public static string Name(PharmacyDetails details, string fallback)
    {
        ArgumentNullException.ThrowIfNull(details);

        return details.HasOwnName ? details.TradingName.Trim() : fallback;
    }

    /// <summary>
    /// The address on one line and the VAT number, as far as either has
    /// been set; empty where neither has.
    /// </summary>
    public static string Particulars(PharmacyDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        var address = string.Join(", ",
            (details.Address ?? "")
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0));

        var vat = string.IsNullOrWhiteSpace(details.VatNumber)
            ? ""
            : $"VAT {details.VatNumber.Trim()}";

        return string.Join("   -   ",
            new[] { address, vat }.Where(part => part.Length > 0));
    }
}
