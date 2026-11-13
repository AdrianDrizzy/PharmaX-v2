using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Reporting;
using PharmaX.Application.Security;

namespace PharmaX.Desktop.Features.Assistant;

public enum ReportOutcome { NotAReport, Unclear, NeedsPeriod, Ran }

/// <param name="Name">The report chosen, as the reports screen lists it.</param>
public sealed record ReportAnswer(ReportOutcome Outcome, string Name = "", ReportResult? Result = null);

/// <summary>
/// A report asked for in Ask PharmaX - "sales for March 2026", "the cash-up
/// for yesterday" - chosen as the reports screen's Ask box chooses one,
/// and run.
/// </summary>
/// <remarks>
/// Only from the reports this user may run; the runner demands the
/// permission again.
/// </remarks>
public sealed partial class AssistantReports
{
    private readonly IReportSuggester _suggester;
    private readonly IReportRunner _runner;
    private readonly IPresetCatalogue _presets;
    private readonly IReportLookups _lookups;
    private readonly IPermissionService _permissions;
    private readonly IPdfWriter _pdfs;
    private readonly IWorkbookWriter _workbooks;
    private readonly IChartRenderer _charts;
    private readonly string _branch;

    private ReportSuggestionContext? _context;

    /// The reports that show what is there now, so need no period.
    private static readonly HashSet<string> Undated = new(StringComparer.Ordinal)
        { "StockOnHand", "StockByBatch", "CostCheck" };

    public AssistantReports(IReportSuggester suggester, IReportRunner runner, IPresetCatalogue presets,
                            IReportLookups lookups, IPermissionService permissions, IPdfWriter pdfs,
                            IWorkbookWriter workbooks, IChartRenderer charts, PharmaXOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _suggester = suggester;
        _runner = runner;
        _presets = presets;
        _lookups = lookups;
        _permissions = permissions;
        _pdfs = pdfs;
        _workbooks = workbooks;
        _charts = charts;
        _branch = options.Workstation.BranchCode;
    }

    public bool IsAvailable => _suggester.IsAvailable;
    public bool IsReady => _suggester.IsReady;

    public async Task<ReportAnswer> AskAsync(string request, CancellationToken ct = default)
    {
        if (_context is null)
        {
            var allowed = _presets.All().Where(p => _permissions.Has(p.PermissionCode)).ToList();
            _context = await Reports.ReportsPresenter.AskContextAsync(_presets, _lookups, allowed);
        }

        if (_context.Presets.Count == 0) return new ReportAnswer(ReportOutcome.NotAReport);

        var suggestion = await _suggester.SuggestAsync(request, _context, ct);
        if (!suggestion.Understood) return new ReportAnswer(ReportOutcome.Unclear);
        if (suggestion.PresetKey is not { } key
            || _context.Presets.FirstOrDefault(p => p.Key == key) is not { } preset)
            return new ReportAnswer(ReportOutcome.NotAReport);

        if (suggestion.From is null && !Undated.Contains(_presets.Resolve(key).DefinitionKey))
            return new ReportAnswer(ReportOutcome.NeedsPeriod, preset.DisplayName);

        var today = _context.Today;
        var result = await _runner.RunAsync(new ReportRequest(key, _branch,
            suggestion.From ?? today, suggestion.To ?? today,
            suggestion.Slicers.ToDictionary(s => s.Key, s => (string?)s.Value, StringComparer.Ordinal)), ct);

        return new ReportAnswer(ReportOutcome.Ran, preset.DisplayName, result);
    }

    public byte[]? Chart(ReportResult report) =>
        report.Chart is { } chart ? _charts.Png(chart, 640, 320) : null;

    /// The whole report, letterhead and graph, as the reports screen exports it.
    public void Save(ReportResult report, bool pdf, string path)
    {
        if (pdf) _pdfs.WriteTo(report, path);
        else _workbooks.WriteTo(report, path);
    }

    /// <summary>
    /// "Sales for March", "give me the cash-up report", "how much did we
    /// take yesterday" - worth putting to the report chooser. "How do I
    /// run a report" is not: the guide answers that.
    /// </summary>
    public static bool Looks(string question)
    {
        var text = (question ?? "").Trim();
        if (HowTo().IsMatch(text)) return false;

        return text.Contains("report", StringComparison.OrdinalIgnoreCase)
               || (Topic().IsMatch(text) && (Asks().IsMatch(text) || Period().IsMatch(text)));
    }

    /// "March 2026", "yesterday", "last week", "2025": a short answer naming a period.
    public static bool NamesPeriod(string answer)
    {
        var text = (answer ?? "").Trim();
        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 8
               && (text.Any(char.IsDigit) || Period().IsMatch(text));
    }

    /// <summary>
    /// The report as text for the conversation: its figures, then its
    /// rows, the first <paramref name="most"/> of each table.
    /// </summary>
    public static string Describe(ReportResult report, int most = 20)
    {
        ArgumentNullException.ThrowIfNull(report);

        var text = new StringBuilder();
        text.AppendLine(report.Provenance.ParameterSummary).AppendLine();

        foreach (var figure in report.Figures)
            text.AppendLine(CultureInfo.CurrentCulture, $"{figure.Label}: **{figure.Value}**");
        if (report.Figures.Count > 0) text.AppendLine();

        Table(text, report.Columns, report.Rows, most);

        foreach (var section in report.Sections)
        {
            text.AppendLine().AppendLine(CultureInfo.CurrentCulture, $"**{section.Title}**");
            Table(text, section.Columns, section.Rows, most);
        }

        return text.ToString();
    }

    private static void Table(StringBuilder text, IReadOnlyList<ReportColumn> columns,
                              IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, int most)
    {
        text.AppendLine("**" + string.Join("  |  ", columns.Select(c => c.Header)) + "**");

        foreach (var row in rows.Take(most))
            text.AppendLine(string.Join("  |  ", columns.Select(c => Cell(c, row.GetValueOrDefault(c.Key)))));

        if (rows.Count > most)
            text.AppendLine(CultureInfo.CurrentCulture,
                $"... and {rows.Count - most} more rows. Save answer puts every row in the file.");
    }

    private static string Cell(ReportColumn column, object? value) => value switch
    {
        null => "",
        IFormattable formattable when column.Format is { } format => formattable.ToString(format, CultureInfo.CurrentCulture),
        _ => Convert.ToString(value, CultureInfo.CurrentCulture) ?? ""
    };

    [GeneratedRegex(@"^(how\s+(do|can|to|should)|where|why)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HowTo();

    [GeneratedRegex(@"\b(show|give|get|generate|create|make|export|print|pull|run|list|what\s+(were|was|are|is)|how\s+much|total)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Asks();

    [GeneratedRegex(@"\b(sales|sold|takings|took|cash[\s-]?up|end[\s-]of[\s-]day|vat|stock|expir\w*|transactions|movements|price changes|turnover|revenue|banking|dispensed|scripts)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Topic();

    [GeneratedRegex(@"\b(today|yesterday|week|month|year|quarter|jan\w*|feb\w*|mar\w*|apr\w*|may|jun\w*|jul\w*|aug\w*|sep\w*|oct\w*|nov\w*|dec\w*|since|last|this)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Period();
}
