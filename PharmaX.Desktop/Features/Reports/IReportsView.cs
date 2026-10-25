using PharmaX.Application.Reporting;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Reports;

public interface IReportsView : IView
{
    string BranchCode { get; }
    DateTime FromDate { get; }
    DateTime ToDate { get; }

    /// Sales summary is asked for by the minute. Other reports use the day.
    bool UsesClock { get; }
    DateTime FromMoment { get; }
    DateTime ToMoment { get; }

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
    string? PromptExportPath(string suggestedName, bool pdf);

    /// The report's graph as a picture beside the rows; null hides it.
    void ShowChart(byte[]? png);

    /// The graph in a window of its own, which can be maximised.
    void ShowGraphWindow(string title, byte[] png);

    event EventHandler? EnlargeGraphRequested;

    /// The graph toggle: shown on the preview, and put in the exports.
    bool IncludeGraph { get; }

    /// The request typed into the Ask box, in the operator's own words.
    string AskText { get; }

    /// Shows the Ask box, or hides it where no assistant is installed.
    void ShowAsk(bool available);

    /// The line under the Ask box: what was chosen, or why nothing was.
    void ShowAskResult(string message);

    /// <summary>
    /// Chooses a report as a click would, but without raising
    /// PresetChanged.
    /// </summary>
    /// <remarks>
    /// The presenter loads the slicers itself and then fills them in. A
    /// PresetChanged would queue a second load behind it that rebuilt the
    /// slicers and wiped what the assistant had chosen.
    /// </remarks>
    void SelectPreset(string presetKey);

    void SetPeriod(DateOnly from, DateOnly to);

    /// Sets the slicers named; the others are left as they are.
    void ApplySlicerValues(IReadOnlyDictionary<string, string> values);

    event EventHandler? PresetChanged;
    event EventHandler? PreviewRequested;   // F12
    event EventHandler? ExportRequested;    // Excel
    event EventHandler? ExportPdfRequested;
    event EventHandler? CloseRequested;     // F7
    event EventHandler? AskStarted;         // typing in the Ask box
    event EventHandler? AskRequested;       // Enter, or Suggest
}
