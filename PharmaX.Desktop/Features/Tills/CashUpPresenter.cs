using System.Globalization;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Reporting;
using PharmaX.Application.Sales;
using PharmaX.Application.Tills;
using PharmaX.Desktop.Features.Sales;
using PharmaX.Desktop.Mvp;
using PharmaX.Reporting.Definitions;

namespace PharmaX.Desktop.Features.Tills;

public interface ICashUpView : IView
{
    /// <param name="forOthers">True where the user may pick any cashier's shift.</param>
    /// <param name="select">The shift to show, where listed; the first otherwise.</param>
    void ShowShifts(IReadOnlyList<ShiftChoiceDto> shifts, bool forOthers, Guid? select);

    Guid? SelectedShiftId { get; }

    /// The day whose shifts are listed; today unless the operator picked another.
    DateOnly Day { get; }

    /// Moves the day without raising DayChanged.
    void ShowDay(DateOnly day);

    /// The cash-up history; the closed shift chosen from it, or null.
    ShiftCashUpDto? PickFromHistory();

    /// Every transaction listed, and printed on the slip.
    bool EveryTransaction { get; }

    void ShowShift(ShiftCashUpDto? shift, bool onBehalf);

    /// Where to save, or null where the operator backed out.
    string? PromptSavePath(string suggestedName, bool pdf);

    /// <summary>
    /// The count-and-close screen for this shift, titled for another
    /// cashier's where <paramref name="onBehalf"/>. True where the till was
    /// closed.
    /// </summary>
    bool CountAndClose(Guid sessionId, bool onBehalf);

    /// <summary>
    /// The money-out window for this shift: drawings, lunch, other
    /// expenses. True where anything was recorded or voided.
    /// </summary>
    bool ShowPayouts(Guid sessionId);

    /// <summary>
    /// The slip as it will print, on screen. True where the operator
    /// chose to print it.
    /// </summary>
    bool PreviewSlip(PrintableDocument document, ReceiptHeaderDto header);

    event EventHandler? ShiftChanged;
    event EventHandler? DayChanged;
    event EventHandler? RefreshRequested;
    event EventHandler? EveryTransactionChanged;
    event EventHandler? PrintRequested;
    event EventHandler? ExportPdfRequested;
    event EventHandler? ExportExcelRequested;
    event EventHandler? CountRequested;
    event EventHandler? PayoutsRequested;
    event EventHandler? HistoryRequested;
}

/// <summary>
/// A cashier's cash-up of their own shift, or a supervisor's on a
/// cashier's behalf: the shift's sales by tender, every transaction if
/// asked, a slip for the till roll and the count-and-close.
/// </summary>
public sealed class CashUpPresenter : PresenterBase<ICashUpView>
{
    private readonly IShiftCashUpService _shifts;
    private readonly IDocumentPrinter _printer;
    private readonly ReceiptHeaderFactory _headers;
    private readonly IWorkbookWriter _workbooks;
    private readonly IPdfWriter _pdfs;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    private ShiftCashUpDto? _shift;
    private bool _onBehalf;

    public CashUpPresenter(IShiftCashUpService shifts, IDocumentPrinter printer,
        ReceiptHeaderFactory headers, IWorkbookWriter workbooks, IPdfWriter pdfs,
        ICurrentUser user, IClock clock)
    {
        _shifts = shifts;
        _printer = printer;
        _headers = headers;
        _workbooks = workbooks;
        _pdfs = pdfs;
        _user = user;
        _clock = clock;
    }

    protected override void Subscribe()
    {
        View.ShiftChanged += (_, _) => Enqueue(LoadShiftAsync);
        View.DayChanged += (_, _) => Enqueue(InitialiseAsync);
        View.RefreshRequested += (_, _) => Enqueue(() => GuardAsync(() => ListAsync(View.SelectedShiftId)));
        View.EveryTransactionChanged += (_, _) => View.ShowShift(_shift, _onBehalf);
        View.PrintRequested += (_, _) => Enqueue(PrintAsync);
        View.ExportPdfRequested += (_, _) => Enqueue(() => ExportAsync(pdf: true));
        View.ExportExcelRequested += (_, _) => Enqueue(() => ExportAsync(pdf: false));
        View.CountRequested += (_, _) => Enqueue(CountAsync);
        View.PayoutsRequested += (_, _) => Enqueue(PayoutsAsync);
        View.HistoryRequested += (_, _) => Enqueue(HistoryAsync);
    }

    /// This computer's till first, so it is the one shown.
    public override Task InitialiseAsync() => GuardAsync(() => ListAsync(null));

