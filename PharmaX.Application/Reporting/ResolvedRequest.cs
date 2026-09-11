using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace PharmaX.Application.Reporting;

/// <summary>
/// A request with its preset applied: the fixed slicers the preset
/// carries, merged with the ones the user chose, plus the provenance line
/// that will be printed on the output.
/// </summary>
/// <remarks>
/// Definitions read slicers through the typed accessors below rather than
/// indexing the dictionary, so a slicer that is absent, blank or
/// unparseable behaves the same way everywhere: as "not applied".
/// </remarks>
[SuppressMessage("Naming", "CA1720:Identifier contains type name",
    Justification = "The accessor is named for the type it returns, which "
                  + "is the point: r.Guid(\"operator\") at a call site in a "
                  + "report definition says exactly what comes back. "
                  + "Renaming them to GuidValue and the rest would make "
                  + "every definition noisier to read.")]
public sealed class ResolvedRequest
{
    private readonly IReadOnlyDictionary<string, string?> _slicers;
    private readonly Func<Guid, string> _operatorName;

    public ResolvedRequest(ReportPreset preset, DateOnly from, DateOnly to,
        string branchCode, IReadOnlyDictionary<string, string?> slicers,
        ReportProvenance provenance, Func<Guid, string>? operatorName = null)
    {
        Preset = preset;
        From = from;
        To = to;
        BranchCode = branchCode;
        _slicers = slicers;
        Provenance = provenance;
        _operatorName = operatorName ?? (id => id.ToString());
    }

    public ReportPreset Preset { get; }
    public DateOnly From { get; }
    public DateOnly To { get; }
    public string BranchCode { get; }
    public ReportProvenance Provenance { get; }

    /// Half-open in UTC, so the last second of the final day is not
    /// silently dropped by a BETWEEN on a timestamp column.
    public DateTime FromUtc => From.ToDateTime(TimeOnly.MinValue).ToUniversalTime();
    public DateTime ToExclusiveUtc =>
        To.AddDays(1).ToDateTime(TimeOnly.MinValue).ToUniversalTime();

    public string? String(string key) =>
        _slicers.TryGetValue(key, out var value)
        && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    public bool Bool(string key) =>
        bool.TryParse(String(key), out var value) && value;

    public Guid? Guid(string key) =>
        System.Guid.TryParse(String(key), out var value) ? value : null;

    public decimal? Decimal(string key) =>
        decimal.TryParse(String(key), NumberStyles.Number,
                         CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    /// <summary>
    /// Operator names are resolved through a callback rather than a join,
    /// because a report grouped by operator needs one lookup per distinct
    /// id and a join would fetch the user row once per transaction.
    /// </summary>
    public string OperatorName(Guid operatorId) => _operatorName(operatorId);
}
