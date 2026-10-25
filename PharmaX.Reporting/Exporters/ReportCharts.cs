using System.Globalization;
using System.Security;
using System.Text;
using PharmaX.Application.Reporting;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace PharmaX.Reporting.Exporters;

/// <summary>
/// The graph a report gets, and the drawing of it. One SVG serves the PDF,
/// the workbook (as a picture) and the preview on screen, so the three
/// never disagree.
/// </summary>
public sealed class ReportCharts : IChartRenderer
{
    /// Most bars a chart shows; the rest are the smallest, and left out.
    public const int MostBars = 15;

    /// Most points a line shows: two months of days.
    public const int MostPoints = 62;

    /// <summary>
    /// The columns a graph is drawn from: a summary's grouping column
    /// against its money. Detail reports - a row per sale, per batch - are
    /// lists to read, not shapes to see, and get none.
    /// </summary>
    private static readonly (string Label, string[] Values)[] Shapes =
    {
        ("Group", new[] { "Sales" }),
        ("Tender", new[] { "Net", "Amount", "Sales" }),
        ("TaxCode", new[] { "Vat" }),
        ("Key", new[] { "Value" })
    };

    /// <summary>
    /// The chart for a result, or null where none would help: a detail
    /// report, a single row, or nothing to measure.
    /// </summary>
    /// <param name="valueKey">
    /// The column to graph - "Margin" for a profit report - where not the
    /// usual one for the report's shape.
    /// </param>
    public static ReportChart? Suggest(ReportResult report, string? valueKey = null)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (report.Rows.Count < 2 || report.Columns.Count == 0) return null;

        var label = report.Columns[0];

        foreach (var (key, values) in Shapes)
        {
            if (!string.Equals(label.Key, key, StringComparison.Ordinal)) continue;

            var value = (valueKey is null ? values : new[] { valueKey })
                .Select(v => report.Columns.FirstOrDefault(c => c.Key == v))
                .FirstOrDefault(c => c is not null);

            if (value is null) return null;

            var points = report.Rows
                .Select(row => new ChartPoint(
                    Text(row.TryGetValue(label.Key, out var l) ? l : null, label.Format),
                    row.TryGetValue(value.Key, out var v) ? Number(v) : 0m))
                .Where(p => p.Label.Length > 0)
                .ToList();

            if (points.Count < 2) return null;

            // Over time it is a line, in the order it happened; anything
            // else is bars, largest first.
            var overTime = label.Header is "Day" or "Month" or "Date"
                           || report.Rows.Any(r => r.TryGetValue(label.Key, out var x)
                                                   && x is DateTime or DateOnly);

            var title = $"{value.Header} by {label.Header.ToLowerInvariant()}";

            // The week and the clock keep their own order, every bar shown.
            if (label.Header is "Day of week" or "Hour")
                return new ReportChart(title, ChartKind.Bar, value.Header, points);

            return overTime
                ? new ReportChart(title, ChartKind.Line, value.Header,
                    points.TakeLast(MostPoints).ToList())
                : new ReportChart(title, ChartKind.Bar, value.Header,
                    points.OrderByDescending(p => p.Value).Take(MostBars).ToList());
        }

