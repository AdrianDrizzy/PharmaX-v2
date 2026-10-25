using System.Globalization;
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
    private readonly IPdfWriter? _pdfs;
    private readonly IChartRenderer? _charts;
    private readonly IReportSuggester? _suggester;

    /// What the assistant may choose from; null until the screen has loaded it.
    private ReportSuggestionContext? _askContext;
    private bool _warming;

    public ReportsPresenter(IReportRunner runner, IPresetCatalogue presets,
                            IReportLookups lookups,
                            Application.Security.IPermissionService permissions,
                            IWorkbookWriter workbooks,
                            IReportSuggester suggester,
                            IPdfWriter pdfs,
                            IChartRenderer charts)
        : this(runner, presets, lookups, permissions)
    {
        _workbooks = workbooks;
        _suggester = suggester;
        _pdfs = pdfs;
        _charts = charts;
    }

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
        View.ExportRequested += (_, _) => Enqueue(() => ExportAsync(pdf: false));
        View.ExportPdfRequested += (_, _) => Enqueue(() => ExportAsync(pdf: true));
        View.EnlargeGraphRequested += (_, _) => Enqueue(EnlargeGraphAsync);
        View.CloseRequested += (_, _) => View.CloseView(true);
        View.AskStarted += (_, _) => WarmUp();
        View.AskRequested += (_, _) => Enqueue(AskAsync);
    }

    public override async Task InitialiseAsync()
    {
        // Every incumbent report name appears here as a preset, which is
        // how parity is delivered without 47 report classes (RPT-003).
        // Only the reports this user may run: a report listed and then
        // refused is a dead end, and a cashier must not even see that
        // the branch has daily and monthly takings to look at. The runner
        // demands the permission again whatever the screen shows.
        var allowed = _presets.All()
            .Where(p => Application.Security.PermissionServiceExtensions
                .Has(_permissions, p.PermissionCode))
            .ToList();

        View.ShowPresets(allowed);

        // Nothing is chosen yet, so there are no slicers to show. Said
        // explicitly because the panel would otherwise open at the
        // height the designer gave it - a band of empty grey above the
        // grid until the operator picks a report.
        View.ShowSlicers(Array.Empty<SlicerDto>());

        // The same list for the assistant, so it can only suggest a
        // report this operator could have picked.
        var askable = _suggester?.IsAvailable == true && allowed.Count > 0;
        View.ShowAsk(askable);

        if (askable)
            await GuardAsync(() => PrepareAskAsync(allowed));
    }

    /// <summary>
    /// Every allowed report's slicers with their choices filled in, read
    /// once when the screen opens so the assistant knows this pharmacy's
    /// departments and staff by name.
    /// </summary>
    private async Task PrepareAskAsync(IReadOnlyList<ReportPresetDto> presets)
    {
        var read = new Dictionary<string, IReadOnlyList<SlicerOptionDto>>(StringComparer.Ordinal);
        var slicers = new Dictionary<string, IReadOnlyList<SlicerDto>>(StringComparer.Ordinal);

        foreach (var preset in presets)
        {
            var filled = new List<SlicerDto>();

            foreach (var slicer in _presets.SlicersFor(preset.Key))
                filled.Add(await FillAsync(slicer, read));

            slicers[preset.Key] = filled;
        }

        _askContext = new ReportSuggestionContext(presets, slicers,
            DateOnly.FromDateTime(DateTime.Today));
    }

    /// <summary>
    /// Starts the model loading as soon as someone types a request, so it
    /// is ready, or nearly, by the time they press Enter.
    /// </summary>
    /// <remarks>
    /// In the background and outside the screen's queue: it touches no
    /// database, and the screen stays usable while it runs.
    /// </remarks>
    private void WarmUp()
    {
        if (_warming || _suggester is null || _askContext is null) return;

        _warming = true;
        _ = WarmUpQuietlyAsync(_suggester, _askContext);
    }

    private static async Task WarmUpQuietlyAsync(IReportSuggester suggester,
                                                 ReportSuggestionContext context)
    {
        try
        {
            await suggester.WarmUpAsync(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The screen closed first.
        }
        catch (ObjectDisposedException)
        {
            // As above.
        }
#pragma warning disable CA1031 // Logged; the question itself reports any failure.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ErrorHandler.Log(ex);
        }
    }

    /// <summary>
    /// Fills the screen in from a request in plain words: the report, its
    /// period and its filters. It stops there - the operator checks what
    /// was chosen and presses Preview, as for any other report.
    /// </summary>
    private Task AskAsync() => GuardAsync(async () =>
    {
        if (_suggester is null || _askContext is null) return;

        var request = View.AskText.Trim();

        if (request.Length == 0)
        {
            View.ShowAskResult("Type the report you want in your own words, for "
                               + "example \"stock expiring in the next 3 months\".");
            return;
        }

        View.ShowAskResult(_suggester.IsReady
            ? "Working it out..."
            : "Getting the assistant ready. The first request on this computer can "
              + "take a minute or two; after that it takes seconds.");

        var suggestion = await _suggester.SuggestAsync(request, _askContext);

        if (!suggestion.Understood)
        {
            View.ShowAskResult("The assistant could not work that out. Try saying it "
                               + "another way, or choose the report from the list.");
            return;
        }

        if (suggestion.PresetKey is null)
        {
            View.ShowAskResult("That does not look like a request for a report, so "
                               + "nothing was chosen. For any other question, press "
                               + "Ctrl+Shift+A for Ask PharmaX.");
            return;
        }

        View.SelectPreset(suggestion.PresetKey);

        if (!string.Equals(View.SelectedPresetKey, suggestion.PresetKey,
                           StringComparison.Ordinal))
        {
            View.ShowAskResult("The assistant chose a report that is not in the list.");
            return;
        }

        await ShowSlicersForPresetAsync();
        View.ApplySlicerValues(suggestion.Slicers);

        if (suggestion.From is DateOnly from && suggestion.To is DateOnly to)
            View.SetPeriod(from, to);

        View.ShowAskResult(Describe(suggestion) + " Check it, then press Preview (F12).");
    });

    /// "Chose Stock Expiring, Expiring within (days): 90."
    private string Describe(ReportSuggestion suggestion)
    {
        var context = _askContext!;
        var preset = context.Presets.First(p => p.Key == suggestion.PresetKey);
        var parts = new List<string> { "Chose " + preset.DisplayName };

        if (suggestion.From is DateOnly from && suggestion.To is DateOnly to)
            parts.Add(from == to
                ? "for " + Day(from)
                : $"from {Day(from)} to {Day(to)}");

        var slicers = context.Slicers.TryGetValue(preset.Key, out var list)
            ? list
            : Array.Empty<SlicerDto>();

        foreach (var (key, value) in suggestion.Slicers)
        {
            if (slicers.FirstOrDefault(s => s.Key == key) is not SlicerDto slicer) continue;

            parts.Add(slicer.Kind == "flag"
                ? slicer.Label
                : $"{slicer.Label}: {slicer.Options.FirstOrDefault(o => o.Value == value)?.Label ?? value}");
        }

        return string.Join(", ", parts) + ".";
    }

    private static string Day(DateOnly day) =>
        day.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

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
        _chart = null;

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
            filled.Add(await FillAsync(slicer));

        View.ShowSlicers(filled);
    });

    /// <summary>
    /// A slicer with its choices from the database, where it has any.
    /// </summary>
    /// <param name="read">Lists already read this time, so a department
    /// list shared by many reports is read once.</param>
    private async Task<SlicerDto> FillAsync(SlicerDto slicer,
        Dictionary<string, IReadOnlyList<SlicerOptionDto>>? read = null)
    {
        // A slicer that already carries its own choices is a fixed
        // list - tenders, tax codes - and the database has nothing
        // to add to it.
        if (slicer.Options.Count > 0) return slicer;

        if (read is null || !read.TryGetValue(slicer.Key, out var options))
        {
            options = await _lookups.OptionsForAsync(slicer.Key);
            read?.TryAdd(slicer.Key, options);
        }

        return options.Count > 0
            ? slicer with { Options = options }
            : slicer;
    }

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

        // The graph beside the rows, where the report has one: a summary
        // by tender, by day, by department. A list of sales has none.
        _chart = result.Chart;
        View.ShowChart(result.Chart is ReportChart chart && _charts is not null
            ? _charts.Png(chart, 640, 320)
            : null);
    });

    /// <remarks>
    /// The operator chooses where it goes, as every other export in
    /// PharmaX asks. It was written straight into Documents without a
    /// word about where (ADM-OBS-06).
    /// </remarks>
    /// <summary>The previewed report's graph, for its own window.</summary>
    private ReportChart? _chart;

    /// <summary>
    /// The graph drawn again at a size for the whole screen, rather than
    /// the side panel's picture stretched, so it stays sharp maximised.
    /// </summary>
    private Task EnlargeGraphAsync() => GuardAsync(() =>
    {
        if (_chart is null || _charts is null)
            View.ShowInfo("Preview a report that has a graph first.");
        else
            View.ShowGraphWindow(_chart.Title, _charts.Png(_chart, 1280, 640));

        return Task.CompletedTask;
    });

    private Task ExportAsync(bool pdf) => GuardAsync(async () =>
    {
        if (BuildRequest() is not ReportRequest request) return;

        if (_workbooks is null || _pdfs is null)
        {
            var written = await _runner.ExportAsync(request,
                pdf ? ExportFormat.Pdf : ExportFormat.Excel);
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
            Reporting.Exporters.ExcelExporter.FileName(result, pdf ? "pdf" : "xlsx"), pdf);

        if (path is null) return;

        // The pharmacy's letterhead, the headline figures, the table and
        // its graph, in either format.
        // The graph only where the operator has it switched on.
        if (!View.IncludeGraph) result = result with { Chart = null };

        if (pdf) _pdfs.WriteTo(result, path);
        else _workbooks.WriteTo(result, path);
        View.ShowSaved(path);
    });

    private ReportRequest? BuildRequest()
    {
        if (View.SelectedPresetKey is null)
        {
            View.ShowError("Choose a report.");
            return null;
        }

        if (View.ToMoment < View.FromMoment)
        {
            View.ShowError(View.UsesClock
                ? "The end time falls before the start time."
                : "The end date falls before the start date.");
            return null;
        }

        return new ReportRequest(
            PresetKey: View.SelectedPresetKey,
            BranchCode: View.BranchCode,
            From: DateOnly.FromDateTime(View.FromMoment),
            To: DateOnly.FromDateTime(View.ToMoment),
            Slicers: View.SlicerValues,
            FromClock: View.UsesClock ? View.FromMoment : null,
            ToClock: View.UsesClock ? View.ToMoment : null);
    }
}
