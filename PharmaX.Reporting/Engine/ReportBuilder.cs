using PharmaX.Application.Reporting;

namespace PharmaX.Reporting.Engine;

/// <summary>
/// Assembles a ReportResult. Exists so that a definition reads as a list
/// of columns and a projection rather than as dictionary plumbing, which
/// is what makes adding the forty-eighth report a five-minute job.
/// </summary>
public sealed class ReportBuilder
{
    private readonly string _title;
    private readonly List<ReportColumn> _columns = new();
    private readonly List<IReadOnlyDictionary<string, object?>> _rows = new();
    private readonly Dictionary<string, object?> _totals = new(StringComparer.Ordinal);

    private ReportBuilder(string title) => _title = title;

    public static ReportBuilder For(string title) => new(title);

    public ReportBuilder Column(string key, string header,
        string? format = null, bool rightAlign = false)
    {
        _columns.Add(new ReportColumn(key, header, format, rightAlign));
        return this;
    }

    public ReportBuilder Rows(
        IEnumerable<IReadOnlyDictionary<string, object?>> rows)
    {
        _rows.AddRange(rows);
        return this;
    }

    public ReportBuilder Total(string key, object? value)
    {
        _totals[key] = value;
        return this;
    }

    public ReportResult Build(ReportProvenance provenance) =>
        new(_title, _columns, _rows, _totals, provenance);
}
