using PharmaX.Domain.Common;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Primitives;

namespace PharmaX.Domain.Claims;

public enum ClaimStatus
{
    /// Raised against the scheme and not yet answered.
    Outstanding = 1,

    /// The scheme paid part of it and the rest is still owed.
    PartPaid = 2,

    /// Paid in full, or short-paid with the difference written off.
    Settled = 3,

    /// The scheme refused it. The money is not coming.
    Rejected = 4
}

/// <summary>
/// What a medical aid scheme owes the pharmacy for one dispensing.
/// </summary>
/// <remarks>
/// Raised the moment a script is issued, because that is when the
/// medicine leaves and the scheme's obligation begins - not when
/// somebody remembers to submit a batch at the end of the month.
///
/// Without this the pharmacy's books were wrong in the largest possible
/// way. A ninety-dollar dispensing where the scheme pays sixty and the
/// patient thirty recorded thirty dollars of revenue: the patient's
/// co-payment went through the till and the scheme's share went nowhere
/// at all. For a dispensary that is most of the turnover.
/// </remarks>
public sealed class MedicalAidClaim : AuditableEntity
{
    public string ClaimNumber { get; private set; } = "";
    public Guid SchemeId { get; private set; }
    public Guid PatientId { get; private set; }
    public Guid DispensingId { get; private set; }

    /// The patient's number with the scheme, as it was when claimed.
    public string MembershipNumber { get; private set; } = "";
    public string? DependantCode { get; private set; }

    public DateOnly ClaimedOn { get; private set; }

    /// What the scheme was asked for.
    public Money Claimed { get; private set; } = Money.Zero();

    /// What it has actually paid so far.
    public Money Settled { get; private set; } = Money.Zero();

    /// <summary>
    /// The part the scheme was never going to pay, given up on.
    /// </summary>
    /// <remarks>
    /// Short payment is normal rather than exceptional in pharmacy: a
    /// scheme pays its own tariff and not the pharmacy's price, and the
    /// difference is a cost of trading with them. Recording it as a
    /// write-off rather than quietly reducing the claim is what lets a
    /// manager see which scheme is costing them the most.
    /// </remarks>
    public Money WrittenOff { get; private set; } = Money.Zero();

    public string? WriteOffReason { get; private set; }
    public ClaimStatus Status { get; private set; } = ClaimStatus.Outstanding;
    public DateTime? SettledAtUtc { get; private set; }

    /// Still owed: what was claimed, less what has been paid or given up.
    public Money Outstanding => Claimed - Settled - WrittenOff;

    public bool IsOpen =>
        Status is ClaimStatus.Outstanding or ClaimStatus.PartPaid;

    private MedicalAidClaim() { }

    public static MedicalAidClaim Raise(string claimNumber, Guid schemeId,
        Guid patientId, Guid dispensingId, string membershipNumber,
        string? dependantCode, Money claimed, DateOnly claimedOn)
    {
        if (string.IsNullOrWhiteSpace(claimNumber))
            throw new DomainException("A claim needs a number.");

        if (schemeId == Guid.Empty)
            throw new DomainException("A claim must name a scheme.");

        if (string.IsNullOrWhiteSpace(membershipNumber))
            throw new DomainException(
                "A claim must quote the patient's membership number.");

        // A claim for nothing is not a claim. Schemes reject them, and a
        // list full of zero-value rows is a list nobody works through.
        if (claimed.IsZero || claimed.IsNegative)
            throw new DomainException(
                "A claim must be for more than nothing.");

        return new MedicalAidClaim
        {
            ClaimNumber = claimNumber,
            SchemeId = schemeId,
            PatientId = patientId,
            DispensingId = dispensingId,
            MembershipNumber = membershipNumber.Trim(),
            DependantCode = dependantCode,
            Claimed = claimed,
            Settled = Money.Zero(claimed.Currency),
            WrittenOff = Money.Zero(claimed.Currency),
            ClaimedOn = claimedOn
        };
    }

    /// <summary>
    /// Records money received against this claim, and optionally gives up
    /// on the rest.
    /// </summary>
    /// <param name="paid">What the scheme actually sent.</param>
    /// <param name="writeOffShortfall">
    /// True where the difference is accepted rather than chased. A
    /// remittance advice is the scheme's final word on a claim far more
    /// often than not, so this is usually true - but it is the operator
    /// who decides, not this method.
    /// </param>
    public void Settle(Money paid, bool writeOffShortfall, string? reason,
                       DateTime nowUtc)
    {
        if (!IsOpen)
            throw new DomainException(
                $"Claim {ClaimNumber} is {Status.ToString().ToLowerInvariant()} "
                + "and cannot take another payment.");

        if (paid.IsNegative)
            throw new DomainException(
                "A settlement cannot be negative. Reverse the claim "
                + "instead.");

        if (paid.Amount > Outstanding.Amount)
            throw new DomainException(
                $"Claim {ClaimNumber} has {Outstanding} outstanding and "
                + $"cannot take {paid}. Check the remittance against the "
                + "claim number.");

        Settled += paid;

        if (writeOffShortfall && !Outstanding.IsZero)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new DomainException(
                    "Giving up on the balance of a claim needs a reason.");

            WrittenOff += Outstanding;
            WriteOffReason = reason.Trim();
        }

        if (Outstanding.IsZero)
        {
            Status = ClaimStatus.Settled;
            SettledAtUtc = nowUtc;
        }
        else
        {
            Status = ClaimStatus.PartPaid;
        }
    }

    /// <summary>
    /// The scheme has refused the claim outright.
    /// </summary>
    /// <remarks>
    /// Distinct from a write-off: a rejection is the scheme saying it
    /// never owed this, and it usually means the patient does. Kept
    /// separate so that a manager can tell "they paid us less than we
    /// asked" from "they would not pay at all", which are different
    /// problems with different answers.
    /// </remarks>
    public void Reject(string reason, DateTime nowUtc)
    {
        if (!IsOpen)
            throw new DomainException(
                $"Claim {ClaimNumber} is already "
                + $"{Status.ToString().ToLowerInvariant()}.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("A rejection needs a reason.");

        WrittenOff += Outstanding;
        WriteOffReason = reason.Trim();
        Status = ClaimStatus.Rejected;
        SettledAtUtc = nowUtc;
    }

    /// How long the scheme has had it, in days.
    public int AgeInDays(DateOnly today) =>
        Math.Max(0, today.DayNumber - ClaimedOn.DayNumber);
}
