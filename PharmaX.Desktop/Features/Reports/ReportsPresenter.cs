using PharmaX.Application.Reporting;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Reports;

public sealed class ReportsPresenter : PresenterBase<IReportsView>
{
    private readonly IReportRunner _runner;
    private readonly IPresetCatalogue _presets;
    private readonly IReportLookups _lookups;
    private readonly Application.Security.IPermissionService _permissions;
    private readonly IWorkbookWriter? _workbooks;

    public ReportsPresenter(IReportRunner runner, IPresetCatalogue presets,
                            IReportLookups lookups,
                            Application.Security.IPermissionService permissions,
                            IWorkbookWriter workbooks)
        : this(runner, presets, lookups, permissions) =>
        _workbooks = workbooks;

    public ReportsPresenter(IReportRunner runner, IPresetCatalogue presets,
                            IReportLookups lookups,
                            Application.Security.IPermissionService permissions)
    {
        _runner = runner;
        _presets = presets;
        _lookups = lookups;
        _permissions = permissions;
    }

    protected override void Subscribe()
    {
        View.PresetChanged += (_, _) => Enqueue(ShowSlicersForPresetAsync);
        View.PreviewRequested += (_, _) => Enqueue(PreviewAsync);
        View.ExportRequested += (_, _) => Enqueue(ExportAsync);
        View.CloseRequested += (_, _) => View.CloseView(true);
    }

    public override Task InitialiseAsync()
    {
        // Every incumbent report name appears here as a preset, which is
        // how parity is delivered without 47 report classes (RPT-003).
        // Only the reports this user may run: a report listed and then
        // refused is a dead end, and a cashier must not even see that
        // the branch has daily and monthly takings to look at. The runner
        // demands the permission again whatever the screen shows.
        View.ShowPresets(_presets.All()
            .Where(p => Application.Security.PermissionServiceExtensions
                .Has(_permissions, p.PermissionCode))
            .ToList());

        // Nothing is chosen yet, so there are no slicers to show. Said
        // explicitly because the panel would otherwise open at the
        // height the designer gave it - a band of empty grey above the
        // grid until the operator picks a report.
        View.ShowSlicers(Array.Empty<SlicerDto>());

        return Task.CompletedTask;
    }

    /// <summary>
    /// The slicers for the chosen report, with the choices filled in for
    /// the ones whose values live in the database.
    /// </summary>
    /// <remarks>
    /// The catalogue is a static table, so it declares a department or a
    /// till session slicer with no options at all. Handing that to the
    /// screen unchanged is what made those filters free text: an
    /// operator had to know a department's exact name, or type a GUID
    /// for a till session, so in practice nobody used them.
    /// </remarks>
    private Task ShowSlicersForPresetAsync() => GuardAsync(async () =>
    {
        // Another report chosen: the last one's rows are not this one's.
        View.ClearPreview("");

        if (View.SelectedPresetKey is null)
        {
            // A group heading rather than a report: it has no slicers,
            // and saying so collapses the panel instead of leaving the
            // last report's filters above a grid they no longer drive.
            View.ShowSlicers(Array.Empty<SlicerDto>());
            return;
        }

        var slicers = _presets.SlicersFor(View.SelectedPresetKey);
        var filled = new List<SlicerDto>(slicers.Count);

        foreach (var slicer in slicers)
        {
            // A slicer that already carries its own choices is a fixed
            // list - tenders, tax codes - and the database has nothing
            // to add to it.
            if (slicer.Options.Count > 0)
            {
                filled.Add(slicer);
                continue;
            }

            var options = await _lookups.OptionsForAsync(slicer.Key);

            filled.Add(options.Count > 0
                ? slicer with { Options = options }
                : slicer);
        }

        View.ShowSlicers(filled);
    });

    private Task PreviewAsync() => GuardAsync(async () =>
    {
        if (BuildRequest() is not ReportRequest request) return;

        var result = await _runner.RunAsync(request);

        if (result.RowCount == 0)
        {
            // RPT-011: say so, do not print an empty form. And empty the
            // grid: the previous report's rows stayed under this one's
            // heading, so a screenshot or an export was mislabelled
            // (ADM-DEF-21).
            View.ClearPreview($"{result.Title}  |  no rows for "
                              + result.Provenance.ParameterSummary);
            View.ShowInfo("No data for the selected period and filters.");
            return;
        }

        View.ShowPreview(result);
    });

    /// <remarks>
    /// The operator chooses where it goes, as every other export in
    /// PharmaX asks. It was written straight into Documents without a
    /// word about where (ADM-OBS-06).
    /// </remarks>
    private Task ExportAsync() => GuardAsync(async () =>
    {
        if (BuildRequest() is not ReportRequest request) return;

        if (_workbooks is null)
        {
            var written = await _runner.ExportAsync(request, ExportFormat.Excel);
            View.ShowSaved(written);
            return;
        }

        var result = await _runner.RunAsync(request);

        if (result.RowCount == 0)
        {
            View.ShowInfo("No data for the selected period and filters, so "
                          + "there is nothing to export.");
            return;
        }

        var path = View.PromptExportPath(
            Reporting.Exporters.ExcelExporter.FileName(result, "xlsx"));

        if (path is null) return;

        _workbooks.WriteTo(result, path);
        View.ShowSaved(path);
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
