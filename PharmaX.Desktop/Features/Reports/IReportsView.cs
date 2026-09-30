using PharmaX.Application.Reporting;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Reports;

public interface IReportsView : IView
{
    string BranchCode { get; }
    DateTime FromDate { get; }
    DateTime ToDate { get; }
    string? SelectedPresetKey { get; }

    void ShowPresets(IReadOnlyList<ReportPresetDto> presets);
    void ShowSlicers(IReadOnlyList<SlicerDto> slicers);
    IReadOnlyDictionary<string, string?> SlicerValues { get; }
    void ShowPreview(ReportResult result);

    /// <summary>
    /// Empties the preview and says why, so a run that found nothing
    /// never sits under the rows of the one before it.
    /// </summary>
    void ClearPreview(string caption);

    /// Where to save an export, or null where the operator backed out.
    string? PromptExportPath(string suggestedName);

    event EventHandler? PresetChanged;
    event EventHandler? PreviewRequested;   // F12
    event EventHandler? ExportRequested;
    event EventHandler? CloseRequested;     // F7
}
