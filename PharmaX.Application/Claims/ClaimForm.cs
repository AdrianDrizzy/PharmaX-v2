using PharmaX.Application.Abstractions;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Reference;
using PharmaX.Application.Repositories;
using PharmaX.Application.Results;
using PharmaX.Application.Sales;
using PharmaX.Application.Security;
using PharmaX.Domain.Claims;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Claims;

/// <summary>
/// Everything the printed medical aid drug claim form carries: the
/// certified copy of the doctor's prescription the member signs and the
/// pharmacy sends to the scheme.
/// </summary>
/// <param name="PharmacyNumber">
/// The pharmacy's licence or practice number, which the scheme pays
/// against.
/// </param>
/// <param name="PatientName">Who the medicine was dispensed to.</param>
/// <param name="MemberSurname">
/// The scheme member's surname, as the form asks for it. The patient's
/// own, since the member and the patient share the membership number and
/// the suffix says which of the family it is.
/// </param>
/// <param name="DependantCode">The suffix: "00" for the main member.</param>
/// <param name="IsRepeat">
/// Whether this dispensing is a repeat of the script rather than its
/// first issue - the form's P/R column.
/// </param>
/// <param name="RepeatsLeft">Repeats still to be dispensed on the script.</param>
/// <param name="Gross">What the medicine cost, before the scheme's share.</param>
/// <param name="Claimed">What the scheme is asked to pay.</param>
/// <param name="CoPayment">What the patient paid at the counter.</param>
public sealed record ClaimFormDto(
    string ClaimNumber, DateOnly ClaimedOn,
    string PharmacyName, string PharmacyAddress, string PharmacyNumber,
    string VatNumber,
    string SchemeName, string PatientName, string MemberSurname,
    string MemberInitials, string MembershipNumber, string? DependantCode,
    DateOnly? DateOfBirth, string? Telephone,
    string ScriptNumber, string DoctorName, string? DoctorNumber,
    string DispensingNumber, DateOnly DispensedOn, bool IsRepeat,
    int RepeatsLeft, IReadOnlyList<ClaimFormLineDto> Lines,
    decimal Gross, decimal Claimed, decimal CoPayment,
    string CurrencySymbol);

/// <summary>One medicine on the claim form.</summary>
/// <param name="PriceCode">The product's code, which the price is looked up by.</param>
/// <param name="Charge">The line at the scheme's tariff.</param>
/// <param name="Claimed">The scheme's share of it.</param>
/// <param name="Directions">How to take it, for the counselling section.</param>
public sealed record ClaimFormLineDto(int LineNo, string Description,
    string PriceCode, decimal Quantity, decimal Charge, decimal Claimed,
    string Directions);

public interface IClaimFormService
{
    /// The claim form for one claim.
    Task<Result<ClaimFormDto>> BuildAsync(Guid claimId,
        CancellationToken ct = default);

    /// <summary>
    /// The claim form for whatever claim a dispensing raised.
    /// </summary>
    /// <remarks>
    /// The dispensary knows the dispensing it has just issued, not the
    /// claim number the issue gave it.
    /// </remarks>
    Task<Result<ClaimFormDto>> ForDispensingAsync(Guid dispensingId,
        CancellationToken ct = default);
}

/// <summary>
/// Prints a claim form on the workstation's A4 document printer.
/// </summary>
public interface IClaimFormPrinter
{
    Task<PrintResult> PrintAsync(ClaimFormDto form,
                                 CancellationToken ct = default);
}

/// <summary>
/// Gathers the claim form from the claim, the dispensing behind it, the
/// script and the patient.
/// </summary>
/// <remarks>
/// The claim was only ever a row on the Claims screen and a figure in
/// the ledger; nothing produced the paper the scheme and the member
/// actually sign. Open to the dispensary as well as the claims office:
/// the form is handed to the member at the counter, when the script is
/// issued, as often as it is posted in a batch at month end.
/// </remarks>
public sealed class ClaimFormService : IClaimFormService
{
    private readonly IClaimRepository _claims;
    private readonly IDispensingRepository _dispensings;
    private readonly IScriptRepository _scripts;
    private readonly IPatientRepository _patients;
    private readonly IProductRepository _products;
    private readonly IReferenceRepository _reference;
    private readonly IPermissionService _permissions;
    private readonly IWorkstationSettings _settings;
    private readonly PharmacyProfile _pharmacy;
    private readonly PharmaXOptions _options;

