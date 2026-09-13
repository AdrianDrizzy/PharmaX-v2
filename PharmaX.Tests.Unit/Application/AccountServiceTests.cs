using FluentAssertions;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Domain.Finance;
using PharmaX.Domain.Patients;
using PharmaX.Domain.Primitives;
using PharmaX.Tests.Unit.Fakes;

namespace PharmaX.Tests.Unit.Application;

/// <summary>
/// Patient accounts: charging one at the till, and the limits that stop
/// a pharmacy giving stock away on credit.
/// </summary>
/// <remarks>
/// Account.Charge already enforced the hold and the credit limit, and
/// had done since the domain was written. Nothing ever called it: the
/// till offered "Account" as a tender, the ledger recorded a debtor, and
/// no patient's account moved. These are the tests that would have
/// caught that.
/// </remarks>
public class AccountServiceTests
{
    [Fact]
    public async Task A_sale_on_account_charges_the_account()
    {
        var builder = new SaleServiceBuilder();
        var account = Open(builder, limit: 500m);

        await SellOnAccount(builder, account, 24.00m);

        account.Balance.Amount.Should().Be(24.00m);
        account.Available.Amount.Should().Be(476.00m);
    }

    /// <summary>
    /// The statement behind the balance.
    /// </summary>
    /// <remarks>
    /// A balance with no movements cannot be queried or disputed, and
    /// "you owe twenty-four dollars" is not an answer anybody accepts
    /// without a list.
    /// </remarks>
    [Fact]
    public async Task A_charge_leaves_a_movement_behind_it()
    {
        var builder = new SaleServiceBuilder();
        var account = Open(builder, limit: 500m);

        await SellOnAccount(builder, account, 24.00m);

        var movement = builder.Accounts.Movements.Should().ContainSingle()
            .Subject;

        movement.Kind.Should().Be(AccountEntryKind.Charge);
        movement.Amount.Amount.Should().Be(24.00m);
        movement.Increases.Should().BeTrue();

        // The balance and its own history agree.
        (await builder.Accounts.MovementTotalAsync(account.Id))
            .Should().Be(account.Balance.Amount);
    }

    /// <summary>
    /// The control that did not exist.
    /// </summary>
    [Fact]
    public async Task A_sale_past_the_credit_limit_is_refused()
    {
        var builder = new SaleServiceBuilder();
        var account = Open(builder, limit: 10m);

        var result = await SellOnAccount(builder, account, 24.00m);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ACC-010");
        result.Error.Message.Should().Contain("credit limit");

        // And nothing moved: not the balance, not the movements.
        account.Balance.IsZero.Should().BeTrue();
        builder.Accounts.Movements.Should().BeEmpty();
    }

    [Fact]
    public async Task A_sale_on_an_account_on_hold_is_refused()
    {
        var builder = new SaleServiceBuilder();
        var account = Open(builder, limit: 500m);

        account.PlaceOnHold("Two months overdue");

        var result = await SellOnAccount(builder, account, 24.00m);

        result.IsFailure.Should().BeTrue();

        // The reason reaches the cashier, who is the one who has to tell
        // the customer.
        result.Error.Message.Should().Contain("Two months overdue");
        account.Balance.IsZero.Should().BeTrue();
    }

