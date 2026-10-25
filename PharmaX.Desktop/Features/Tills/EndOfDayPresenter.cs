using System.Globalization;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Reporting;
using PharmaX.Application.Tills;
using PharmaX.Desktop.Mvp;
using PharmaX.Reporting.Definitions;

namespace PharmaX.Desktop.Features.Tills;

public interface IEndOfDayView : IView
{
    DateOnly Day { get; }

    /// <param name="chartPng">The tender graph as a picture, or null.</param>
    void ShowReport(ReportResult report, byte[]? chartPng);

    string? PromptSavePath(string suggestedName, bool pdf);

    /// <summary>
    /// The money-out window, choosing among the tills still open. True
    /// where anything was recorded or voided.
    /// </summary>
    bool ShowPayouts();

    event EventHandler? ShowRequested;
    event EventHandler? ExportPdfRequested;
    event EventHandler? ExportExcelRequested;
    event EventHandler? PayoutsRequested;
}

/// <summary>
/// The end-of-day cash-up: every till and every cashier for one day, on
/// screen and as PDF or Excel.
/// </summary>
public sealed class EndOfDayPresenter : PresenterBase<IEndOfDayView>
{
    private readonly IShiftCashUpService _shifts;
    private readonly IWorkbookWriter _workbooks;
    private readonly IPdfWriter _pdfs;
    private readonly IChartRenderer _charts;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    private ReportResult? _report;

    public EndOfDayPresenter(IShiftCashUpService shifts, IWorkbookWriter workbooks,
        IPdfWriter pdfs, IChartRenderer charts, ICurrentUser user, IClock clock)
    {
        _shifts = shifts;
        _workbooks = workbooks;
        _pdfs = pdfs;
        _charts = charts;
        _user = user;
        _clock = clock;
    }

    protected override void Subscribe()
    {
        View.ShowRequested += (_, _) => Enqueue(InitialiseAsync);
        View.ExportPdfRequested += (_, _) => Enqueue(() => ExportAsync(pdf: true));
        View.ExportExcelRequested += (_, _) => Enqueue(() => ExportAsync(pdf: false));

        // Drawings, lunch and expenses out of a till still open, then the
        // day again with them.
        View.PayoutsRequested += (_, _) => Enqueue(async () =>
        {
            if (View.ShowPayouts()) await InitialiseAsync();
        });
    }

    public override Task InitialiseAsync() => GuardAsync(async () =>
    {
        var day = await _shifts.EndOfDayAsync(View.Day);

        _report = CashUpReports.EndOfDay(day, new ReportProvenance(
            _user.Username, _clock.UtcNow.ToLocalTime(),
            string.Format(CultureInfo.CurrentCulture, "End-of-day cash-up | {0:dddd dd/MM/yyyy} | all tills",
                View.Day.ToDateTime(TimeOnly.MinValue)),
            day.Shifts.Any(s => s.IsOpen)
                ? "Some tills are still open: their figures will change"
                : "Every till is closed"));

        View.ShowReport(_report, _report.Chart is ReportChart chart
            ? _charts.Png(chart, 640, 300)
            : null);
    });

    private Task ExportAsync(bool pdf) => GuardAsync(() =>
    {
        if (_report is null) return Task.CompletedTask;

        if (_report.RowCount == 0)
        {
            View.ShowInfo("No till traded on that day, so there is nothing to export.");
            return Task.CompletedTask;
        }

        var path = View.PromptSavePath(
            PharmaX.Reporting.Exporters.ExcelExporter.FileName(_report, pdf ? "pdf" : "xlsx"), pdf);

        if (path is null) return Task.CompletedTask;

        if (pdf) _pdfs.WriteTo(_report, path);
        else _workbooks.WriteTo(_report, path);

        View.ShowSaved(path);
        return Task.CompletedTask;
    });
}