        return null;
    }

    private static string Text(object? value, string? format) => value switch
    {
        null => "",
        IFormattable f when format is not null => f.ToString(format, CultureInfo.CurrentCulture),
        DateTime d => d.ToString("dd/MM", CultureInfo.CurrentCulture),
        _ => value.ToString()?.Trim() ?? ""
    };

    private static decimal Number(object? value) => value switch
    {
        decimal d => d,
        int i => i,
        long l => l,
        double x => (decimal)x,
        float f => (decimal)f,
        _ => 0m
    };

    /// <summary>The chart as SVG, <paramref name="width"/> by <paramref name="height"/>.</summary>
    public static string Svg(ReportChart chart, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(chart);

        const string ink = "#1F2937", muted = "#6B7280", grid = "#E5E7EB",
                     bar = "#2F6DB5", negative = "#B42318";
        const int left = 70, right = 16, top = 34, bottom = 58;

        var c = CultureInfo.InvariantCulture;
        var svg = new StringBuilder();
        var plotW = width - left - right;
        var plotH = height - top - bottom;
        var points = chart.Points;

        var max = Math.Max(0m, points.Select(p => p.Value).DefaultIfEmpty(0m).Max());
        var min = Math.Min(0m, points.Select(p => p.Value).DefaultIfEmpty(0m).Min());
        var (tickStep, ticks) = Ticks(min, max);
        var top0 = tickStep * Math.Ceiling(max / tickStep);
        var bottom0 = tickStep * Math.Floor(min / tickStep);
        if (top0 == bottom0) top0 = bottom0 + 1m;

        double Y(decimal v) => top + (double)((top0 - v) / (top0 - bottom0)) * plotH;

        svg.Append(c, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\" font-family=\"Segoe UI, Arial, sans-serif\">");
        svg.Append(c, $"<rect width=\"{width}\" height=\"{height}\" fill=\"#FFFFFF\"/>");
        svg.Append(c, $"<text x=\"{left}\" y=\"20\" font-size=\"14\" font-weight=\"bold\" fill=\"{ink}\">{Escape(chart.Title)}</text>");

        for (var i = 0; i <= ticks; i++)
        {
            var v = bottom0 + tickStep * i;
            if (v > top0) break;
            var y = Y(v);
            svg.Append(c, $"<line x1=\"{left}\" x2=\"{width - right}\" y1=\"{y:0.#}\" y2=\"{y:0.#}\" stroke=\"{(v == 0m ? muted : grid)}\" stroke-width=\"1\"/>");
            svg.Append(c, $"<text x=\"{left - 6}\" y=\"{y + 4:0.#}\" font-size=\"10\" text-anchor=\"end\" fill=\"{muted}\">{Escape(Short(v))}</text>");
        }

        var n = Math.Max(1, points.Count);
        var slot = (double)plotW / n;
        var labelEvery = Math.Max(1, (int)Math.Ceiling(n / (plotW / 46d)));

        if (chart.Kind == ChartKind.Bar)
        {
            var barW = Math.Max(4, slot * 0.68);

            for (var i = 0; i < points.Count; i++)
            {
                var p = points[i];
                var x = left + slot * i + (slot - barW) / 2;
                var y1 = Y(Math.Max(p.Value, 0m));
                var y2 = Y(Math.Min(p.Value, 0m));
                svg.Append(c, $"<rect x=\"{x:0.#}\" y=\"{y1:0.#}\" width=\"{barW:0.#}\" height=\"{Math.Max(1, y2 - y1):0.#}\" rx=\"2\" fill=\"{(p.Value < 0 ? negative : bar)}\"/>");

                if (n <= 12)
                    svg.Append(c, $"<text x=\"{x + barW / 2:0.#}\" y=\"{y1 - 4:0.#}\" font-size=\"10\" text-anchor=\"middle\" fill=\"{ink}\">{Escape(Short(p.Value))}</text>");

                if (i % labelEvery == 0) Label(svg, c, x + barW / 2, top + plotH, p.Label, muted);
            }
        }
        else
        {
            var path = new StringBuilder();

            for (var i = 0; i < points.Count; i++)
            {
                var x = left + slot * i + slot / 2;
                path.Append(c, $"{(i == 0 ? "M" : "L")}{x:0.#},{Y(points[i].Value):0.#} ");
                if (i % labelEvery == 0) Label(svg, c, x, top + plotH, points[i].Label, muted);
            }

            svg.Append(c, $"<path d=\"{path.ToString().Trim()}\" fill=\"none\" stroke=\"{bar}\" stroke-width=\"2.5\" stroke-linejoin=\"round\"/>");

            for (var i = 0; i < points.Count; i++)
                svg.Append(c, $"<circle cx=\"{left + slot * i + slot / 2:0.#}\" cy=\"{Y(points[i].Value):0.#}\" r=\"3\" fill=\"{bar}\"/>");
        }

        svg.Append("</svg>");
        return svg.ToString();
    }

    /// An axis label under a bar or point, slanted when they crowd.
    private static void Label(StringBuilder svg, CultureInfo c, double x, double baseline,
                              string text, string colour)
    {
        var shown = text.Length > 14 ? text[..13] + "…" : text;
        svg.Append(c, $"<text x=\"{x:0.#}\" y=\"{baseline + 14:0.#}\" font-size=\"10\" text-anchor=\"end\" fill=\"{colour}\" transform=\"rotate(-30 {x:0.#} {baseline + 14:0.#})\">{Escape(shown)}</text>");
    }

    /// A round step that gives four to six gridlines.
    private static (decimal Step, int Count) Ticks(decimal min, decimal max)
    {
        var span = (double)Math.Max(1m, max - min);
        var raw = span / 5;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var step = new[] { 1d, 2d, 2.5d, 5d, 10d }.Select(m => m * magnitude).First(s => s >= raw);
        return ((decimal)step, (int)Math.Ceiling(span / step) + 1);
    }

    /// 12500 as "12.5k": axis labels have little room.
    private static string Short(decimal v)
    {
        var a = Math.Abs(v);
        var c = CultureInfo.CurrentCulture;
        return a >= 1_000_000m ? (v / 1_000_000m).ToString("0.#", c) + "m"
            : a >= 10_000m ? (v / 1_000m).ToString("0.#", c) + "k"
            : v.ToString(a >= 100m ? "N0" : "0.##", c);
    }

    private static string Escape(string text) => SecurityElement.Escape(text) ?? "";

    /// <summary>The chart as a PNG, drawn twice the size for a sharp picture.</summary>
    public byte[] Png(ReportChart chart, int width, int height) => Picture(chart, width, height);

    public static byte[] Picture(ReportChart chart, int width, int height)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var svg = Svg(chart, width, height);

        return Document.Create(doc => doc.Page(page =>
            {
                page.Size(width, height, Unit.Point);
                page.Margin(0);
                page.Content().Svg(svg);
            }))
            .GenerateImages(new ImageGenerationSettings
            {
                ImageFormat = ImageFormat.Png,
                RasterDpi = 144
            })
            .First();
    }
}
