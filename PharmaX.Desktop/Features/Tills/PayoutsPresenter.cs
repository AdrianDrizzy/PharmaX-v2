using PharmaX.Application.Tills;
using PharmaX.Application.Tills.Dtos;
using PharmaX.Desktop.Mvp;
using PharmaX.Domain.Tills;

namespace PharmaX.Desktop.Features.Tills;

public interface IPayoutsView : IView
{
    /// <param name="select">The shift to show first, where it is listed.</param>
    void ShowShifts(IReadOnlyList<ShiftChoiceDto> shifts, Guid? select);

    Guid? SelectedShiftId { get; }

    void ShowPayouts(IReadOnlyList<PayoutDto> payouts);

    PayoutKind EntryKind { get; }

    /// Null where nothing, or not a number, was typed.
    decimal? EntryAmount { get; }

    string EntryNote { get; }

    void ClearEntry();

    Guid? SelectedPayoutId { get; }

    /// Why it is being voided, or null where the operator backed out.
    string? PromptVoidReason(PayoutDto payout);

    event EventHandler? ShiftChanged;
    event EventHandler? AddRequested;
    event EventHandler? VoidRequested;
}

/// <summary>
/// Money taken out of a till's drawer during its shift - drawings, lunch,
/// other expenses - recorded with the amount and what it was for, and
/// voided where it was a mistake.
/// </summary>
public sealed class PayoutsPresenter : PresenterBase<IPayoutsView>
{
    private readonly ITillPayoutService _payouts;
    private readonly IShiftCashUpService _shifts;
    private readonly ITillSessionService _sessions;

    private IReadOnlyList<PayoutDto> _shown = Array.Empty<PayoutDto>();

    public PayoutsPresenter(ITillPayoutService payouts, IShiftCashUpService shifts,
                            ITillSessionService sessions)
    {
        _payouts = payouts;
        _shifts = shifts;
        _sessions = sessions;
    }

    /// <summary>
    /// The shift to open on - one chosen on the cash-up screen, or the one
    /// being counted - or null for this till's own.
    /// </summary>
    public Guid? TargetSessionId { get; set; }

    /// Whether anything was recorded or voided, so the caller reloads.
    public bool Changed { get; private set; }

    protected override void Subscribe()
    {
        View.ShiftChanged += (_, _) => Enqueue(LoadAsync);
        View.AddRequested += (_, _) => Enqueue(AddAsync);
        View.VoidRequested += (_, _) => Enqueue(VoidAsync);
    }

    public override Task InitialiseAsync() => GuardAsync(async () =>
    {
        // The open shifts this user may take money out of: their own, or
        // anyone's for whoever cashes up for others. This till's own is
        // always there, whoever opened it.
        var choices = (await _shifts.ShiftsAsync()).Where(s => s.IsOpen).ToList();
        var current = await _sessions.CurrentAsync();

        if (current is not null && choices.All(c => c.SessionId != current.Id))
            choices.Insert(0, new ShiftChoiceDto(current.Id, current.TillNumber,
                current.OpenedByName, current.OpenedAtUtc.ToLocalTime(), IsOpen: true,
                IsOwn: false));

        View.ShowShifts(choices, TargetSessionId ?? current?.Id);

        if (choices.Count == 0)
            View.ShowInfo("No till is open. Money out of the drawer is recorded while "
                          + "the till is open, before it is cashed up.");
    });

    private Task LoadAsync() => GuardAsync(async () =>
    {
        _shown = View.SelectedShiftId is Guid id
            ? await _payouts.ListAsync(id)
            : Array.Empty<PayoutDto>();

        View.ShowPayouts(_shown);
    });

    private Task AddAsync() => GuardAsync(async () =>
    {
        if (View.SelectedShiftId is not Guid id)
        {
            View.ShowError("Choose the till the money came out of.");
            return;
        }

        if (View.EntryAmount is not decimal amount || amount <= 0m)
        {
            View.ShowError("Enter how much was taken out of the drawer.");
            return;
        }

        var result = await _payouts.RecordAsync(
            new PayoutEditDto(View.EntryKind, amount, View.EntryNote), id);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        Changed = true;
        View.ClearEntry();
        await LoadAsync();
    });

    private Task VoidAsync() => GuardAsync(async () =>
    {
        var payout = _shown.FirstOrDefault(p => p.Id == View.SelectedPayoutId);

        if (payout is null)
        {
            View.ShowError("Choose the entry to void.");
            return;
        }

        if (payout.IsVoided)
        {
            View.ShowInfo("That entry is already voided.");
            return;
        }

        if (View.PromptVoidReason(payout) is not string reason) return;

        var result = await _payouts.VoidAsync(payout.Id, reason);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        Changed = true;
        await LoadAsync();
    });
}
