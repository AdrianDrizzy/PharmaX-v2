using FluentAssertions;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Security;

namespace PharmaX.Tests.Unit.Application;

/// <summary>
/// A till whose clock was ten hours out put a day's sales on the wrong
/// day. Sign-in compares the PC with the database server first.
/// </summary>
public sealed class ClockCheckTests
{
    private static readonly DateTime Server = new(2026, 11, 21, 8, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Cat = TimeSpan.FromHours(2);

    private static string? Check(TimeSpan pcAhead, TimeSpan? pcOffset = null) =>
        ClockCheck.Problem(Server + pcAhead, pcOffset ?? Cat, new ServerTime(Server, Cat));

    [Fact]
    public void Ten_hours_out_is_blocked() =>
        Check(TimeSpan.FromHours(-10)).Should().Contain("600 minutes");

    [Fact]
    public void Ninety_seconds_out_is_fine() =>
        Check(TimeSpan.FromSeconds(90)).Should().BeNull();

    [Fact]
    public void Three_minutes_out_is_blocked() =>
        Check(TimeSpan.FromMinutes(3)).Should().Contain("3 minutes");

    [Fact]
    public void A_different_time_zone_is_blocked() =>
        Check(TimeSpan.Zero, TimeSpan.Zero).Should().Contain("time zone")
            .And.Contain("UTC+00:00").And.Contain("UTC+02:00");

    /// A database server left on UTC (a Linux box, a container) says
    /// nothing about the pharmacy's zone; only the clock is compared.
    [Fact]
    public void A_server_on_UTC_is_not_compared_for_time_zone() =>
        ClockCheck.Problem(Server, Cat, new ServerTime(Server, TimeSpan.Zero)).Should().BeNull();
}
