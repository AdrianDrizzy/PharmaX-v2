using FluentAssertions;
using PharmaX.Application.Finance;
using PharmaX.Application.Security;
using PharmaX.Domain.Finance;
using PharmaX.Domain.Security;
using PharmaX.Tests.Unit.Fakes;

namespace PharmaX.Tests.Unit.Application;

/// <summary>
/// The parts of the books a person touches: the chart, hand-written
/// journals, and the trial balance that proves the whole thing hangs
/// together.
/// </summary>
public class LedgerServiceTests
{
    private static readonly DateOnly Today = new(2026, 9, 13);

    [Fact]
    public async Task The_standard_chart_is_there_to_post_to()
    {
        var result = await new LedgerBuilder().Build().AccountsAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(LedgerCode.Standard.Count);

        result.Value.Should().Contain(a => a.Code == LedgerCode.Sales
                                           && a.Name == "Sales");
    }

    [Fact]
    public async Task A_balanced_journal_posts()
    {
        var builder = new LedgerBuilder();

        var result = await builder.Build().PostJournalAsync(Today,
            "Month-end stock", new[]
            {
                new JournalLineDto(LedgerCode.StockOnHand, 1500m, true, null),
                new JournalLineDto(LedgerCode.Purchases, 1500m, false, null)
            });

        result.IsSuccess.Should().BeTrue();
        result.Value.IsPosted.Should().BeTrue();
        result.Value.Source.Should().Be("MANUAL");

        builder.Ledger.AllBalance.Should().BeTrue();
        builder.Ledger.NetOf(LedgerCode.StockOnHand).Should().Be(1500m);
    }

    /// <summary>
    /// The one rule the whole screen exists to enforce.
    /// </summary>
    [Fact]
    public async Task An_unbalanced_journal_is_refused()
    {
        var builder = new LedgerBuilder();

        var result = await builder.Build().PostJournalAsync(Today, "Oops",
            new[]
            {
                new JournalLineDto(LedgerCode.StockOnHand, 1500m, true, null),
                new JournalLineDto(LedgerCode.Purchases, 1400m, false, null)
            });

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("FIN-013");
        result.Error.Message.Should().Contain("do not equal");

        // And nothing was filed: a refused journal leaves no trace.
        builder.Ledger.Posted.Should().BeEmpty();
    }

    [Fact]
    public async Task A_journal_to_an_account_that_does_not_exist_is_refused()
    {
        var result = await new LedgerBuilder().Build().PostJournalAsync(
            Today, "Typo", new[]
            {
                new JournalLineDto("9999", 100m, true, null),
                new JournalLineDto(LedgerCode.Bank, 100m, false, null)
            });

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("FIN-011");
        result.Error.Message.Should().Contain("9999");
    }

    [Fact]
    public async Task A_one_sided_journal_is_refused()
    {
        var result = await new LedgerBuilder().Build().PostJournalAsync(
            Today, "Half a journal", new[]
            {
                new JournalLineDto(LedgerCode.Bank, 100m, true, null)
            });

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("FIN-010");
    }

