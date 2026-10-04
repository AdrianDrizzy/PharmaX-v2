namespace PharmaX.Application.Abstractions;

/// <summary>
/// The particulars the pharmacy trades under: the name on the shop
/// front, the company's registered name beneath it, its VAT
/// registration, its address, and the numbers it is reached on.
/// </summary>
/// <param name="Telephone">
/// The number printed on the receipt and at the foot of every
/// dispensing label. A patient holding a container hours later has
/// nothing else to ring.
/// </param>
/// <param name="WhatsApp">
/// Where the pharmacy takes messages and photographs of scripts, which
/// is often not the number it answers calls on. Printed on the receipt.
/// </param>
public sealed record PharmacyDetails(string TradingName,
                                     string RegisteredName,
                                     string VatNumber, string Address,
                                     string Telephone = "",
                                     string WhatsApp = "")
{
    /// What the application calls itself until the pharmacy has been
    /// given a name of its own.
    public const string ProductName = "PharmaX";

    /// The installed particulars, before anyone has changed them at a
    /// till.
    public static PharmacyDetails From(PharmacyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new(options.TradingName, options.RegisteredName,
                   options.VatNumber, options.Address, options.Telephone,
                   options.WhatsApp);
    }

    /// <summary>
    /// Whether the pharmacy trades under a name of its own, rather than
    /// the product's name the installation starts with.
    /// </summary>
    /// <remarks>
    /// The sign-in window and the home screen head themselves with the
    /// trading name, and a pharmacy that has not been named yet would
    /// otherwise have "PharmaX" written across both.
    /// </remarks>
    public bool HasOwnName =>
        !string.IsNullOrWhiteSpace(TradingName)
        && !string.Equals(TradingName.Trim(), ProductName,
                          StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The pharmacy's particulars as the running application currently
/// knows them. One instance for the whole process.
/// </summary>
/// <remarks>
/// PharmaXOptions is bound once from the configuration files and cannot
/// change, but these can be edited at the System Console and must reach
/// the next receipt, the next export and every open window without a
/// restart. Everything that prints or shows the pharmacy's name reads
/// it from here, and the installed options are only the starting value.
///
/// The details are swapped as one immutable record, so a receipt being
/// printed on one thread never sees the new name with the old VAT
/// number.
/// </remarks>
public sealed class PharmacyProfile
{
    private PharmacyDetails _current;

    public PharmacyProfile(PharmaXOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _current = PharmacyDetails.From(options.Pharmacy);
    }

    public PharmacyDetails Current => Volatile.Read(ref _current);

    public string TradingName => Current.TradingName;
    public string RegisteredName => Current.RegisteredName;
    public string VatNumber => Current.VatNumber;
    public string Address => Current.Address;
    public string Telephone => Current.Telephone;
    public string WhatsApp => Current.WhatsApp;

    /// Raised after the details have changed, so open windows can
    /// re-title themselves.
    public event EventHandler? Changed;

    public void Use(PharmacyDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        Volatile.Write(ref _current, details);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// A title or caption with the product's name replaced by the
    /// trading name.
    /// </summary>
    /// <remarks>
    /// Only a leading "PharmaX" standing as a word is replaced -
    /// "PharmaX - Sign in", "PharmaX", "PharmaX  -  TEST DATA" - so a
    /// title that merely mentions the product further along is left
    /// alone. A blank trading name leaves the title as it was written.
    ///
    /// There used to be a switch for this at the System Console, off
    /// until someone found it, so a pharmacy that had saved its own name
    /// still saw "PharmaX" on every window. The trading name is what the
    /// pharmacy is called; the titles simply follow it.
    /// </remarks>
    public string Brand(string? text)
    {
        text ??= "";
        var details = Current;

        if (string.IsNullOrWhiteSpace(details.TradingName)
            || !text.StartsWith(PharmacyDetails.ProductName,
                                StringComparison.Ordinal))
            return text;

        var rest = text[PharmacyDetails.ProductName.Length..];
        if (rest.Length > 0 && char.IsLetterOrDigit(rest[0])) return text;

        return details.TradingName.Trim() + rest;
    }
}
