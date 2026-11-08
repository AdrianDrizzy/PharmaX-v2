using PharmaX.Application.Tills;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Tills;

public interface ICashUpHistoryView : IView
{
    DateOnly From { get; }
    DateOnly To { get; }

    void ShowHistory(IReadOnlyList<ShiftCashUpDto> shifts);

    event EventHandler? RangeChanged;
}

/// <summary>
/// Past cash-ups, every till's, between two days: what each drawer should
/// have held, what was counted, and why where it was out.
/// </summary>
public sealed class CashUpHistoryPresenter : PresenterBase<ICashUpHistoryView>
{
    private readonly IShiftCashUpService _shifts;

    public CashUpHistoryPresenter(IShiftCashUpService shifts) => _shifts = shifts;

    protected override void Subscribe() =>
        View.RangeChanged += (_, _) => Enqueue(InitialiseAsync);

    public override Task InitialiseAsync() => GuardAsync(async () =>
        View.ShowHistory(await _shifts.HistoryAsync(View.From, View.To)));
}
