using FluentAssertions;
using NSubstitute;
using PharmaX.Application.Claims;
using PharmaX.Application.Reference;
using PharmaX.Application.Security;
using PharmaX.Domain.Claims;
using PharmaX.Domain.Dispensing;
using PharmaX.Domain.Finance;
using PharmaX.Domain.Patients;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Reference;
using PharmaX.Domain.Security;
using PharmaX.Tests.Unit.Fakes;

namespace PharmaX.Tests.Unit.Application;

/// <summary>
/// What the schemes owe, and what happens when they pay.
/// </summary>
public class ClaimServiceTests
{
    private static readonly DateOnly Today = new(2026, 9, 13);

    // ---- Raising a claim from a dispensing ------------------------------

    /// <summary>
    /// The rule the whole feature turns on.
    /// </summary>
    /// <remarks>
    /// A ninety-dollar dispensing where the scheme pays sixty used to
    /// record thirty dollars of revenue. The scheme's share is now
    /// claimed, and it is claimed for exactly the contribution - not the
    /// tariff, and not the co-payment.
    /// </remarks>
    [Fact]
    public void A_dispensing_claims_the_scheme_contribution()
    {
        var patient = OnScheme();
        var dispensing = Dispensed(tariff: 90m, scheme: 60m);

        var claim = ClaimService.Build("CLM001", patient, dispensing, Today);

        claim.Should().NotBeNull();
        claim!.Claimed.Amount.Should().Be(60m);
        claim.MembershipNumber.Should().Be("MEM4417");
        claim.DependantCode.Should().Be("00");
        claim.SchemeId.Should().Be(patient.MedicalAidSchemeId!.Value);
    }

    [Fact]
    public void A_cash_patient_raises_no_claim()
    {
        var patient = Patient.Create("PF1", "Moyo", "Grace");
        var dispensing = Dispensed(tariff: 90m, scheme: 60m);

        ClaimService.Build("CLM001", patient, dispensing, Today)
            .Should().BeNull();
    }

    /// <remarks>
    /// A scheme member can still be dispensed something the scheme does
    /// not cover, and billing them nothing is not a claim.
    /// </remarks>
    [Fact]
    public void A_script_the_scheme_covers_nothing_of_raises_no_claim()
    {
        var patient = OnScheme();
        var dispensing = Dispensed(tariff: 90m, scheme: 0m);

        ClaimService.Build("CLM001", patient, dispensing, Today)
            .Should().BeNull();
    }

    /// <remarks>
    /// A membership number is what a scheme matches a claim against, so
    /// a patient carrying a scheme and no number cannot be claimed for.
    /// </remarks>
    [Fact]
    public void A_scheme_without_a_membership_number_raises_no_claim()
    {
        var patient = Patient.Create("PF1", "Moyo", "Grace");
        var dispensing = Dispensed(tariff: 90m, scheme: 60m);

        patient.IsOnMedicalAid.Should().BeFalse();

        ClaimService.Build("CLM001", patient, dispensing, Today)
            .Should().BeNull();
    }

    /// <summary>
    /// The VAT split follows the product, not the claim total.
    /// </summary>
    /// <remarks>
    /// A script can put a zero-rated medicine beside a standard-rated
    /// one, and one rate across the whole claim would misstate what is
    /// owed to the revenue authority on every mixed script.
    /// </remarks>
    [Fact]
    public void The_vat_split_is_worked_out_per_line()
    {
        var dispensing = Dispensing.Begin(Guid.NewGuid(), Guid.NewGuid(),
                                          Guid.NewGuid(), 0);

        var rated = Guid.NewGuid();
        var zeroRated = Guid.NewGuid();

        Line(dispensing, rated, tariff: 46m, scheme: 46m);
        Line(dispensing, zeroRated, tariff: 40m, scheme: 40m);

        var splits = ClaimService.SplitOf(dispensing,
            new Dictionary<Guid, decimal> { [rated] = 15m, [zeroRated] = 0m });

        splits.Should().HaveCount(2);

        // 46.00 inclusive of 15% is 40.00 net and 6.00 of VAT.
        splits[0].VatAmount.Amount.Should().Be(6.00m);

        // Nothing on the zero-rated line.
        splits[1].VatAmount.Amount.Should().Be(0m);
        splits[1].NetAmount.Amount.Should().Be(40m);
    }

