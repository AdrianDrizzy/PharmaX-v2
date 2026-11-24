using FluentAssertions;
using PharmaX.Application.Reporting;
using PharmaX.Application.Security;
using PharmaX.Domain.Security;

namespace PharmaX.Tests.Unit.Reporting;

/// <summary>
/// A sales summary is from one minute to another, including across midnight
/// and into a later day. The end minute is part of the range.
/// </summary>
public class SalesSummaryClockTests
{
    [Fact]
    public void Eight_to_one_the_same_day_includes_the_end_minute()
    {
        var request = Window(
            new DateTime(2026, 10, 21, 8, 0, 0),
            new DateTime(2026, 10, 21, 13, 0, 0));

        request.FromUtc.ToLocalTime().Should().Be(new DateTime(2026, 10, 21, 8, 0, 0));
        request.ToExclusiveUtc.ToLocalTime()
            .Should().Be(new DateTime(2026, 10, 21, 13, 1, 0));
    }

    [Fact]
    public void A_range_can_cross_midnight_into_the_next_day()
    {
        var request = Window(
            new DateTime(2026, 10, 20, 13, 0, 0),
            new DateTime(2026, 10, 21, 20, 0, 0));

        request.FromUtc.Should().BeBefore(request.ToExclusiveUtc);
        request.ToExclusiveUtc.ToLocalTime()
            .Should().Be(new DateTime(2026, 10, 21, 20, 1, 0));
    }

    [Fact]
    public void A_date_only_report_still_covers_the_whole_days()
    {
        var request = new ResolvedRequest(
            Preset(), new DateOnly(2026, 10, 21), new DateOnly(2026, 10, 21),
            "001", new Dictionary<string, string?>(),
            new ReportProvenance("admin", DateTime.Now, "", ""));

        request.FromUtc.Should().Be(new DateTime(2026, 10, 21, 5, 0, 0, DateTimeKind.Utc),
            "a trading day opens at 07:00 in Harare, which is 05:00 UTC");
        request.ToExclusiveUtc.Should().Be(new DateTime(2026, 10, 21, 19, 0, 0, DateTimeKind.Utc),
            "the last sale is before 21:00 in Harare, which is 19:00 UTC");
    }

    /// <summary>
    /// 22/11/2026 on the transaction list started at 00:02. Sales are allowed from 07:00 to 21:00.
    /// </summary>
    [Fact]
    public void A_trading_day_is_seven_in_the_morning_until_nine_at_night()
    {
        var request = new ResolvedRequest(
            Preset(), new DateOnly(2026, 11, 22), new DateOnly(2026, 11, 22),
            "001", new Dictionary<string, string?>(),
            new ReportProvenance("admin", DateTime.Now, "", ""));

        var justAfterMidnight = new DateTime(2026, 11, 21, 22, 2, 0, DateTimeKind.Utc);
        var opening = new DateTime(2026, 11, 22, 5, 0, 0, DateTimeKind.Utc);
        var closing = new DateTime(2026, 11, 22, 19, 0, 0, DateTimeKind.Utc);

        request.FromUtc.Should().Be(opening);
        request.ToExclusiveUtc.Should().Be(closing);
        (justAfterMidnight >= request.FromUtc && justAfterMidnight < request.ToExclusiveUtc)
            .Should().BeFalse();
        PharmacyTime.IsOpen(justAfterMidnight).Should().BeFalse();
        PharmacyTime.IsOpen(opening).Should().BeTrue();
        PharmacyTime.IsOpen(new DateTime(2026, 11, 22, 6, 0, 0, DateTimeKind.Utc)).Should().BeTrue();
        PharmacyTime.IsOpen(new DateTime(2026, 11, 22, 18, 0, 0, DateTimeKind.Utc)).Should().BeTrue();
        PharmacyTime.IsOpen(closing.AddMinutes(-1)).Should().BeTrue();
        PharmacyTime.IsOpen(closing).Should().BeFalse();
    }

    private static ResolvedRequest Window(DateTime from, DateTime to) =>
        new(Preset(), DateOnly.FromDateTime(from), DateOnly.FromDateTime(to),
            "001", new Dictionary<string, string?>(),
            new ReportProvenance("admin", DateTime.Now, "", ""),
            fromClock: from, toClock: to);

    private static ReportPreset Preset() =>
        new("pos.sales.summary", "Sales Summary", "SalesSummary",
            new Dictionary<string, string?>(), Array.Empty<string>(),
            PermissionCode.ReportOperational);
}
