namespace PharmaX.Application.Reporting;

public sealed record ReportRequest(
    string PresetKey,
    string BranchCode,
    DateOnly From,
    DateOnly To,
    IReadOnlyDictionary<string, string?> Slicers);

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
}

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

public interface IReportRunner
{
    Task<ReportResult> RunAsync(ReportRequest request,
        CancellationToken ct = default);

    Task<string> ExportAsync(ReportRequest request, ExportFormat format,
        CancellationToken ct = default);
}

/// <summary>
/// A preset is a name the pharmacy already recognises, bound to a report
/// definition and a fixed set of slicer values. Forty-eight incumbent
/// report names reach a handful of definitions this way.
/// </summary>
public sealed record ReportPreset(
    string Key,
    string DisplayName,
    string DefinitionKey,
    IReadOnlyDictionary<string, string?> FixedSlicers,
    IReadOnlyList<string> UserSlicers,
    string PermissionCode);

public sealed record ReportPresetDto(string Key, string DisplayName,
    string Group, string PermissionCode);

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
