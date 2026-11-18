using FluentAssertions;
using PharmaX.Application.Licensing;
using PharmaX.Application.Security;
using PharmaX.Domain.Security;
using PharmaX.Tests.Unit.Fakes;

namespace PharmaX.Tests.Unit.Security;

public sealed class LicenceGateTests
{
    private static LicenceGate Gate(LicenceStatus status)
    {
        var gate = new LicenceGate();
        gate.Update(new(status, new DateOnly(2026, 1, 1), MembershipPlan.Monthly, false));
        return gate;
    }

    private static PermissionService Service(ILicenceGate gate) =>
        new(new FakeCurrentUser(), new FakePermissionRepository(),
            new FakeAuditWriter(), gate);

    [Fact]
    public void Read_only_denies_selling() =>
        Gate(LicenceStatus.ReadOnly).Allows(PermissionCode.SaleCreate).Should().BeFalse();

    [Theory]
    [InlineData(PermissionCode.ProductRead)]
    [InlineData(PermissionCode.ReportFinancial)]
    [InlineData(PermissionCode.SettingsChange)]
    public void Read_only_allows_lookups_and_fixing(string code) =>
        Gate(LicenceStatus.ClockWrong).Allows(code).Should().BeTrue();

    [Fact]
    public void Grace_allows_everything() =>
        Gate(LicenceStatus.Grace).Allows(PermissionCode.SaleCreate).Should().BeTrue();

    [Fact]
    public void New_gate_blocks_nothing() =>
        new LicenceGate().Allows(PermissionCode.SaleCreate).Should().BeTrue();

    [Fact]
    public void Held_permission_is_denied_when_read_only() =>
        Service(Gate(LicenceStatus.ReadOnly)).Check(PermissionCode.SaleCreate)
            .Should().Be(PermissionOutcome.Denied);

    [Fact]
    public void Demand_says_why_when_read_only() =>
        FluentActions.Invoking(() =>
                Service(Gate(LicenceStatus.ReadOnly)).Demand(PermissionCode.SaleCreate))
            .Should().Throw<PermissionDeniedException>()
            .WithMessage("PharmaX is read-only until the membership is renewed.");
}
