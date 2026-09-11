using PharmaX.Domain.Exceptions;

namespace PharmaX.Domain.Primitives;

/// <summary>
/// An inclusive span of days. Report parameters and till sessions both need
/// one, and both got it subtly wrong when each built its own.
/// </summary>
public readonly record struct DateRange
{
    public DateOnly From { get; }
    public DateOnly To { get; }

    private DateRange(DateOnly from, DateOnly to)
    {
        From = from;
        To = to;
    }

    public static DateRange Of(DateOnly from, DateOnly to)
    {
        if (to < from)
            throw new DomainException(
                "The end of a date range cannot fall before its start.");

        return new DateRange(from, to);
    }

    public static DateRange SingleDay(DateOnly day) => new(day, day);

    public static DateRange MonthOf(DateOnly day) =>
        new(new DateOnly(day.Year, day.Month, 1),
            new DateOnly(day.Year, day.Month,
                         DateTime.DaysInMonth(day.Year, day.Month)));

    public int Days => To.DayNumber - From.DayNumber + 1;

    public bool Contains(DateOnly day) => day >= From && day <= To;

    // Half-open in UTC, which is what a BETWEEN on a timestamp column needs
    // if the last second of the final day is not to be silently dropped.
    public DateTime FromUtc => From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
    public DateTime ToExclusiveUtc =>
        To.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    public override string ToString() => $"{From:yyyy-MM-dd} to {To:yyyy-MM-dd}";
}
