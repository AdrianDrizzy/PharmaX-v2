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

    event EventHandler? PresetChanged;
    event EventHandler? PreviewRequested;   // F12
    event EventHandler? ExportRequested;
    event EventHandler? CloseRequested;     // F7
}
