using System.Globalization;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Finance;
using PharmaX.Application.Reference;
using PharmaX.Application.Repositories;
using PharmaX.Application.Results;
using PharmaX.Application.Security;
using PharmaX.Domain.Claims;
using PharmaX.Domain.Pricing;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Claims;

/// <summary>
/// One outstanding claim, as the claims screen shows it.
/// </summary>
public sealed record ClaimDto(Guid Id, string ClaimNumber, string SchemeName,
    string PatientName, string MembershipNumber, string DispensingNumber,
    DateOnly ClaimedOn, int AgeInDays, decimal Claimed, decimal Settled,
    decimal WrittenOff, decimal Outstanding, ClaimStatus Status)
{
    /// <summary>
    /// The bucket a debtors age analysis puts this in.
    /// </summary>
    /// <remarks>
    /// Thirty-day buckets, because that is how schemes pay and how every
    /// accountant reading this will expect to see it.
    /// </remarks>
    public string AgeBucket => AgeInDays switch
    {
        < 31 => "Current",
        < 61 => "31-60 days",
        < 91 => "61-90 days",
        _ => "Over 90 days"
    };
}

/// One scheme's line on the age analysis.
public sealed record SchemeAgeDto(Guid SchemeId, string SchemeName,
    int Claims, decimal Current, decimal Days31, decimal Days61,
    decimal Over90)
{
    public decimal Total => Current + Days31 + Days61 + Over90;
}

/// What one claim was paid on a remittance.
public sealed record RemittanceLineDto(Guid ClaimId, decimal Paid,
    bool WriteOffShortfall);

/// <summary>
/// What the medical aid schemes owe, and what they have paid.
/// </summary>
/// <remarks>
/// Claims are raised automatically when a script is issued, so nothing
/// here creates one by hand. What a person does with this service is
/// look at what is owed and record what came in.
/// </remarks>
public interface IClaimService
{
    Task<Result<IReadOnlyList<ClaimDto>>> OutstandingAsync(
        Guid? schemeId = null, CancellationToken ct = default);

    /// Owed per scheme, in thirty-day buckets.
    Task<Result<IReadOnlyList<SchemeAgeDto>>> AgeAnalysisAsync(
        CancellationToken ct = default);

    /// <summary>
    /// Records a scheme's remittance against the claims it paid.
    /// </summary>
    /// <remarks>
    /// One journal for the whole remittance rather than one per claim:
    /// the pharmacy received a single payment, and a bank reconciliation
    /// that has to add up forty journals to match one deposit is a bank
    /// reconciliation nobody does.
    /// </remarks>
    Task<Result<RemittanceResultDto>> SettleAsync(Guid schemeId,
        DateOnly receivedOn, string? reference,
        IReadOnlyList<RemittanceLineDto> lines,
        CancellationToken ct = default);

    /// The scheme has refused a claim outright.
    Task<Result> RejectAsync(Guid claimId, string reason,
                             CancellationToken ct = default);
}

public sealed record RemittanceResultDto(int Claims, decimal Paid,
                                         decimal WrittenOff);

