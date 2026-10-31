using NSubstitute;
using PharmaX.Application.Results;
using PharmaX.Application.Tills;
using PharmaX.Application.Tills.Dtos;
using PharmaX.Desktop.Features.Tills;
using PharmaX.Domain.Tills;
using Xunit;

namespace PharmaX.Tests.Unit.Presenters;

public sealed class PayoutsPresenterTests
{
    private readonly ITillPayoutService _payouts = Substitute.For<ITillPayoutService>();
    private readonly IPayoutsView _view = Substitute.For<IPayoutsView>();

    /// <summary>
    /// Several entries picked are voided on one reason, one already voided
    /// is left alone, and one the service refuses is named without
    /// stopping the others.
    /// </summary>
    [Fact]
    public async Task Voiding_several_payouts_asks_one_reason_and_names_the_one_refused()
    {
        var shift = Guid.NewGuid();
        PayoutDto Payout(PayoutKind kind, decimal amount, bool voided = false) =>
            new(Guid.NewGuid(), shift, kind, amount, "", DateTime.Now, "Sam", voided);

        var lunch = Payout(PayoutKind.Lunch, 50m);
        var drawings = Payout(PayoutKind.Drawings, 200m);
        var gone = Payout(PayoutKind.Lunch, 30m, voided: true);
        var other = Payout(PayoutKind.Expense, 20m);

        _payouts.ListAsync(shift, Arg.Any<CancellationToken>())
                .Returns(new[] { lunch, drawings, gone, other });
        _payouts.VoidAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Result.Ok());
        _payouts.VoidAsync(drawings.Id, Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Result.Fail("TILL-001", "The till is cashed up."));

        _view.SelectedShiftId.Returns(shift);
        _view.SelectedPayoutIds.Returns(new[] { lunch.Id, drawings.Id, gone.Id, other.Id });
        _view.PromptVoidReason(Arg.Any<IReadOnlyList<PayoutDto>>()).Returns("Entered twice");

        var sut = new PayoutsPresenter(_payouts, Substitute.For<IShiftCashUpService>(),
            Substitute.For<ITillSessionService>());
        sut.Attach(_view);

        _view.ShiftChanged += Raise.EventWith(this, EventArgs.Empty);
        _view.VoidRequested += Raise.EventWith(this, EventArgs.Empty);
        await Task.Yield();

        _view.Received(1).PromptVoidReason(Arg.Is<IReadOnlyList<PayoutDto>>(p => p.Count == 3));
        foreach (var p in new[] { lunch, drawings, other })
            await _payouts.Received(1).VoidAsync(p.Id, "Entered twice", Arg.Any<CancellationToken>());
        await _payouts.DidNotReceive().VoidAsync(gone.Id, Arg.Any<string>(), Arg.Any<CancellationToken>());
        _view.Received(1).ShowError(Arg.Is<string>(m =>
            m.StartsWith("Drawings ") && m.EndsWith(": The till is cashed up.")));
        Assert.True(sut.Changed);
    }
}
