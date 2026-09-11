using PharmaX.Application.Reporting;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Reports;

public sealed class ReportsPresenter : PresenterBase<IReportsView>
{
    private readonly IReportRunner _runner;
    private readonly IPresetCatalogue _presets;

    public ReportsPresenter(IReportRunner runner, IPresetCatalogue presets)
    {
        _runner = runner;
        _presets = presets;
    }

    protected override void Subscribe()
    {
        View.PresetChanged += (_, _) => ShowSlicersForPreset();
        View.PreviewRequested += (_, _) => _ = PreviewAsync();
        View.ExportRequested += (_, _) => _ = ExportAsync();
        View.CloseRequested += (_, _) => View.CloseView(true);
    }

    public override Task InitialiseAsync()
    {
        // Every incumbent report name appears here as a preset, which is
        // how parity is delivered without 47 report classes (RPT-003).
        View.ShowPresets(_presets.All());
        return Task.CompletedTask;
    }

    private void ShowSlicersForPreset()
    {
        if (View.SelectedPresetKey is null) return;
        View.ShowSlicers(_presets.SlicersFor(View.SelectedPresetKey));
    }

    private Task PreviewAsync() => GuardAsync(async () =>
    {
        if (BuildRequest() is not ReportRequest request) return;

        var result = await _runner.RunAsync(request);

        if (result.RowCount == 0)
        {
            // RPT-011: say so, do not print an empty form.
            View.ShowInfo("No data for the selected period and filters.");
            return;
        }

        View.ShowPreview(result);
    });

    private Task ExportAsync() => GuardAsync(async () =>
    {
        if (BuildRequest() is not ReportRequest request) return;

        var path = await _runner.ExportAsync(request, ExportFormat.Excel);
        View.ShowInfo("Exported to " + path);
    });

    private ReportRequest? BuildRequest()
    {
        if (View.SelectedPresetKey is null)
        {
            View.ShowError("Choose a report.");
            return null;
        }

        if (View.ToDate < View.FromDate)
        {
            View.ShowError("The end date falls before the start date.");
            return null;
        }

        return new ReportRequest(
            PresetKey: View.SelectedPresetKey,
            BranchCode: View.BranchCode,
            From: DateOnly.FromDateTime(View.FromDate),
            To: DateOnly.FromDateTime(View.ToDate),
            Slicers: View.SlicerValues);
    }
}
