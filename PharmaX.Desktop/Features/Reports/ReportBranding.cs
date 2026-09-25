using PharmaX.Application.Abstractions;
using PharmaX.Application.Reporting;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Reports;

/// <summary>
/// Gives the exporters the same logo the shell and the receipts use.
/// </summary>
/// <remarks>
/// Reads it from the Branding singleton rather than from the database,
/// so an export picks up a logo changed in System Setup without a
/// restart and without touching a DbContext from a background export.
/// </remarks>
public sealed class ReportBranding : IReportBranding
{
    private readonly Branding _branding;
    private readonly PharmacyProfile _pharmacy;

    public ReportBranding(Branding branding, PharmacyProfile pharmacy)
    {
        _branding = branding;
        _pharmacy = pharmacy;
    }

    public string? LogoPath => _branding.LogoPath;

    public string TradingName => _pharmacy.TradingName;
}
