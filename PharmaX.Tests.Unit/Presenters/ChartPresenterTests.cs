using NSubstitute;
using PharmaX.Application.Finance;
using PharmaX.Application.Results;
using PharmaX.Desktop.Features.Finance;
using PharmaX.Domain.Finance;
using Xunit;

namespace PharmaX.Tests.Unit.Presenters;

public sealed class ChartPresenterTests
{
    private readonly ILedgerService _ledger = Substitute.For<ILedgerService>();
    private readonly IChartView _view = Substitute.For<IChartView>();

    /// <summary>
    /// Several accounts picked are deactivated on one confirmation, and
    /// one the service refuses is named without stopping the others.
    /// </summary>
    [Fact]
    public async Task Deactivating_several_accounts_asks_once_and_names_the_one_refused()
    {
        var cash = new LedgerAccountDto(Guid.NewGuid(), "1000", "Cash", LedgerAccountType.Asset, true);
        var bank = new LedgerAccountDto(Guid.NewGuid(), "1100", "Bank", LedgerAccountType.Asset, true);
        var sales = new LedgerAccountDto(Guid.NewGuid(), "4000", "Sales", LedgerAccountType.Income, true);

        _ledger.AccountsAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
               .Returns(Result<IReadOnlyList<LedgerAccountDto>>.Ok(new[] { cash, bank, sales }));
        _ledger.DeactivateAccountAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
               .Returns(Result.Ok());
        _ledger.DeactivateAccountAsync(bank.Id, Arg.Any<CancellationToken>())
               .Returns(Result.Fail("GL-001", "It has postings this period."));

        _view.SelectedAccountIds.Returns(new[] { cash.Id, bank.Id, sales.Id });
        _view.Confirm(Arg.Any<string>()).Returns(true);

        var sut = new ChartPresenter(_ledger);
        sut.Attach(_view);
        await sut.InitialiseAsync();

        _view.DeactivateRequested += Raise.EventWith(this, EventArgs.Empty);

        _view.Received(1).Confirm("Deactivate 3 accounts? They stay in the books, and leave this list.");
        foreach (var account in new[] { cash, bank, sales })
            await _ledger.Received(1).DeactivateAccountAsync(account.Id, Arg.Any<CancellationToken>());
        _view.Received(1).ShowError("1100 Bank: It has postings this period.");
    }
}