    [Fact]
    public void A_line_the_scheme_pays_nothing_on_is_left_out_of_the_split()
    {
        var dispensing = Dispensing.Begin(Guid.NewGuid(), Guid.NewGuid(),
                                          Guid.NewGuid(), 0);

        var covered = Guid.NewGuid();
        var notCovered = Guid.NewGuid();

        Line(dispensing, covered, tariff: 46m, scheme: 46m);
        Line(dispensing, notCovered, tariff: 40m, scheme: 0m);

        ClaimService.SplitOf(dispensing,
            new Dictionary<Guid, decimal> { [covered] = 15m })
            .Should().ContainSingle();
    }

    // ---- Settling -------------------------------------------------------

    [Fact]
    public async Task A_remittance_settles_the_claims_on_it()
    {
        var builder = new ClaimBuilder();
        var claim = builder.Outstanding(60m);

        var result = await builder.Build().SettleAsync(builder.SchemeId,
            Today, "RA-9912", new[]
            {
                new RemittanceLineDto(claim.Id, 60m, false)
            });

        result.IsSuccess.Should().BeTrue();
        result.Value.Paid.Should().Be(60m);
        result.Value.WrittenOff.Should().Be(0m);

        claim.Status.Should().Be(ClaimStatus.Settled);
        builder.Ledger.AllBalance.Should().BeTrue();
        builder.Ledger.NetOf(LedgerCode.Bank).Should().Be(60m);
    }

    /// <summary>
    /// The normal case: the scheme pays its tariff, not the pharmacy's
    /// price, and the difference is a cost of trading with them.
    /// </summary>
    [Fact]
    public async Task A_short_payment_is_written_off_where_asked()
    {
        var builder = new ClaimBuilder();
        var claim = builder.Outstanding(60m);

        var result = await builder.Build().SettleAsync(builder.SchemeId,
            Today, "RA-9912", new[]
            {
                new RemittanceLineDto(claim.Id, 55m, true)
            });

        result.Value.Paid.Should().Be(55m);
        result.Value.WrittenOff.Should().Be(5m);

        claim.Status.Should().Be(ClaimStatus.Settled);
        builder.Ledger.NetOf(LedgerCode.ClaimsWrittenOff).Should().Be(5m);
        builder.Ledger.NetOf(LedgerCode.MedicalAidDebtors).Should().Be(-60m);
    }

    /// <remarks>
    /// A mis-keyed claim number on a remittance is the usual way one
    /// scheme's payment would land against another's claim, and it must
    /// be refused rather than absorbed.
    /// </remarks>
    [Fact]
    public async Task A_remittance_cannot_settle_another_schemes_claim()
    {
        var builder = new ClaimBuilder();
        var claim = builder.Outstanding(60m, schemeId: Guid.NewGuid());

        var result = await builder.Build().SettleAsync(builder.SchemeId,
            Today, null, new[]
            {
                new RemittanceLineDto(claim.Id, 60m, false)
            });

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("CLM-004");
    }

    [Fact]
    public async Task An_empty_remittance_is_refused()
    {
        var builder = new ClaimBuilder();

        var result = await builder.Build().SettleAsync(builder.SchemeId,
            Today, null, Array.Empty<RemittanceLineDto>());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("CLM-001");
    }