    private async Task ListAsync(Guid? select)
    {
        // The list is shown with a shift chosen; choosing raises
        // ShiftChanged, which loads it.
        _choices = await _shifts.ShiftsAsync(View.Day);
        View.ShowShifts(_choices, _shifts.MayActForOthers, select);

        if (_choices.Count == 0) View.ShowShift(null, false);
    }

    /// A past cash-up, shown here on the day it closed: its figures, its slip and the exports.
    private Task HistoryAsync() => GuardAsync(async () =>
    {
        if (View.PickFromHistory() is not { ClosedAtLocal: DateTime closed } picked) return;

        View.ShowDay(DateOnly.FromDateTime(closed));
        await ListAsync(picked.SessionId);
    });

    private Task LoadShiftAsync() => GuardAsync(async () =>
    {
        if (View.SelectedShiftId is not Guid id)
        {
            _shift = null;
            View.ShowShift(null, false);
            return;
        }

        _shift = await _shifts.ShiftAsync(id);
        _onBehalf = _choices.FirstOrDefault(c => c.SessionId == id) is { IsOwn: false };
        View.ShowShift(_shift, _onBehalf);
    });

    /// The shifts the picker offers, as last read.
    private IReadOnlyList<ShiftChoiceDto> _choices = Array.Empty<ShiftChoiceDto>();

    private Task PrintAsync() => GuardAsync(async () =>
    {
        if (_shift is null) { View.ShowError("Choose a shift to cash up first."); return; }

        var slip = CashUpSlip.Document(_shift, View.EveryTransaction, _clock.UtcNow.ToLocalTime());
        var header = _headers.Build();

        // On screen first, as the till roll will have it.
        if (!View.PreviewSlip(slip, header)) return;

        var printed = await _printer.PrintAsync(slip, header);

        if (printed.Printed) View.ShowInfo(printed.Describe("The cash-up slip"));
        else View.ShowError(printed.Describe("The cash-up slip"));
    });

    /// Drawings, lunch and other expenses out of the chosen shift's drawer.
    private Task PayoutsAsync() => GuardAsync(async () =>
    {
        if (_shift is null) { View.ShowError("Choose a shift to cash up first."); return; }

        if (!_shift.IsOpen)
        {
            View.ShowInfo("This till is already closed, so what came out of it stands as "
                          + "counted. Its money out is listed on the slip.");
            return;
        }

        if (!MayHandle()) return;

        if (View.ShowPayouts(_shift.SessionId)) await LoadShiftAsync();
    });

    /// <summary>
    /// Every shift is listed to everyone, but only its own cashier or a
    /// supervisor takes money out of it or closes it.
    /// </summary>
    /// <remarks>
    /// Said here, before the window opens, rather than by the service after
    /// a count has been typed in.
    /// </remarks>
    private bool MayHandle()
    {
        if (!_onBehalf || _shifts.MayActForOthers) return true;

        View.ShowInfo("This is another cashier's till. Only they, or a pharmacist, an "
                      + "assistant or the administrator, can take money out of it or close it.");
        return false;
    }

    private Task ExportAsync(bool pdf) => GuardAsync(() =>
    {
        if (_shift is null) { View.ShowError("Choose a shift to cash up first."); return Task.CompletedTask; }

        var report = CashUpReports.Shift(_shift, Provenance(_shift), View.EveryTransaction);
        var path = View.PromptSavePath(
            PharmaX.Reporting.Exporters.ExcelExporter.FileName(report, pdf ? "pdf" : "xlsx"), pdf);

        if (path is null) return Task.CompletedTask;

        if (pdf) _pdfs.WriteTo(report, path);
        else _workbooks.WriteTo(report, path);

        View.ShowSaved(path);
        return Task.CompletedTask;
    });

    private ReportProvenance Provenance(ShiftCashUpDto shift) => new(
        _user.Username, _clock.UtcNow.ToLocalTime(),
        string.Format(CultureInfo.CurrentCulture, "Till {0} | {1} | from {2:dd/MM/yyyy HH:mm}{3}",
            shift.TillNumber, shift.CashierName, shift.OpenedAtLocal,
            shift.ClosedAtLocal is DateTime to
                ? string.Format(CultureInfo.CurrentCulture, " to {0:dd/MM/yyyy HH:mm}", to)
                : " (still open)"),
        _onBehalf ? "Cashed up on the cashier's behalf" : "Cashed up by the cashier");

    private Task CountAsync() => GuardAsync(async () =>
    {
        if (_shift is null) { View.ShowError("Choose a shift to cash up first."); return; }

        if (!_shift.IsOpen)
        {
            View.ShowInfo("This till is already closed. Its figures and the count are shown here, "
                          + "and the slip can still be printed.");
            return;
        }

        if (!MayHandle()) return;

        if (!View.CountAndClose(_shift.SessionId, _onBehalf)) return;

        await ListAsync(_shift.SessionId);
    });
}
