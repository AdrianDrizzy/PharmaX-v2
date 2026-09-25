using PharmaX.Application.Abstractions;
using PharmaX.Application.Results;
using PharmaX.Application.Security;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Pharmacy;

/// <summary>
/// Reads and changes the name, registered name, VAT number and address
/// the pharmacy trades under.
/// </summary>
public interface IPharmacyDetailsService
{
    /// What is stored for the pharmacy, with the installed configuration
    /// filling anything that has never been set.
    Task<PharmacyDetails> LoadAsync(CancellationToken ct = default);

    /// Stores new particulars and puts them into use immediately.
    Task<Result> SaveAsync(PharmacyDetails details,
                           CancellationToken ct = default);
}

/// <remarks>
/// These are fiscal particulars - the VAT number on a receipt is the
/// pharmacy's declaration to the revenue service - so changing them is
/// an administrator's permission and every change is audited with what
/// it was and what it became.
/// </remarks>
public sealed class PharmacyDetailsService : IPharmacyDetailsService
{
    public const int MaxTradingName = 100;
    public const int MaxRegisteredName = 100;
    public const int MaxVatNumber = 30;
    public const int MaxAddress = 400;

    private readonly IPharmacySettings _settings;
    private readonly PharmacyProfile _profile;
    private readonly PharmaXOptions _options;
    private readonly IPermissionService _permissions;
    private readonly IAuditWriter _audit;

    public PharmacyDetailsService(IPharmacySettings settings,
        PharmacyProfile profile, PharmaXOptions options,
        IPermissionService permissions, IAuditWriter audit)
    {
        _settings = settings;
        _profile = profile;
        _options = options;
        _permissions = permissions;
        _audit = audit;
    }

    public async Task<PharmacyDetails> LoadAsync(CancellationToken ct = default)
    {
        var installed = PharmacyDetails.From(_options.Pharmacy);

        var name = await _settings.GetAsync(PharmacySettingKey.TradingName, ct)
                                  .ConfigureAwait(false);
        var registered = await _settings.GetAsync(
            PharmacySettingKey.RegisteredName, ct).ConfigureAwait(false);
        var vat = await _settings.GetAsync(PharmacySettingKey.VatNumber, ct)
                                 .ConfigureAwait(false);
        var address = await _settings.GetAsync(PharmacySettingKey.Address, ct)
                                     .ConfigureAwait(false);

        // A value never set falls back to the installed file. One set to
        // blank on purpose - an address removed, a sole trader with no
        // company name - is kept blank.
        return new PharmacyDetails(
            TradingName: string.IsNullOrWhiteSpace(name)
                ? installed.TradingName : name,
            RegisteredName: registered ?? installed.RegisteredName,
            VatNumber: vat ?? installed.VatNumber,
            Address: address ?? installed.Address);
    }

    public async Task<Result> SaveAsync(PharmacyDetails details,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(details);

        _permissions.Demand(PermissionCode.SettingsChange);

        var clean = new PharmacyDetails(
            TradingName: details.TradingName?.Trim() ?? "",
            RegisteredName: details.RegisteredName?.Trim() ?? "",
            VatNumber: details.VatNumber?.Trim() ?? "",
            Address: NormaliseAddress(details.Address));

        var refusal = Validate(clean);
        if (refusal is not null) return refusal;

        var before = _profile.Current;

        // Written before the settings: the settings store saves the
        // shared context, which commits this entry in the same save.
        _audit.Applied("adm.pharmacy.details", "Pharmacy", Guid.Empty,
            Describe(before, clean));

        await _settings.SetAsync(PharmacySettingKey.TradingName,
            clean.TradingName, ct).ConfigureAwait(false);
        await _settings.SetAsync(PharmacySettingKey.RegisteredName,
            clean.RegisteredName, ct).ConfigureAwait(false);
        await _settings.SetAsync(PharmacySettingKey.VatNumber,
            clean.VatNumber, ct).ConfigureAwait(false);
        await _settings.SetAsync(PharmacySettingKey.Address,
            clean.Address, ct).ConfigureAwait(false);

        _profile.Use(clean);
        return Result.Ok();
    }

    internal static Result? Validate(PharmacyDetails d)
    {
        if (d.TradingName.Length == 0)
            return Result.Fail("PHX-001",
                "Enter the trading name - it heads every receipt.");

        if (d.TradingName.Length > MaxTradingName)
            return Result.Fail("PHX-002",
                $"The trading name can be at most {MaxTradingName} characters.");

        if (d.RegisteredName.Length > MaxRegisteredName)
            return Result.Fail("PHX-006",
                $"The registered name can be at most {MaxRegisteredName} characters.");

        if (d.VatNumber.Length > MaxVatNumber)
            return Result.Fail("PHX-003",
                $"The VAT number can be at most {MaxVatNumber} characters.");

        if (d.VatNumber.Length > 0
            && !d.VatNumber.All(c => char.IsLetterOrDigit(c) || c is ' ' or '-'))
            return Result.Fail("PHX-004",
                "A VAT number is letters and digits only.");

        if (d.Address.Length > MaxAddress)
            return Result.Fail("PHX-005",
                $"The address can be at most {MaxAddress} characters.");

        return null;
    }

    /// One line per line of the address, with blank lines and stray
    /// spaces removed, so the receipt prints it the way it was typed.
    private static string NormaliseAddress(string? address) =>
        string.Join(Environment.NewLine,
            (address ?? "")
                .Split('\n')
                .Select(line => line.Trim().TrimEnd('\r').Trim())
                .Where(line => line.Length > 0));

    private static string Describe(PharmacyDetails before,
                                   PharmacyDetails after)
    {
        var changes = new List<string>();

        void Compare(string what, string was, string now)
        {
            if (!string.Equals(was, now, StringComparison.Ordinal))
                changes.Add($"{what}: '{Flat(was)}' -> '{Flat(now)}'");
        }

        Compare("trading name", before.TradingName, after.TradingName);
        Compare("registered name", before.RegisteredName, after.RegisteredName);
        Compare("VAT number", before.VatNumber, after.VatNumber);
        Compare("address", before.Address, after.Address);

        return changes.Count == 0 ? "saved unchanged" : string.Join("; ", changes);
    }

    private static string Flat(string text) =>
        text.Replace(Environment.NewLine, ", ", StringComparison.Ordinal);
}
