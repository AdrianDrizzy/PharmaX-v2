using FluentAssertions;
using PharmaX.Application.Reporting;
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

        request.FromUtc.ToLocalTime().TimeOfDay.Should().Be(TimeSpan.Zero);
        request.ToExclusiveUtc.ToLocalTime().Date
            .Should().Be(new DateTime(2026, 10, 22));
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
