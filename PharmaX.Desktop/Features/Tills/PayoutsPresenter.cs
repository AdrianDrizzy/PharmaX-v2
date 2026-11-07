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

    /// The entries picked, top to bottom; the focused one where none is.
    IReadOnlyList<Guid> SelectedPayoutIds { get; }

    /// Why they are being voided, or null where the operator backed out.
    string? PromptVoidReason(IReadOnlyList<PayoutDto> payouts);

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
        var choices = (await _shifts.ShiftsAsync())
            .Where(s => s.IsOpen && (s.IsOwn || _shifts.MayActForOthers)).ToList();
        var current = await _sessions.CurrentAsync();

        if (current is not null && choices.All(c => c.SessionId != current.Id))
            choices.Insert(0, new ShiftChoiceDto(current.Id, current.TillNumber,
                current.OpenedByName, current.OpenedAtUtc.ToLocalTime(), IsOpen: true,
                IsOwn: false, AtThisPc: true));

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
        var ids = View.SelectedPayoutIds;
        var picked = _shown.Where(p => ids.Contains(p.Id)).ToList();

        if (picked.Count == 0)
        {
            View.ShowError("Choose the entry to void.");
            return;
        }

        // Ones already voided are left as they are; the rest still go.
        var payouts = picked.Where(p => !p.IsVoided).ToList();

        if (payouts.Count == 0)
        {
            View.ShowInfo(picked.Count == 1
                ? "That entry is already voided."
                : "Those entries are already voided.");
            return;
        }

        // One reason for every entry picked; each void is still the
        // service's to allow or refuse.
        if (View.PromptVoidReason(payouts) is not string reason) return;

        var failed = new List<string>();
        var voided = false;

        foreach (var payout in payouts)
        {
            var result = await _payouts.VoidAsync(payout.Id, reason);
            if (result.IsFailure)
                failed.Add(payouts.Count == 1
                    ? result.Error.Message
                    : $"{payout.KindName} {Format.Money(payout.Amount)}: {result.Error.Message}");
            else
                voided = true;
        }

        if (failed.Count > 0) View.ShowError(string.Join("\n", failed));
        if (!voided) return;

        Changed = true;
        await LoadAsync();
    });
}