    public ClaimFormService(IClaimRepository claims,
        IDispensingRepository dispensings, IScriptRepository scripts,
        IPatientRepository patients, IProductRepository products,
        IReferenceRepository reference, IPermissionService permissions,
        IWorkstationSettings settings, PharmacyProfile pharmacy,
        PharmaXOptions options)
    {
        _claims = claims;
        _dispensings = dispensings;
        _scripts = scripts;
        _patients = patients;
        _products = products;
        _reference = reference;
        _permissions = permissions;
        _settings = settings;
        _pharmacy = pharmacy;
        _options = options;
    }

    public async Task<Result<ClaimFormDto>> BuildAsync(Guid claimId,
        CancellationToken ct = default)
    {
        DemandAccess();

        var claim = await _claims.GetAsync(claimId, ct);

        return claim is null
            ? Result<ClaimFormDto>.Fail("CLM-003", "Claim not found.")
            : await BuildAsync(claim, ct);
    }

    public async Task<Result<ClaimFormDto>> ForDispensingAsync(
        Guid dispensingId, CancellationToken ct = default)
    {
        DemandAccess();

        var claim = await _claims.ForDispensingAsync(dispensingId, ct);

        return claim is null
            ? Result<ClaimFormDto>.Fail("CLM-010",
                "Nothing on this dispensing is claimed from a medical aid, "
                + "so there is no claim form for it.")
            : await BuildAsync(claim, ct);
    }

    /// The dispensary and the claims office, and nobody else.
    private void DemandAccess()
    {
        if (!_permissions.HasAny(PermissionCode.ScriptDispense,
                                 PermissionCode.LedgerRead))
            _permissions.Demand(PermissionCode.ScriptDispense);
    }

    private async Task<Result<ClaimFormDto>> BuildAsync(MedicalAidClaim claim,
        CancellationToken ct)
    {
        var dispensing = await _dispensings.GetAsync(claim.DispensingId, ct);

        if (dispensing is null)
            return Result<ClaimFormDto>.Fail("CLM-011",
                $"The dispensing behind claim {claim.ClaimNumber} is no "
                + "longer on record.");

        var script = await _scripts.GetAsync(dispensing.ScriptId, ct);
        var patient = await _patients.GetAsync(claim.PatientId, ct);

        var scheme = await _reference.GetAsync(
            ReferenceKind.MedicalAidScheme, claim.SchemeId, ct);

        var lines = new List<ClaimFormLineDto>();

        foreach (var line in dispensing.Lines.OrderBy(l => l.LineNo))
        {
            var product = await _products.GetAsync(line.ProductId, ct);

            lines.Add(new ClaimFormLineDto(line.LineNo, line.Description,
                product?.Code ?? "", line.Quantity, line.TariffAmount.Amount,
                line.SchemeContribution.Amount, line.Directions));
        }

        var dispensedOn = dispensing.IssuedAtUtc is DateTime issued
            ? DateOnly.FromDateTime(issued.ToLocalTime())
            : claim.ClaimedOn;

        var currency = Currencies.ByCode(
            await _settings.GetAsync(WorkstationSettingKey.Currency, ct));

        return Result<ClaimFormDto>.Ok(new ClaimFormDto(
            ClaimNumber: claim.ClaimNumber,
            ClaimedOn: claim.ClaimedOn,
            PharmacyName: _pharmacy.TradingName,
            PharmacyAddress: _pharmacy.Address,
            PharmacyNumber: _options.Pharmacy.LicenceNumber,
            VatNumber: _pharmacy.VatNumber,
            SchemeName: scheme?.Name ?? "",
            PatientName: patient?.FullName ?? "",
            MemberSurname: patient?.Surname ?? "",
            MemberInitials: Initials(patient?.FirstNames),
            MembershipNumber: claim.MembershipNumber,
            DependantCode: claim.DependantCode,
            DateOfBirth: patient?.DateOfBirth,
            Telephone: patient?.Telephone,
            ScriptNumber: script?.ScriptNumber ?? "",
            DoctorName: script?.PrescriberName ?? "",
            DoctorNumber: script?.PrescriberNumber,
            DispensingNumber: dispensing.DocumentNumber,
            DispensedOn: dispensedOn,
            IsRepeat: dispensing.RepeatNumber > 0,
            RepeatsLeft: Math.Max(0, script?.RepeatsRemaining ?? 0),
            Lines: lines,
            Gross: dispensing.TariffTotal.Amount,
            Claimed: claim.Claimed.Amount,
            CoPayment: dispensing.CoPaymentTotal.Amount,
            CurrencySymbol: currency.Symbol));
    }

    /// <summary>
    /// "John Kudzai" as the form's initials box wants it: "J K".
    /// </summary>
    public static string Initials(string? firstNames) =>
        string.Join(" ", (firstNames ?? "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries
                        | StringSplitOptions.TrimEntries)
            .Select(name => char.ToUpperInvariant(name[0])));
}
