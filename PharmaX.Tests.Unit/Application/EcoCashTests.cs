using FluentAssertions;
using NSubstitute;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Finance;
using PharmaX.Application.Repositories;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Application.Security;
using PharmaX.Application.Tills;
using PharmaX.Domain.Security;
using PharmaX.Domain.Finance;
using PharmaX.Domain.Patients;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Sales;
using PharmaX.Domain.Tills;
using PharmaX.Tests.Unit.Fakes;

namespace PharmaX.Tests.Unit.Application;

/// <summary>
/// EcoCash: taken wherever cash is, held in its own wallet, and declared
/// at cash-up off the merchant statement.
/// </summary>
public class EcoCashTests
{
    private static readonly DateTime Noon =
        new(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);

    // ---- The tender --------------------------------------------------

    [Fact]
    public void EcoCash_lands_in_its_own_wallet_account()
    {
        LedgerCode.For(TenderType.EcoCash).Should().Be(LedgerCode.EcoCashWallet);

        LedgerCode.Standard.Should().Contain(a =>
            a.Code == LedgerCode.EcoCashWallet
            && a.Type == LedgerAccountType.Asset);
    }

    [Fact]
    public void EcoCash_is_cash_like_but_not_in_the_drawer()
    {
        TenderType.EcoCash.IsCashLike().Should().BeTrue();
        TenderType.EcoCash.AffectsDrawer().Should().BeFalse();
        TenderType.EcoCash.IsDeclaredAgainstStatement().Should().BeTrue();
        TenderType.EcoCash.DisplayName().Should().Be("EcoCash");
        TenderType.Card.IsDeclaredAgainstStatement().Should().BeTrue();
        TenderType.Card.AffectsDrawer().Should().BeFalse();
    }

    /// <remarks>
    /// A voucher was posted to the bank because the fallback sent every
    /// tender it did not name there (ADM-OBS-18).
    /// </remarks>
    [Fact]
    public void A_voucher_is_redeemed_rather_than_banked()
    {
        LedgerCode.For(TenderType.Voucher).Should().Be(LedgerCode.VouchersRedeemed);
    }

    // ---- At the till -------------------------------------------------

    [Fact]
    public async Task An_EcoCash_sale_debits_the_wallet_not_the_drawer()
    {
        var builder = new SaleServiceBuilder();
        var sut = builder.Build();

        var opened = await sut.OpenAsync();
        await sut.AddLineAsync(opened.Value.Id, "PAR0500", 1m);

        var finalised = await sut.FinaliseAsync(opened.Value.Id, new[]
        {
            new PaymentDto("EcoCash", 24.00m, "MP260925.1200.A12345")
        });

        finalised.IsSuccess.Should().BeTrue(finalised.Error.Message);

        builder.Ledger.AllBalance.Should().BeTrue();
        builder.Ledger.NetOf(LedgerCode.EcoCashWallet).Should().Be(24.00m);
        builder.Ledger.NetOf(LedgerCode.CashOnHand).Should().Be(0m);
    }

    /// <remarks>The customer sends the exact amount; only cash gives change.</remarks>
    [Fact]
    public async Task EcoCash_over_the_total_is_refused()
    {
        var builder = new SaleServiceBuilder();
        var sut = builder.Build();

        var opened = await sut.OpenAsync();
        await sut.AddLineAsync(opened.Value.Id, "PAR0500", 1m);

        var act = async () => await sut.FinaliseAsync(opened.Value.Id,
            new[] { new PaymentDto("EcoCash", 30.00m, null) });

        (await ResultOrRefusal(act)).Should().Contain("only cash");
    }

