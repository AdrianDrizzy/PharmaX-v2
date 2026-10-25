namespace PharmaX.Application.Reporting;

public sealed record ReportRequest(
    string PresetKey,
    string BranchCode,
    DateOnly From,
    DateOnly To,
    IReadOnlyDictionary<string, string?> Slicers,
    DateTime? FromClock = null,
    DateTime? ToClock = null);

public sealed record ReportColumn(string Key, string Header,
    string? Format = null, bool RightAlign = false);

public sealed record ReportResult(
    string Title,
    IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    IReadOnlyDictionary<string, object?> Totals,
    ReportProvenance Provenance)
{
    public int RowCount => Rows.Count;

    /// <summary>
    /// Headline figures printed above the table: "Net takings 1,234.00".
    /// </summary>
    public IReadOnlyList<ReportFigure> Figures { get; init; } = Array.Empty<ReportFigure>();

    /// <summary>
    /// Further tables after the main one, each under its own heading: the
    /// end-of-day cash-up's tenders, tills and cashiers.
    /// </summary>
    public IReadOnlyList<ReportSection> Sections { get; init; } = Array.Empty<ReportSection>();

    /// <summary>A graph of the figures, where one helps; null where not.</summary>
    public ReportChart? Chart { get; init; }
}

public sealed record ReportFigure(string Label, string Value);

public sealed record ReportSection(string Title, IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows);

public enum ChartKind { Bar, Line }

public sealed record ChartPoint(string Label, decimal Value);

/// <param name="ValueLabel">What the bars measure: "Net", "Sales".</param>
public sealed record ReportChart(string Title, ChartKind Kind, string ValueLabel,
                                 IReadOnlyList<ChartPoint> Points);

/// Printed on every report (RPT-012): what was asked, by whom, when,
/// and how current the data is.
public sealed record ReportProvenance(
    string RunByUsername,
    DateTime RunAtLocal,
    string ParameterSummary,
    string DataCurrency);

public enum ExportFormat { Pdf, Excel, Csv }

/// <summary>
/// Writes a result to a spreadsheet at a path somebody has chosen,
/// rather than to a generated name in a folder.
/// </summary>
/// <remarks>
/// The reports screen exports through IReportExporter, which names the
/// file itself. The accounts and ledger screens export what is on the
/// grid to wherever the operator points a save dialog, so they need the
/// same workbook and a different way of saying where it goes.
/// </remarks>
public interface IWorkbookWriter
{
    void WriteTo(ReportResult report, string path);
}

/// <summary>
/// The same report as a PDF, at a path somebody has chosen.
/// </summary>
public interface IPdfWriter
{
    void WriteTo(ReportResult report, string path);
}

/// <summary>
/// A report's chart as a picture, for the preview on screen.
/// </summary>
public interface IChartRenderer
{
    byte[] Png(ReportChart chart, int width, int height);
}

public interface IReportRunner
{
    Task<ReportResult> RunAsync(ReportRequest request,
        CancellationToken ct = default);

    Task<string> ExportAsync(ReportRequest request, ExportFormat format,
        CancellationToken ct = default);
}

/// <summary>
/// How a report asked for is recorded in the audit trail: who, when and
/// which report. The home screen reads when one was last asked for.
/// </summary>
public static class ReportRuns
{
    public const string Action = "rpt.report.run";
    public const string EntityType = "Report";
}

/// <summary>
/// A preset is a name the pharmacy already recognises, bound to a report
/// definition and a fixed set of slicer values. Forty-eight incumbent
/// report names reach a handful of definitions this way.
/// </summary>
/// <param name="Description">
/// What the report shows, in the words the staff use. A name such as
/// "Sales (By Till Operator)" does not say it is the per-cashier report,
/// and the assistant that finds reports from a request in plain words
/// cannot pick a report it cannot recognise.
/// </param>
public sealed record ReportPreset(
    string Key,
    string DisplayName,
    string DefinitionKey,
    IReadOnlyDictionary<string, string?> FixedSlicers,
    IReadOnlyList<string> UserSlicers,
    string PermissionCode,
    string Description = "");

public sealed record ReportPresetDto(string Key, string DisplayName,
    string Group, string PermissionCode, string Description = "");

/// <summary>
/// One user-settable filter on a preset. Options is empty where the
/// values are loaded at run time, such as the operator list.
/// </summary>
public sealed record SlicerDto(string Key, string Label, string Kind,
    IReadOnlyList<SlicerOptionDto> Options, bool IsMultiSelect = false);

public sealed record SlicerOptionDto(string Value, string Label);

/// <summary>
/// The choices for a slicer the catalogue cannot know in advance,
/// because they are rows in this pharmacy's database rather than a fixed
/// list: its departments, its till sessions, its staff.
/// </summary>
/// <remarks>
/// Returns an empty list for a slicer it has nothing to offer, which the
/// screen reads as "leave this one as typed text".
/// </remarks>
public interface IReportLookups
{
    Task<IReadOnlyList<SlicerOptionDto>> OptionsForAsync(string slicerKey,
        CancellationToken ct = default);
}

public interface IPresetCatalogue
{
    IReadOnlyList<ReportPresetDto> All();
    IReadOnlyList<SlicerDto> SlicersFor(string presetKey);
    ReportPreset Resolve(string presetKey);
}

/// <summary>
/// One report definition: the query behind any number of presets. A
/// definition never knows which preset invoked it, only the slicers it
/// was handed.
/// </summary>
public interface IReportDefinition
{
    string Key { get; }

    Task<ReportResult> RunAsync(ResolvedRequest request,
                                CancellationToken ct = default);
}

/// <summary>
/// Writes a finished result to a file in the given directory and returns
/// the path. One implementation per format, resolved by the runner.
/// </summary>
public interface IReportExporter
{
    ExportFormat Format { get; }
    string Write(ReportResult report, string directory);
}
