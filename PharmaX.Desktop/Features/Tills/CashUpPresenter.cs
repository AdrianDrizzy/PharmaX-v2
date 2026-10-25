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
    void ShowShifts(IReadOnlyList<ShiftChoiceDto> shifts, bool forOthers);

    Guid? SelectedShiftId { get; }

    /// Every transaction listed, and printed on the slip.
    bool EveryTransaction { get; }

    void ShowShift(ShiftCashUpDto? shift, bool onBehalf);

    /// Where to save, or null where the operator backed out.
    string? PromptSavePath(string suggestedName, bool pdf);

    /// <summary>
    /// The count-and-close screen for this shift. True where the till was
    /// closed.
    /// </summary>
    bool CountAndClose(Guid sessionId);

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
    event EventHandler? RefreshRequested;
    event EventHandler? EveryTransactionChanged;
    event EventHandler? PrintRequested;
    event EventHandler? ExportPdfRequested;
    event EventHandler? ExportExcelRequested;
    event EventHandler? CountRequested;
    event EventHandler? PayoutsRequested;
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
        View.RefreshRequested += (_, _) => Enqueue(InitialiseAsync);
        View.EveryTransactionChanged += (_, _) => View.ShowShift(_shift, _onBehalf);
        View.PrintRequested += (_, _) => Enqueue(PrintAsync);
        View.ExportPdfRequested += (_, _) => Enqueue(() => ExportAsync(pdf: true));
        View.ExportExcelRequested += (_, _) => Enqueue(() => ExportAsync(pdf: false));
        View.CountRequested += (_, _) => Enqueue(CountAsync);
        View.PayoutsRequested += (_, _) => Enqueue(PayoutsAsync);
    }

    public override Task InitialiseAsync() => GuardAsync(async () =>
    {
        // The list is shown with the first shift chosen; choosing raises
        // ShiftChanged, which loads it.
        _choices = await _shifts.ShiftsAsync();
        View.ShowShifts(_choices, _shifts.MayActForOthers);

        if (_choices.Count == 0) View.ShowShift(null, false);
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

        if (View.ShowPayouts(_shift.SessionId)) await LoadShiftAsync();
    });

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

        if (!View.CountAndClose(_shift.SessionId)) return;

        await InitialiseAsync();
    });
}