    [Fact]
    public async Task EcoCash_and_cash_can_split_a_sale()
    {
        var builder = new SaleServiceBuilder();
        var sut = builder.Build();

        var opened = await sut.OpenAsync();
        await sut.AddLineAsync(opened.Value.Id, "PAR0500", 1m);

        var finalised = await sut.FinaliseAsync(opened.Value.Id, new[]
        {
            new PaymentDto("EcoCash", 20.00m, null),
            new PaymentDto("Cash", 10.00m, null)
        });

        finalised.IsSuccess.Should().BeTrue(finalised.Error.Message);
        finalised.Value.Change.Should().Be(6.00m);

        builder.Ledger.NetOf(LedgerCode.EcoCashWallet).Should().Be(20.00m);
        builder.Ledger.NetOf(LedgerCode.CashOnHand).Should().Be(4.00m);
        builder.Ledger.AllBalance.Should().BeTrue();
    }

    // ---- On account --------------------------------------------------

    [Fact]
    public async Task An_account_paid_by_EcoCash_is_tied_to_the_till_and_the_wallet()
    {
        var builder = new SaleServiceBuilder();
        var account = builder.Accounts.Add(
            Account.Open("ACC-0001", "Moyo, Tendai", Money.Of(500m)));

        var result = await builder.AccountService().TakePaymentAsync(
            account.Id, 15m, "EcoCash", new DateOnly(2026, 9, 25), "MP1");

        result.IsSuccess.Should().BeTrue(result.Error.Message);

        var payment = builder.Accounts.Movements.Single();
        payment.Tender.Should().Be(TenderType.EcoCash);
        payment.TillSessionId.Should().Be(builder.User.TillSessionId);

        builder.Ledger.NetOf(LedgerCode.EcoCashWallet).Should().Be(15m);
        builder.Ledger.AllBalance.Should().BeTrue();
    }

    [Theory]
    [InlineData("Cash")]
    [InlineData("EcoCash")]
    public async Task Cash_or_EcoCash_off_an_account_needs_an_open_till(
        string tender)
    {
        var builder = new SaleServiceBuilder().WithNoTillSession();
        var account = builder.Accounts.Add(
            Account.Open("ACC-0001", "Moyo, Tendai", Money.Of(500m)));

        var result = await builder.AccountService().TakePaymentAsync(
            account.Id, 15m, tender, new DateOnly(2026, 9, 25), null);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ACC-007");
        builder.Accounts.Movements.Should().BeEmpty();
    }

    [Fact]
    public async Task A_card_off_an_account_may_be_taken_in_the_back_office()
    {
        var builder = new SaleServiceBuilder().WithNoTillSession();
        var account = builder.Accounts.Add(
            Account.Open("ACC-0001", "Moyo, Tendai", Money.Of(500m)));

        var result = await builder.AccountService().TakePaymentAsync(
            account.Id, 15m, "Card", new DateOnly(2026, 9, 25), null);

        result.IsSuccess.Should().BeTrue(result.Error.Message);
        builder.Accounts.Movements.Single().TillSessionId.Should().BeNull();
    }

    [Theory]
    [InlineData("MedicalAid")]
    [InlineData("Account")]
    [InlineData("Voucher")]
    [InlineData("Bitcoin")]
    public async Task An_account_cannot_be_paid_by_anything_else(string tender)
    {
        var builder = new SaleServiceBuilder();
        var account = builder.Accounts.Add(
            Account.Open("ACC-0001", "Moyo, Tendai", Money.Of(500m)));

        var result = await builder.AccountService().TakePaymentAsync(
            account.Id, 15m, tender, new DateOnly(2026, 9, 25), null);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ACC-006");
    }

    // ---- Cash-up -----------------------------------------------------

    /// <remarks>
    /// The float and a cash sale are counted in the drawer; the EcoCash
    /// sale is declared off the statement; the account paid in cash at
    /// this till is in the drawer too (ADM-DEF-18).
    /// </remarks>
    [Fact]
    public async Task The_cash_up_expects_EcoCash_and_account_payments_taken_at_the_till()
    {
        var (builder, session, cashUp) = await TradedSessionAsync();

        var declaration = await cashUp.DeclareAsync(
            new Dictionary<string, int> { ["$1"] = 144 },
            new Dictionary<TenderType, decimal> { [TenderType.EcoCash] = 24m });

        var cash = declaration.Variances.Single(v => v.TenderType == "Cash");
        cash.Expected.Should().Be(100m + 24m + 20m,
            "the float, the cash sale and the account paid in cash");
        cash.Variance.Should().Be(0m);

        var ecoCash = declaration.Variances.Single(v => v.TenderType == "EcoCash");
        ecoCash.Expected.Should().Be(24m);
        ecoCash.Declared.Should().Be(24m);
        ecoCash.Variance.Should().Be(0m);

        _ = builder;
        _ = session;
    }

