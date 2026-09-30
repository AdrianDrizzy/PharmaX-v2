using PharmaX.Application.Abstractions;
using PharmaX.Application.Sales;

namespace PharmaX.Desktop.Features.Sales;

/// <summary>
/// Builds the fiscal header every receipt must carry (specification
/// Section 6.10). A separate class so that the presenter does not
/// assemble eight strings inline, and so the values come from
/// configuration rather than from constants in a print method.
/// </summary>
public sealed class ReceiptHeaderFactory
{
    private readonly PharmaXOptions _options;
    private readonly PharmacyProfile _pharmacy;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly Mvp.Branding _branding;

    public ReceiptHeaderFactory(PharmaXOptions options, PharmacyProfile pharmacy,
                                ICurrentUser user, IClock clock,
                                Mvp.Branding branding)
    {
        _options = options;
        _pharmacy = pharmacy;
        _user = user;
        _clock = clock;
        _branding = branding;
    }

    // Names, VAT number and address from the live profile, so a change
    // at the System Console is on the very next receipt.
    public ReceiptHeaderDto Build() => new(
        TradingName: _pharmacy.TradingName,
        RegisteredName: _pharmacy.RegisteredName,
        Address: _pharmacy.Address,
        VatNumber: _pharmacy.VatNumber,
        LicenceNumber: _options.Pharmacy.LicenceNumber,
        BranchAndTill: $"Branch {_options.Workstation.BranchCode} "
                       + $"Till {_options.Workstation.TillNumber}",
        OperatorName: _user.Username,
        PrintedAtLocal: _clock.UtcNow.ToLocalTime(),
        LogoPath: _branding.LogoPath,
        TillNumber: _options.Workstation.TillNumber,
        CurrencyCode: Mvp.Format.Currency.Code);
}
