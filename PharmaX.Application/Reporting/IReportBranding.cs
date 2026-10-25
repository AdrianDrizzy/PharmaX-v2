namespace PharmaX.Application.Reporting;

/// <summary>
/// The pharmacy's own marks, for the top of an exported file.
/// </summary>
/// <remarks>
/// An abstraction rather than a path passed through every report,
/// because the logo belongs to the pharmacy and not to the question the
/// report answers: the same Stock On Hand result exported twice should
/// carry whatever mark the pharmacy has set at the time, and a report
/// definition should not have to know that a logo exists.
/// </remarks>
public interface IReportBranding
{
    /// The logo file, or null where the pharmacy has not set one.
    string? LogoPath { get; }

    string TradingName { get; }

    /// One line per line of the address; empty where none is set.
    string Address { get; }

    string VatNumber { get; }

    string Telephone { get; }
}

/// <summary>
/// What reporting uses when nothing better has been registered, which
/// is the case in tests and anywhere the desktop layer is absent.
/// </summary>
public sealed class NoReportBranding : IReportBranding
{
    public string? LogoPath => null;

    public string TradingName => "";

    public string Address => "";

    public string VatNumber => "";

    public string Telephone => "";
}