    /// <remarks>
    /// A blank statement is not a statement. Taking the till's own figure
    /// for it would balance every session whether or not the money came.
    /// </remarks>
    [Fact]
    public async Task EcoCash_left_undeclared_is_declared_as_nothing_received()
    {
        var (_, _, cashUp) = await TradedSessionAsync();

        var declaration = await cashUp.DeclareAsync(
            new Dictionary<string, int> { ["$1"] = 144 });

        var ecoCash = declaration.Variances.Single(v => v.TenderType == "EcoCash");
        ecoCash.Declared.Should().Be(0m);
        ecoCash.Variance.Should().Be(-24m);
        ecoCash.RequiresReason.Should().BeTrue();
    }

    /// The card machine's total, not the till's. A blank is nothing
    /// collected, the same as a blank EcoCash statement.
    [Fact]
    public async Task The_card_collected_is_what_the_machine_says()
    {
        var (_, _, cashUp) = await TradedSessionAsync(withCard: true);

        var typed = await cashUp.DeclareAsync(
            new Dictionary<string, int> { ["$1"] = 144 },
            new Dictionary<TenderType, decimal>
            {
                [TenderType.EcoCash] = 24m,
                [TenderType.Card] = 20m
            });

        var card = typed.Variances.Single(v => v.TenderType == "Card");
        card.Expected.Should().Be(24m);
        card.Declared.Should().Be(20m);
        card.Variance.Should().Be(-4m);

        var blank = await cashUp.DeclareAsync(
            new Dictionary<string, int> { ["$1"] = 144 },
            new Dictionary<TenderType, decimal> { [TenderType.EcoCash] = 24m });

        blank.Variances.Single(v => v.TenderType == "Card").Declared.Should().Be(0m);
    }

    [Fact]
    public async Task A_short_EcoCash_statement_comes_off_the_wallet_at_close()
    {
        var (builder, _, cashUp) = await TradedSessionAsync();

        await cashUp.DeclareAsync(
            new Dictionary<string, int> { ["$1"] = 144 },
            new Dictionary<TenderType, decimal> { [TenderType.EcoCash] = 20m });

        var closed = await cashUp.CloseAsync("One EcoCash payment never arrived");

        closed.IsSuccess.Should().BeTrue(closed.Error.Message);

        builder.Ledger.AllBalance.Should().BeTrue();
        builder.Ledger.NetOf(LedgerCode.EcoCashWallet).Should().Be(20m,
            "24.00 taken, 4.00 of it never arrived");
        builder.Ledger.NetOf(LedgerCode.CashShortOrOver).Should().Be(4m);
    }

    [Fact]
    public async Task A_short_EcoCash_statement_posts_against_the_wallet()
    {
        var ledger = new InMemoryLedgerRepository();

        var poster = new PharmaX.Application.Finance.JournalPoster(ledger,
            new FakeDocumentNumbering(),
            new PharmaX.Application.Abstractions.PharmaXOptions(),
            new FakeClock(Noon));

        await poster.PostCashUpAsync(
            TillSession.Open(Guid.NewGuid(), Guid.NewGuid(), "1",
                             Guid.NewGuid(), Money.Of(100m), Noon),
            TenderType.EcoCash, Money.Of(100m), Money.Of(90m));

        ledger.AllBalance.Should().BeTrue();
        ledger.NetOf(LedgerCode.EcoCashWallet).Should().Be(-10m);
        ledger.NetOf(LedgerCode.CashShortOrOver).Should().Be(10m);
        ledger.NetOf(LedgerCode.CashOnHand).Should().Be(0m);
    }

