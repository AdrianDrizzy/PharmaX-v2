using FluentAssertions;
using PharmaX.Application.Licensing;

namespace PharmaX.Tests.Unit.Application;

public sealed class LicenceStateTests
{
    private static readonly DateOnly End = new(2026, 12, 31);

    [Theory]
    [InlineData(-8, LicenceStatus.Active)]
    [InlineData(-7, LicenceStatus.Expiring)]
    [InlineData(0, LicenceStatus.Expiring)]   // the last valid day
    [InlineData(1, LicenceStatus.Grace)]
    [InlineData(7, LicenceStatus.Grace)]
    [InlineData(8, LicenceStatus.ReadOnly)]
    public void State_by_day(int offset, LicenceStatus expected) =>
        LicenceState.Evaluate(End, End.AddDays(offset), null).Should().Be(expected);

    [Fact]
    public void No_licence_is_read_only() =>
        LicenceState.Evaluate(null, End, null).Should().Be(LicenceStatus.ReadOnly);

    [Fact]
    public void Clock_set_back_two_days_is_clock_wrong() =>
        LicenceState.Evaluate(End, End.AddDays(-30), End.AddDays(-28))
            .Should().Be(LicenceStatus.ClockWrong);

    [Fact]
    public void Clock_one_day_behind_is_tolerated() =>
        LicenceState.Evaluate(End, End.AddDays(-30), End.AddDays(-29))
            .Should().Be(LicenceStatus.Active);

    [Fact]
    public void Active_has_no_banner() =>
        LicenceState.BannerText(new(LicenceStatus.Active, End, MembershipPlan.Yearly, false))
            .Should().BeNull();

    [Fact]
    public void Grace_banner_names_the_read_only_date() =>
        LicenceState.BannerText(new(LicenceStatus.Grace, End, MembershipPlan.Monthly, false))
            .Should().Contain("31 Dec 2026").And.Contain("8 Jan 2027");

    [Fact]
    public void Read_only_snapshot_is_read_only() =>
        new LicenceSnapshot(LicenceStatus.ClockWrong, End, null, false).IsReadOnly
            .Should().BeTrue();
}
