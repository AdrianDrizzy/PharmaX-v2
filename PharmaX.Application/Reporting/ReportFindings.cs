using System.Globalization;

namespace PharmaX.Application.Reporting;

/// <summary>
/// A few plain sentences on what a previewed report's graph shows: the
/// best and the worst, how far apart they are, and how the rest compare.
/// </summary>
/// <remarks>
/// Worked out from the figures, not written by the assistant: a 2 GB model
/// on a shop PC gets arithmetic wrong often enough that a summary from it
/// would need checking against the table, which defeats the point.
/// </remarks>
public static class ReportFindings
{
    /// Fewer bars than this means every row is in the graph, so a share of
    /// the total is a share of everything (the graph keeps the top 15).
    private const int AllShown = 15;

    private static readonly string[] Weekend = { "Saturday", "Sunday" };

    public static IReadOnlyList<string> For(ReportChart? chart)
    {
        if (chart is null || chart.Points.Count < 2) return Array.Empty<string>();

        return chart.Kind == ChartKind.Line ? OverTime(chart) : Compared(chart);
    }

    private static List<string> Compared(ReportChart chart)
    {
        var what = chart.ValueLabel.ToLowerInvariant();
        var points = chart.Points;
        var best = points.MaxBy(p => p.Value)!;
        var worst = points.MinBy(p => p.Value)!;
        var total = points.Sum(p => p.Value);
        var average = total / points.Count;
        var findings = new List<string>();

        var share = points.Count < AllShown && total > 0
            ? $", {Percent(best.Value / total)} of the total"
            : "";
        findings.Add($"{best.Label} is the highest with {Money(best.Value)} in {what}{share}.");

        if (average > 0)
            findings.Add($"That is {Percent(best.Value / average - 1)} above the average of "
                         + $"{Money(average)} per {Noun(chart)}.");

        findings.Add($"{worst.Label} is the lowest with {Money(worst.Value)}"
                     + (worst.Value > 0
                         ? $"; {best.Label} brings in {Times(best.Value / worst.Value)} as much."
                         : "."));

        var ranked = points.OrderByDescending(p => p.Value).Select(p => p.Label).ToList();
        if (ranked.Count > 2)
            findings.Add("From highest to lowest: " + string.Join(", ", ranked) + ".");

        // The week has a question of its own: is the weekend busier?
        var weekend = points.Where(p => Weekend.Contains(p.Label, StringComparer.OrdinalIgnoreCase)).ToList();
        var weekdays = points.Except(weekend).ToList();

        if (weekend.Count > 0 && weekdays.Count > 0)
        {
            var end = weekend.Average(p => p.Value);
            var week = weekdays.Average(p => p.Value);

            if (week > 0)
                findings.Add(end >= week
                    ? $"A weekend day averages {Money(end)}, {Percent(end / week - 1)} more than a weekday ({Money(week)})."
                    : $"A weekday averages {Money(week)}, {Percent(week / Math.Max(end, 0.01m) - 1)} more than a weekend day ({Money(end)}).");
        }

        var below = points.Count(p => p.Value < average);
        if (below > 0 && below < points.Count)
            findings.Add($"{below} of the {points.Count} are below the average.");

        return findings;
    }

    private static List<string> OverTime(ReportChart chart)
    {
        var points = chart.Points;
        var what = chart.ValueLabel.ToLowerInvariant();
        var best = points.MaxBy(p => p.Value)!;
        var worst = points.MinBy(p => p.Value)!;
        var first = points[0];
        var last = points[^1];
        var findings = new List<string>
        {
            $"The highest {what} was {Money(best.Value)} on {best.Label}; "
            + $"the lowest {Money(worst.Value)} on {worst.Label}.",
            $"Average {Money(points.Average(p => p.Value))} per {Noun(chart)} "
            + $"over {points.Count} {Noun(chart)}s, {Money(points.Sum(p => p.Value))} in all."
        };

        if (first.Value > 0)
        {
            var change = last.Value / first.Value - 1;
            findings.Add(change == 0
                ? $"{last.Label} ended level with {first.Label}."
                : $"From {first.Label} to {last.Label} it went {(change > 0 ? "up" : "down")} "
                  + $"{Percent(Math.Abs(change))}.");
        }

        // The second half against the first: a trend that one odd day
        // at either end does not decide.
        var half = points.Count / 2;
        if (half >= 2)
        {
            var early = points.Take(half).Average(p => p.Value);
            var late = points.Skip(points.Count - half).Average(p => p.Value);

            if (early > 0)
                findings.Add($"The second half of the period averaged {Percent(Math.Abs(late / early - 1))} "
                             + $"{(late >= early ? "more" : "less")} than the first.");
        }

        return findings;
    }

    /// What one bar is, from the graph's title: "Sales by tender" gives
    /// "tender", and "by day of week" gives "day".
    private static string Noun(ReportChart chart)
    {
        var by = chart.Title.Split(" by ", 2).ElementAtOrDefault(1)?.ToLowerInvariant() ?? "";
        return by switch
        {
            "day of week" or "date" => "day",
            "" => "item",
            _ => by
        };
    }

    private static string Money(decimal value) =>
        value.ToString("N2", CultureInfo.CurrentCulture);

    private static string Percent(decimal fraction) =>
        (fraction * 100m).ToString("0", CultureInfo.CurrentCulture) + "%";

    private static string Times(decimal ratio) =>
        ratio.ToString("0.0", CultureInfo.CurrentCulture) + " times";
}