public sealed class ClaimService : IClaimService
{
    private readonly IClaimRepository _claims;
    private readonly IReferenceRepository _reference;
    private readonly IPatientRepository _patients;
    private readonly IDispensingRepository _dispensings;
    private readonly IJournalPoster _journals;
    private readonly IPermissionService _permissions;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public ClaimService(IClaimRepository claims,
        IReferenceRepository reference, IPatientRepository patients,
        IDispensingRepository dispensings, IJournalPoster journals,
        IPermissionService permissions, IAuditWriter audit, IUnitOfWork uow,
        IClock clock)
    {
        _claims = claims;
        _reference = reference;
        _patients = patients;
        _dispensings = dispensings;
        _journals = journals;
        _permissions = permissions;
        _audit = audit;
        _uow = uow;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<ClaimDto>>> OutstandingAsync(
        Guid? schemeId = null, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.LedgerRead);

        var claims = await _claims.OutstandingAsync(schemeId, ct);
        var described = new List<ClaimDto>(claims.Count);

        var schemeNames = new Dictionary<Guid, string>();

        foreach (var claim in claims)
        {
            if (!schemeNames.TryGetValue(claim.SchemeId, out var scheme))
            {
                scheme = (await _reference.GetAsync(
                    ReferenceKind.MedicalAidScheme, claim.SchemeId, ct))
                    ?.Name ?? "(unknown scheme)";

                schemeNames[claim.SchemeId] = scheme;
            }

            var patient = await _patients.GetAsync(claim.PatientId, ct);

            var dispensing =
                await _dispensings.GetAsync(claim.DispensingId, ct);

            described.Add(new ClaimDto(
                claim.Id, claim.ClaimNumber, scheme,
                patient?.FullName ?? "(unknown patient)",
                claim.MembershipNumber,
                dispensing?.DocumentNumber ?? "",
                claim.ClaimedOn,
                claim.AgeInDays(_clock.Today),
                claim.Claimed.Amount, claim.Settled.Amount,
                claim.WrittenOff.Amount, claim.Outstanding.Amount,
                claim.Status));
        }

        return Result<IReadOnlyList<ClaimDto>>.Ok(described);
    }

    public async Task<Result<IReadOnlyList<SchemeAgeDto>>> AgeAnalysisAsync(
        CancellationToken ct = default)
    {
        var outstanding = await OutstandingAsync(null, ct);

        if (outstanding.IsFailure)
            return Result<IReadOnlyList<SchemeAgeDto>>.Fail(
                outstanding.Error.Code, outstanding.Error.Message);

        var schemes = await _reference.ListAsync(
            ReferenceKind.MedicalAidScheme, "", includeInactive: true, ct);

        IReadOnlyList<SchemeAgeDto> analysis = outstanding.Value
            .GroupBy(c => c.SchemeName, StringComparer.Ordinal)
            .Select(g => new SchemeAgeDto(
                schemes.FirstOrDefault(s =>
                    string.Equals(s.Name, g.Key, StringComparison.Ordinal))
                    ?.Id ?? Guid.Empty,
                g.Key,
                g.Count(),
                g.Where(c => c.AgeBucket == "Current").Sum(c => c.Outstanding),
                g.Where(c => c.AgeBucket == "31-60 days").Sum(c => c.Outstanding),
                g.Where(c => c.AgeBucket == "61-90 days").Sum(c => c.Outstanding),
                g.Where(c => c.AgeBucket == "Over 90 days").Sum(c => c.Outstanding)))
            .OrderByDescending(s => s.Total)
            .ToList();

        return Result<IReadOnlyList<SchemeAgeDto>>.Ok(analysis);
    }

    public async Task<Result<RemittanceResultDto>> SettleAsync(Guid schemeId,
        DateOnly receivedOn, string? reference,
        IReadOnlyList<RemittanceLineDto> lines,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lines);

        _permissions.Demand(PermissionCode.LedgerPost);

        if (lines.Count == 0)
            return Result<RemittanceResultDto>.Fail("CLM-001",
                "A remittance needs at least one claim on it.");

        var scheme = await _reference.GetAsync(
            ReferenceKind.MedicalAidScheme, schemeId, ct);

        if (scheme is null)
            return Result<RemittanceResultDto>.Fail("CLM-002",
                "Scheme not found.");

        await using var tx = await _uow.BeginTransactionAsync(ct);

        var paid = Money.Zero();
        var writtenOff = Money.Zero();

        foreach (var line in lines)
        {
            var claim = await _claims.GetAsync(line.ClaimId, ct);

            if (claim is null)
                return Result<RemittanceResultDto>.Fail("CLM-003",
                    "One of the claims on this remittance no longer "
                    + "exists.");

            // A remittance from one scheme cannot settle another's
            // claims, and a mis-keyed claim number is the usual way that
            // would happen.
            if (claim.SchemeId != schemeId)
                return Result<RemittanceResultDto>.Fail("CLM-004",
                    $"Claim {claim.ClaimNumber} belongs to another scheme.");

            var before = claim.WrittenOff;

            try
            {
                claim.Settle(Money.Of(line.Paid), line.WriteOffShortfall,
                    $"Short-paid by {scheme.Name}", _clock.UtcNow);
            }
            catch (Domain.Exceptions.DomainException ex)
            {
                return Result<RemittanceResultDto>.Fail("CLM-005",
                    ex.Message);
            }

            paid += Money.Of(line.Paid);
            writtenOff += claim.WrittenOff - before;
        }