    /// <summary>
    /// The hole itself: an account tender with no account on the sale.
    /// </summary>
    /// <remarks>
    /// This was the whole defect. The till offered Account, nobody had
    /// to choose whose, and the sale went through.
    /// </remarks>
    [Fact]
    public async Task An_account_tender_with_no_account_is_refused()
    {
        var builder = new SaleServiceBuilder();
        var sut = builder.Build();

        var opened = await sut.OpenAsync();
        await sut.AddLineAsync(opened.Value.Id, "PAR0500", 1m);

        var result = await sut.FinaliseAsync(opened.Value.Id, new[]
        {
            new PaymentDto("Account", 24.00m, null)
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("POS-020");
        result.Error.Message.Should().Contain("no account has been chosen");
    }

    /// <remarks>
    /// A patient can settle part in cash and put the rest on account,
    /// and only the account part may touch the account.
    /// </remarks>
    [Fact]
    public async Task Only_the_account_part_of_a_split_is_charged()
    {
        var builder = new SaleServiceBuilder();
        var account = Open(builder, limit: 500m);

        var sut = builder.Build();
        var opened = await sut.OpenAsync();
        await sut.AddLineAsync(opened.Value.Id, "PAR0500", 1m);

        var sale = await builder.Sales.GetAsync(opened.Value.Id);
        sale!.AssignPatient(Guid.NewGuid(), account.Id);

        await sut.FinaliseAsync(opened.Value.Id, new[]
        {
            new PaymentDto("Cash", 10.00m, null),
            new PaymentDto("Account", 14.00m, null)
        });

        account.Balance.Amount.Should().Be(14.00m);
        builder.Ledger.NetOf(LedgerCode.CashOnHand).Should().Be(10.00m);
        builder.Ledger.NetOf(LedgerCode.TradeDebtors).Should().Be(14.00m);
        builder.Ledger.AllBalance.Should().BeTrue();
    }

    // ---- Payments off an account ----------------------------------------

    [Fact]
    public async Task A_payment_reduces_the_balance_and_posts_the_money()
    {
        var builder = new SaleServiceBuilder();
        var account = Open(builder, limit: 500m);

        await SellOnAccount(builder, account, 24.00m);

        var result = await builder.AccountService().TakePaymentAsync(
            account.Id, 20m, "Cash", new DateOnly(2026, 9, 13), "RCPT-1");

        result.IsSuccess.Should().BeTrue();
        account.Balance.Amount.Should().Be(4.00m);

        // Money in, debtor down - and no VAT, because that was dealt
        // with when the sale went on the account.
        builder.Ledger.AllBalance.Should().BeTrue();
        builder.Ledger.NetOf(LedgerCode.TradeDebtors).Should().Be(4.00m);
    }

    /// <remarks>
    /// Somebody settling a round figure, or paying for a script they
    /// then do not collect. Real, and not an error.
    /// </remarks>
    [Fact]
    public async Task An_overpayment_leaves_the_account_in_credit()
    {
        var builder = new SaleServiceBuilder();
        var account = Open(builder, limit: 500m);

        await SellOnAccount(builder, account, 24.00m);

        await builder.AccountService().TakePaymentAsync(account.Id, 30m,
            "Cash", new DateOnly(2026, 9, 13), null);

        account.Balance.Amount.Should().Be(-6.00m);
    }

    [Fact]
    public async Task A_payment_of_nothing_is_refused()
    {
        var builder = new SaleServiceBuilder();
        var account = Open(builder, limit: 500m);

        var result = await builder.AccountService().TakePaymentAsync(
            account.Id, 0m, "Cash", new DateOnly(2026, 9, 13), null);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ACC-005");
    }

    // ---- The statement ---------------------------------------------------

    [Fact]
    public async Task A_statement_runs_a_balance_down_the_page()
    {
        var builder = new SaleServiceBuilder();
        var account = Open(builder, limit: 500m);

        await SellOnAccount(builder, account, 24.00m);

        await builder.AccountService().TakePaymentAsync(account.Id, 10m,
            "Cash", new DateOnly(2026, 9, 13), "RCPT-1");

        var statement = await builder.AccountService().StatementAsync(
            account.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        statement.IsSuccess.Should().BeTrue();
        statement.Value.Opening.Should().Be(0m);
        statement.Value.Lines.Should().HaveCount(2);

        statement.Value.Lines[0].Charged.Should().Be(24.00m);
        statement.Value.Lines[0].Balance.Should().Be(24.00m);

        statement.Value.Lines[1].Paid.Should().Be(10.00m);
        statement.Value.Lines[1].Balance.Should().Be(14.00m);

        statement.Value.Closing.Should().Be(14.00m);
    }

    // ---- Helpers ---------------------------------------------------------

    private static Account Open(SaleServiceBuilder builder, decimal limit) =>
        builder.Accounts.Add(Account.Open("ACC0001", "Grace Moyo",
            Money.Of(limit), Guid.NewGuid()));

    /// <summary>
    /// One sale, wholly on the account.
    /// </summary>
    /// <remarks>
    /// The product the builder seeds comes to 24.00, so the caller says
    /// what it should be tendered as rather than what it costs.
    /// </remarks>
    private static async Task<PharmaX.Application.Results.Result<FinaliseDto>>
        SellOnAccount(SaleServiceBuilder builder, Account account,
                      decimal amount)
    {
        var sut = builder.Build();

        var opened = await sut.OpenAsync();
        await sut.AddLineAsync(opened.Value.Id, "PAR0500", 1m);

        var sale = await builder.Sales.GetAsync(opened.Value.Id);
        sale!.AssignPatient(account.PatientId!.Value, account.Id);

        return await sut.FinaliseAsync(opened.Value.Id, new[]
        {
            new PaymentDto("Account", amount, null)
        });
    }
}
