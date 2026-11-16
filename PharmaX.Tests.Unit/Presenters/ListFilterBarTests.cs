using FluentAssertions;
using PharmaX.Desktop.Controls;
using Xunit;

namespace PharmaX.Tests.Unit.Presenters;

/// <summary>
/// The Filter drawer's date ranges: whole local days, the end exclusive,
/// so the query's "from &lt;= stamp &lt; to" takes in all of the last day.
/// </summary>
public sealed class ListFilterBarTests
{
    private static readonly DateTime Today = new(2026, 11, 16);

    [Theory]
    [InlineData(1, "2026-11-16")]   // Today
    [InlineData(2, "2026-11-10")]   // Last 7 days: today and the six before it
    [InlineData(3, "2026-10-18")]   // Last 30 days
    [InlineData(4, "2026-11-01")]   // This month
    public void A_preset_runs_from_its_first_day_to_the_end_of_today(int range, string from)
    {
        var (start, end) = ListFilterBar.Range(range, Today, Today, Today);

        start.Should().Be(DateTime.Parse(from, System.Globalization.CultureInfo.InvariantCulture));
        end.Should().Be(Today.AddDays(1));
    }

    [Fact]
    public void All_time_has_no_bounds() =>
        ListFilterBar.Range(0, Today, Today, Today).Should().Be(((DateTime?)null, (DateTime?)null));

    [Fact]
    public void From_to_takes_in_both_days_whichever_way_round_they_are_picked()
    {
        var first = new DateTime(2026, 11, 2);
        var last = new DateTime(2026, 11, 5);
        var custom = ListFilterBar.Ranges.Length - 1;

        ListFilterBar.Range(custom, Today, first, last).Should().Be(((DateTime?)first, (DateTime?)last.AddDays(1)));
        ListFilterBar.Range(custom, Today, last, first).Should().Be(((DateTime?)first, (DateTime?)last.AddDays(1)));
    }
}
