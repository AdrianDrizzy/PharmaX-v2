using FluentAssertions;
using PharmaX.Domain.Claims;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Primitives;

namespace PharmaX.Tests.Unit.Domain;

/// <summary>
/// What a medical aid scheme owes, and what happens when it pays less
/// than that - which in pharmacy is most of the time.
/// </summary>
public class MedicalAidClaimTests
{
    private static readonly DateTime Noon =
        new(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateOnly Today = new(2026, 9, 13);

    [Fact]
    public void A_new_claim_is_owed_in_full()
    {
        var claim = Raise(60m);

        claim.Outstanding.Amount.Should().Be(60m);
        claim.Status.Should().Be(ClaimStatus.Outstanding);
        claim.IsOpen.Should().BeTrue();
    }

    [Fact]
    public void A_claim_for_nothing_is_refused()
    {
        var act = () => Raise(0m);

        act.Should().Throw<DomainException>()
           .WithMessage("*more than nothing*");
    }

    [Fact]
    public void A_claim_must_quote_a_membership_number()
    {
        var act = () => MedicalAidClaim.Raise("CLM001", Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), "  ", null, Money.Of(60m),
            Today);

        act.Should().Throw<DomainException>()
           .WithMessage("*membership number*");
    }

    [Fact]
    public void Paying_in_full_settles_it()
    {
        var claim = Raise(60m);

        claim.Settle(Money.Of(60m), writeOffShortfall: false, null, Noon);

        claim.Status.Should().Be(ClaimStatus.Settled);
        claim.Outstanding.IsZero.Should().BeTrue();
        claim.SettledAtUtc.Should().Be(Noon);
        claim.IsOpen.Should().BeFalse();
    }

    /// <summary>
    /// The normal case, not the exception.
    /// </summary>
    /// <remarks>
    /// A scheme pays its own tariff and not the pharmacy's price, so the
    /// difference is a cost of trading with them. Recording it as a
    /// write-off rather than quietly reducing the claim is what lets a
    /// manager see which scheme costs the most.
    /// </remarks>
    [Fact]
    public void A_short_payment_writes_off_the_difference()
    {
        var claim = Raise(60m);

        claim.Settle(Money.Of(55m), writeOffShortfall: true,
                     "Scheme tariff", Noon);

        claim.Settled.Amount.Should().Be(55m);
        claim.WrittenOff.Amount.Should().Be(5m);
        claim.Outstanding.IsZero.Should().BeTrue();
        claim.Status.Should().Be(ClaimStatus.Settled);
        claim.WriteOffReason.Should().Be("Scheme tariff");
    }

    [Fact]
    public void A_short_payment_that_is_not_written_off_stays_open()
    {
        var claim = Raise(60m);

        claim.Settle(Money.Of(55m), writeOffShortfall: false, null, Noon);

        claim.Outstanding.Amount.Should().Be(5m);
        claim.Status.Should().Be(ClaimStatus.PartPaid);
        claim.IsOpen.Should().BeTrue();
    }

    [Fact]
    public void A_part_paid_claim_can_take_the_rest_later()
    {
        var claim = Raise(60m);

        claim.Settle(Money.Of(40m), writeOffShortfall: false, null, Noon);
        claim.Settle(Money.Of(20m), writeOffShortfall: false, null, Noon);

        claim.Settled.Amount.Should().Be(60m);
        claim.Status.Should().Be(ClaimStatus.Settled);
    }

    /// <summary>
    /// A remittance keyed against the wrong claim number is the usual way
    /// this would happen, and it must not be absorbed silently.
    /// </summary>
    [Fact]
    public void A_scheme_cannot_pay_more_than_it_owes()
    {
        var claim = Raise(60m);

        var act = () => claim.Settle(Money.Of(75m), false, null, Noon);

        act.Should().Throw<DomainException>()
           .WithMessage("*cannot take*");
    }

    [Fact]
    public void Giving_up_on_a_balance_needs_a_reason()
    {
        var claim = Raise(60m);

        var act = () => claim.Settle(Money.Of(55m),
            writeOffShortfall: true, "   ", Noon);

        act.Should().Throw<DomainException>()
           .WithMessage("*needs a reason*");
    }

    [Fact]
    public void A_settled_claim_takes_no_further_payment()
    {
        var claim = Raise(60m);
        claim.Settle(Money.Of(60m), false, null, Noon);

        var act = () => claim.Settle(Money.Of(10m), false, null, Noon);

        act.Should().Throw<DomainException>().WithMessage("*settled*");
    }

    /// <remarks>
    /// A rejection is the scheme saying it never owed this, which
    /// usually means the patient does. Kept apart from a write-off so a
    /// manager can tell "they paid us less" from "they would not pay".
    /// </remarks>
    [Fact]
    public void A_rejected_claim_is_written_off_in_full()
    {
        var claim = Raise(60m);

        claim.Reject("Member not on the scheme at that date", Noon);

        claim.Status.Should().Be(ClaimStatus.Rejected);
        claim.WrittenOff.Amount.Should().Be(60m);
        claim.Outstanding.IsZero.Should().BeTrue();
        claim.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void Age_is_counted_from_the_day_it_was_claimed()
    {
        var claim = Raise(60m);

        claim.AgeInDays(Today.AddDays(45)).Should().Be(45);

        // A claim raised today is not negative days old, whatever the
        // clock on a workstation says.
        claim.AgeInDays(Today.AddDays(-3)).Should().Be(0);
    }

    private static MedicalAidClaim Raise(decimal amount) =>
        MedicalAidClaim.Raise("CLM001", Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), "MEM12345", "00", Money.Of(amount), Today);
}