    [Fact]
    public async Task A_rejected_claim_is_written_off_and_leaves_the_queue()
    {
        var builder = new ClaimBuilder();
        var claim = builder.Outstanding(60m);

        var result = await builder.Build().RejectAsync(claim.Id,
            "Member not on the scheme at that date");

        result.IsSuccess.Should().BeTrue();
        claim.Status.Should().Be(ClaimStatus.Rejected);

        builder.Ledger.NetOf(LedgerCode.ClaimsWrittenOff).Should().Be(60m);
        builder.Ledger.NetOf(LedgerCode.Bank).Should().Be(0m);

        (await builder.Claims.OutstandingAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Settling_a_remittance_needs_permission_to_post()
    {
        var builder = new ClaimBuilder
        {
            Permissions = new FakePermissionRepository(
                granted: new[] { PermissionCode.LedgerRead })
        };

        var claim = builder.Outstanding(60m);
        var sut = builder.Build();

        (await sut.OutstandingAsync()).IsSuccess.Should().BeTrue();

        var act = () => sut.SettleAsync(builder.SchemeId, Today, null,
            new[] { new RemittanceLineDto(claim.Id, 60m, false) });

        await act.Should().ThrowAsync<PermissionDeniedException>();
    }

    // ---- Builders --------------------------------------------------------

    private static Patient OnScheme()
    {
        var patient = Patient.Create("PF1", "Moyo", "Grace");
        patient.SetMedicalAid(Guid.NewGuid(), "MEM4417", "00");
        return patient;
    }

    private static Dispensing Dispensed(decimal tariff, decimal scheme)
    {
        var dispensing = Dispensing.Begin(Guid.NewGuid(), Guid.NewGuid(),
                                          Guid.NewGuid(), 0);

        Line(dispensing, Guid.NewGuid(), tariff, scheme);
        return dispensing;
    }

    private static void Line(Dispensing dispensing, Guid productId,
                             decimal tariff, decimal scheme)
    {
        var line = dispensing.AddLine(DispensingLine.Create(
            productId, "Amoxicillin 500mg", 21m,
            "One three times a day", Money.Of(tariff)));

        line.SetSchemeContribution(Money.Of(scheme));
    }
}

internal sealed class ClaimBuilder
{
    private static readonly DateTime Noon =
        new(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);

    public Guid SchemeId { get; } = Guid.NewGuid();

    public InMemoryClaimRepository Claims { get; } = new();
    public InMemoryLedgerRepository Ledger { get; } = new();
    public FakeDocumentNumbering Numbering { get; } = new();
    public FakeAuditWriter Audit { get; } = new();
    public FakeUnitOfWork UnitOfWork { get; } = new();
    public FakeCurrentUser User { get; } = new();
    public FakeClock Clock { get; } = new(Noon);

    public FakePermissionRepository Permissions { get; set; } =
        new(granted: new[]
            { PermissionCode.LedgerRead, PermissionCode.LedgerPost });

    public MedicalAidClaim Outstanding(decimal amount, Guid? schemeId = null) =>
        Claims.Add(MedicalAidClaim.Raise($"CLM{Claims.All.Count + 1:000}",
            schemeId ?? SchemeId, Guid.NewGuid(), Guid.NewGuid(),
            "MEM4417", "00", Money.Of(amount), new DateOnly(2026, 9, 13)));

    public ClaimService Build()
    {
        var reference = Substitute.For<IReferenceRepository>();

        reference.GetAsync(ReferenceKind.MedicalAidScheme, Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(MedicalAidScheme.Create("ALPHA", "Alpha Medical Aid"));

        return new ClaimService(Claims, reference,
            Substitute.For<PharmaX.Application.Repositories
                .IPatientRepository>(),
            Substitute.For<PharmaX.Application.Dispensing
                .IDispensingRepository>(),
            new PharmaX.Application.Finance.JournalPoster(Ledger, Numbering,
                new PharmaX.Application.Abstractions.PharmaXOptions(), Clock),
            new PermissionService(User, Permissions, Audit),
            Audit, UnitOfWork, Clock);
    }
}
