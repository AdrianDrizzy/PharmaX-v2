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
        Check(TimeSpan.Zero, TimeSpan.Zero).Should().Contain("Harare")
            .And.Contain("UTC+00:00");

    /// A database server left on UTC must not pull the tills off Harare.
    [Fact]
    public void A_server_on_UTC_does_not_move_the_till_off_Harare() =>
        ClockCheck.Problem(Server, Cat, new ServerTime(Server, TimeSpan.Zero)).Should().BeNull();

    [Fact]
    public void A_till_on_UTC_is_blocked_even_when_the_server_is_too() =>
        ClockCheck.Problem(Server, TimeSpan.Zero, new ServerTime(Server, TimeSpan.Zero))
            .Should().Contain("Harare");

    [Fact]
    public void Setting_the_pc_clock_back_is_caught()
    {
        var started = new DateTime(2026, 11, 22, 8, 0, 0, DateTimeKind.Utc);
        ClockCheck.Moved(started, TimeSpan.FromMinutes(30), started.AddHours(-6))
            .Should().BeTrue();
        ClockCheck.Moved(started, TimeSpan.FromMinutes(30), started.AddMinutes(31))
            .Should().BeFalse();
    }
}