    /// <summary>
    /// Take one of these out of use and the next sale of the day fails
    /// at a till, a long way from where the mistake was made.
    /// </summary>
    [Fact]
    public async Task An_account_PharmaX_posts_to_cannot_be_retired()
    {
        var builder = new LedgerBuilder();
        var sut = builder.Build();

        var sales = (await sut.AccountsAsync()).Value
            .Single(a => a.Code == LedgerCode.Sales);

        var result = await sut.DeactivateAccountAsync(sales.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("FIN-005");
    }

    [Fact]
    public async Task An_account_somebody_added_can_be_retired()
    {
        var builder = new LedgerBuilder();
        var sut = builder.Build();

        var added = await sut.AddAccountAsync("6100", "Rent",
                                              LedgerAccountType.Expense);

        var result = await sut.DeactivateAccountAsync(added.Value.Id);

        result.IsSuccess.Should().BeTrue();
        (await sut.AccountsAsync()).Value.Should()
            .NotContain(a => a.Code == "6100");
    }

    [Fact]
    public async Task An_account_code_cannot_be_used_twice()
    {
        var sut = new LedgerBuilder().Build();

        var result = await sut.AddAccountAsync(LedgerCode.Sales, "Turnover",
                                               LedgerAccountType.Income);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("FIN-003");
    }

    // ---- Trial balance ---------------------------------------------------

    /// <summary>
    /// Every account nets to one column, and the two columns agree.
    /// </summary>
    [Fact]
    public async Task The_trial_balance_nets_each_account_and_balances()
    {
        var builder = new LedgerBuilder();
        var sut = builder.Build();

        await sut.PostJournalAsync(Today, "Opening float", new[]
        {
            new JournalLineDto(LedgerCode.CashOnHand, 500m, true, null),
            new JournalLineDto(LedgerCode.OwnersCapital, 500m, false, null)
        });

        await sut.PostJournalAsync(Today, "Banked the float", new[]
        {
            new JournalLineDto(LedgerCode.Bank, 200m, true, null),
            new JournalLineDto(LedgerCode.CashOnHand, 200m, false, null)
        });

        var balance = (await sut.TrialBalanceAsync(Today)).Value;

        balance.IsBalanced.Should().BeTrue();
        balance.TotalDebits.Should().Be(500m);

        // Cash was debited 500 and credited 200: it shows 300 on the
        // debit side and nothing on the credit side.
        var cash = balance.Lines.Single(l => l.Code == LedgerCode.CashOnHand);
        cash.Debit.Should().Be(300m);
        cash.Credit.Should().Be(0m);

        var capital = balance.Lines
            .Single(l => l.Code == LedgerCode.OwnersCapital);

        capital.Credit.Should().Be(500m);
        capital.Debit.Should().Be(0m);
    }

    /// <remarks>
    /// A trial balance is as at a date, so a journal posted after it is
    /// not in it.
    /// </remarks>
    [Fact]
    public async Task A_later_journal_is_not_in_an_earlier_balance()
    {
        var sut = new LedgerBuilder().Build();

        await sut.PostJournalAsync(Today.AddDays(1), "Tomorrow", new[]
        {
            new JournalLineDto(LedgerCode.CashOnHand, 500m, true, null),
            new JournalLineDto(LedgerCode.OwnersCapital, 500m, false, null)
        });

        var balance = (await sut.TrialBalanceAsync(Today)).Value;

        balance.TotalDebits.Should().Be(0m);
    }

    [Fact]
    public async Task Reading_the_books_needs_permission()
    {
        var builder = new LedgerBuilder
        {
            Permissions = new FakePermissionRepository(
                granted: Array.Empty<string>())
        };

        var act = () => builder.Build().TrialBalanceAsync(Today);

        await act.Should().ThrowAsync<PermissionDeniedException>();
    }

    /// <remarks>
    /// A cashier can read the books where the site has granted it, but
    /// posting is a manager's job: a till shortage that can be journalled
    /// away by the person who caused it is not a control at all.
    /// </remarks>
    [Fact]
    public async Task Posting_a_journal_needs_more_than_reading_one()
    {
        var builder = new LedgerBuilder
        {
            Permissions = new FakePermissionRepository(
                granted: new[] { PermissionCode.LedgerRead })
        };

        var sut = builder.Build();

        (await sut.TrialBalanceAsync(Today)).IsSuccess.Should().BeTrue();

        var act = () => sut.PostJournalAsync(Today, "Nice try", new[]
        {
            new JournalLineDto(LedgerCode.CashOnHand, 20m, true, null),
            new JournalLineDto(LedgerCode.CashShortOrOver, 20m, false, null)
        });

        await act.Should().ThrowAsync<PermissionDeniedException>();
    }
}

internal sealed class LedgerBuilder
{
    private static readonly DateTime Noon =
        new(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);

    public InMemoryLedgerRepository Ledger { get; } = new();
    public FakeDocumentNumbering Numbering { get; } = new();
    public FakeAuditWriter Audit { get; } = new();
    public FakeUnitOfWork UnitOfWork { get; } = new();
    public FakeCurrentUser User { get; } = new();
    public FakeClock Clock { get; } = new(Noon);

    public FakePermissionRepository Permissions { get; set; } =
        new(granted: new[]
        {
            PermissionCode.LedgerRead, PermissionCode.LedgerPost,
            PermissionCode.AccountDeactivate
        });

    public LedgerService Build() =>
        new(Ledger, Numbering,
            new PermissionService(User, Permissions, Audit),
            Audit, UnitOfWork, Clock);
}