        // One journal for the whole remittance: the pharmacy received a
        // single payment, and the bank reconciliation has to match it.
        await _journals.PostRemittanceAsync(scheme.Name, paid, writtenOff,
            receivedOn, reference, ct);

        _audit.Applied("fin.claim.settle", "MedicalAidScheme", schemeId,
            string.Format(CultureInfo.CurrentCulture,
                "{0} claim(s), {1} paid, {2} written off. {3}",
                lines.Count, paid, writtenOff, reference));

        await _uow.SaveChangesAsync(ct);
        await tx.DisposeAsync();

        return Result<RemittanceResultDto>.Ok(new RemittanceResultDto(
            lines.Count, paid.Amount, writtenOff.Amount));
    }

    public async Task<Result> RejectAsync(Guid claimId, string reason,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.LedgerPost);

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Fail("CLM-006", "A rejection needs a reason.");

        var claim = await _claims.GetAsync(claimId, ct);

        if (claim is null)
            return Result.Fail("CLM-003", "Claim not found.");

        var scheme = await _reference.GetAsync(
            ReferenceKind.MedicalAidScheme, claim.SchemeId, ct);

        var givenUp = claim.Outstanding;

        try
        {
            claim.Reject(reason, _clock.UtcNow);
        }
        catch (Domain.Exceptions.DomainException ex)
        {
            return Result.Fail("CLM-005", ex.Message);
        }

        // Nothing came in, so the whole balance is written off and the
        // debtor comes down by the same amount.
        await _journals.PostRemittanceAsync(
            scheme?.Name ?? "scheme", Money.Zero(), givenUp,
            _clock.Today, $"Claim {claim.ClaimNumber} rejected", ct);

        _audit.Applied("fin.claim.reject", "MedicalAidClaim", claimId,
                       reason);

        await _uow.SaveChangesAsync(ct);

        return Result.Ok();
    }

    /// <summary>
    /// Raises the claim for a dispensing that a scheme is paying part of.
    /// </summary>
    /// <remarks>
    /// Called from the dispensing service inside the issue transaction,
    /// rather than being a thing somebody has to remember to do. Returns
    /// null where there is nothing to claim: no scheme on the patient,
    /// or a script the scheme contributes nothing to.
    /// </remarks>
    public static MedicalAidClaim? Build(string claimNumber,
        Domain.Patients.Patient patient,
        Domain.Dispensing.Dispensing dispensing, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(patient);
        ArgumentNullException.ThrowIfNull(dispensing);

        if (!patient.IsOnMedicalAid) return null;

        var contribution = dispensing.ContributionTotal;
        if (contribution.IsZero) return null;

        return MedicalAidClaim.Raise(claimNumber,
            patient.MedicalAidSchemeId!.Value, patient.Id, dispensing.Id,
            patient.MembershipNumber!, patient.DependantCode,
            contribution, today);
    }

    /// <summary>
    /// The VAT split of the scheme's share, a line at a time.
    /// </summary>
    /// <remarks>
    /// Worked out per line because a script can mix rates. Applying one
    /// rate to the claim total would misstate what is owed to the
    /// revenue authority on every mixed script the pharmacy dispenses.
    /// </remarks>
    public static IReadOnlyList<VatSplit> SplitOf(
        Domain.Dispensing.Dispensing dispensing,
        IReadOnlyDictionary<Guid, decimal> vatRates)
    {
        ArgumentNullException.ThrowIfNull(dispensing);
        ArgumentNullException.ThrowIfNull(vatRates);

        return dispensing.Lines
            .Where(l => !l.SchemeContribution.IsZero)
            .Select(l => VatCalculator.FromInclusive(l.SchemeContribution,
                vatRates.GetValueOrDefault(l.ProductId)))
            .ToList();
    }
}