    [Fact]
    public async Task A_pharmacist_cannot_cash_up_and_close()
    {
        var cashUp = new CashUpService(
            Substitute.For<ITillSessionRepository>(),
            Substitute.For<ISaleRepository>(),
            Substitute.For<IReturnRepository>(),
            Substitute.For<IAccountRepository>(),
            Substitute.For<IJournalPoster>(),
            new FakeSessionContext(),
            new FakeAuditWriter(),
            Substitute.For<IOutbox>(),
            new FakeUnitOfWork(),
            new FakeCurrentUser(),
            new FakeClock(Noon),
            new PermissionService(new FakeCurrentUser(),
                new FakePermissionRepository(granted: new[]
                    { PermissionCode.SaleCreate }),
                new FakeAuditWriter()));

        var result = await cashUp.CloseAsync(null);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("POS-027");
    }

    // ---- Helpers -----------------------------------------------------

    /// <summary>
    /// A session with a 100.00 float, a 24.00 cash sale, a 24.00 EcoCash
    /// sale and a 20.00 cash payment off an account.
    /// </summary>
    internal static async Task<(SaleServiceBuilder, TillSession, CashUpService)>
        TradedSessionAsync(bool withCard = false)
    {
        var builder = new SaleServiceBuilder();

        var session = TillSession.Open(Guid.NewGuid(), Guid.NewGuid(), "1",
            builder.User.Id, Money.Of(100m), Noon);

        builder.User.TillSessionId = session.Id;

        var sut = builder.Build();

        foreach (var tender in new[] { "Cash", "EcoCash" })
        {
            var opened = await sut.OpenAsync();
            await sut.AddLineAsync(opened.Value.Id, "PAR0500", 1m);

            var paid = await sut.FinaliseAsync(opened.Value.Id,
                new[] { new PaymentDto(tender, 24m, null) });

            paid.IsSuccess.Should().BeTrue(paid.Error.Message);
        }

        if (withCard)
        {
            var opened = await sut.OpenAsync();
            await sut.AddLineAsync(opened.Value.Id, "PAR0500", 1m);
            var paid = await sut.FinaliseAsync(opened.Value.Id,
                new[] { new PaymentDto("Card", 24m, null) });
            paid.IsSuccess.Should().BeTrue(paid.Error.Message);
        }

        var account = builder.Accounts.Add(
            Account.Open("ACC-0001", "Moyo, Tendai", Money.Of(500m)));

        (await builder.AccountService().TakePaymentAsync(account.Id, 20m,
            "Cash", new DateOnly(2026, 9, 25), null))
            .IsSuccess.Should().BeTrue();

        var sessions = Substitute.For<ITillSessionRepository>();
        sessions.CurrentAsync(Arg.Any<CancellationToken>()).Returns(session);

        sessions.DenominationsAsync(Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new[] { Denomination.Create("$1", 1m, isNote: true, 1) });

        TillDeclaration? latest = null;

        await sessions.AddDeclarationAsync(
            Arg.Do<TillDeclaration>(d => latest = d),
            Arg.Any<CancellationToken>());

        sessions.LatestDeclarationAsync(session.Id,
                Arg.Any<CancellationToken>())
            .Returns(_ => latest);

        var returns = Substitute.For<IReturnRepository>();
        returns.ForSessionAsync(session.Id, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SaleReturn>());

        var cashUp = new CashUpService(sessions, builder.Sales, returns,
            builder.Accounts, builder.Journals(), new FakeSessionContext(),
            builder.Audit, builder.Outbox, builder.UnitOfWork, builder.User,
            builder.Clock, builder.PermissionService());

        return (builder, session, cashUp);
    }

    /// The failure message, whether the sale returned it or threw it.
    private static async Task<string> ResultOrRefusal(
        Func<Task<PharmaX.Application.Results.Result<FinaliseDto>>> act)
    {
        try
        {
            var result = await act();
            result.IsFailure.Should().BeTrue();
            return result.Error.Message;
        }
        catch (PharmaX.Domain.Exceptions.DomainException ex)
        {
            return ex.Message;
        }
    }
}
