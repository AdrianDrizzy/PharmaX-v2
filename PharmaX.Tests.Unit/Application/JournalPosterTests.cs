using FluentAssertions;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Domain.Finance;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Sales;
using PharmaX.Domain.Tills;
using PharmaX.Tests.Unit.Fakes;

namespace PharmaX.Tests.Unit.Application;

/// <summary>
/// The books writing themselves as the pharmacy trades.
/// </summary>
/// <remarks>
/// Every test here ends up asserting the same thing in the end - that
/// the batch balanced - because a journal that does not balance is the
/// one failure that makes every figure after it meaningless.
/// </remarks>
public class JournalPosterTests
{
    private static readonly DateTime Noon =
        new(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// A cash sale: money into the drawer, revenue and VAT recognised.
    /// </summary>
    [Fact]
    public async Task A_cash_sale_debits_the_drawer_and_splits_the_vat()
    {
        var builder = new SaleServiceBuilder();
        var sut = builder.Build();

        var opened = await sut.OpenAsync();
        await sut.AddLineAsync(opened.Value.Id, "PAR0500", 1m);

        await sut.FinaliseAsync(opened.Value.Id, new[]
        {
            new PaymentDto("Cash", 24.00m, null)
        });

        builder.Ledger.AllBalance.Should().BeTrue();

        // 24.00 inclusive of 15% is 20.87 net and 3.13 of VAT.
        builder.Ledger.NetOf(LedgerCode.CashOnHand).Should().Be(24.00m);
        builder.Ledger.NetOf(LedgerCode.Sales).Should().Be(-20.87m);
        builder.Ledger.NetOf(LedgerCode.VatControl).Should().Be(-3.13m);
    }

    /// <summary>
    /// The drawer is debited what the sale was worth, and no more.
    /// </summary>
    /// <remarks>
    /// There is no change line in the journal because there cannot be
    /// one: BR-POS-01 refuses to finalise a sale whose tender does not
    /// equal its total, so the change has already been settled at the
    /// payment screen. This test is what will notice if that rule ever
    /// moves.
    /// </remarks>
    [Fact]
    public async Task A_sale_cannot_be_tendered_more_than_it_is_worth()
    {
        var builder = new SaleServiceBuilder();
        var sut = builder.Build();

        var opened = await sut.OpenAsync();
        await sut.AddLineAsync(opened.Value.Id, "PAR0500", 1m);

        var act = () => sut.FinaliseAsync(opened.Value.Id, new[]
        {
            new PaymentDto("Cash", 50.00m, null)
        });

        await act.Should()
            .ThrowAsync<PharmaX.Domain.Exceptions.DomainException>()
            .WithMessage("*BR-POS-01*");

        builder.Ledger.Posted.Should().BeEmpty();
    }

    /// <remarks>
    /// Cash is in the drawer at close of business and a card payment is
    /// not, which is exactly the distinction cash-up depends on.
    /// </remarks>
    [Fact]
    public async Task A_card_payment_reaches_the_bank_rather_than_the_drawer()
    {
        var builder = new SaleServiceBuilder();
        var sut = builder.Build();

        var opened = await sut.OpenAsync();
        await sut.AddLineAsync(opened.Value.Id, "PAR0500", 1m);

        await sut.FinaliseAsync(opened.Value.Id, new[]
        {
            new PaymentDto("Card", 24.00m, "auth 12345")
        });

        builder.Ledger.NetOf(LedgerCode.Bank).Should().Be(24.00m);
        builder.Ledger.NetOf(LedgerCode.CashOnHand).Should().Be(0m);
    }

    [Fact]
    public async Task A_split_payment_debits_both_sides()
    {
        var builder = new SaleServiceBuilder();
        var sut = builder.Build();

        var opened = await sut.OpenAsync();
        await sut.AddLineAsync(opened.Value.Id, "PAR0500", 1m);

        await sut.FinaliseAsync(opened.Value.Id, new[]
        {
            new PaymentDto("Cash", 10.00m, null),
            new PaymentDto("Card", 14.00m, "auth 67890")
        });

        builder.Ledger.AllBalance.Should().BeTrue();
        builder.Ledger.NetOf(LedgerCode.CashOnHand).Should().Be(10.00m);
        builder.Ledger.NetOf(LedgerCode.Bank).Should().Be(14.00m);
    }

    [Fact]
    public async Task Turning_auto_posting_off_writes_no_journals()
    {
        var builder = new SaleServiceBuilder { AutoPost = false };
        var sut = builder.Build();

        var opened = await sut.OpenAsync();
        await sut.AddLineAsync(opened.Value.Id, "PAR0500", 1m);

        await sut.FinaliseAsync(opened.Value.Id, new[]
        {
            new PaymentDto("Cash", 24.00m, null)
        });

        builder.Ledger.Posted.Should().BeEmpty();
    }

    // ---- Cash-up ---------------------------------------------------------

    /// <summary>
    /// A drawer that balanced has nothing to say.
    /// </summary>
    [Fact]
    public async Task A_till_that_balanced_posts_nothing()
    {
        var ledger = new InMemoryLedgerRepository();

        await Poster(ledger).PostCashUpAsync(Session(), Money.Of(500m),
            Money.Of(500m));

        ledger.Posted.Should().BeEmpty();
    }

    /// <remarks>
    /// Short: the drawer holds less than the sales say it should, so the
    /// difference is a loss and the cash account comes down to the truth.
    /// </remarks>
    [Fact]
    public async Task A_short_till_is_an_expense()
    {
        var ledger = new InMemoryLedgerRepository();

        await Poster(ledger).PostCashUpAsync(Session(), Money.Of(500m),
            Money.Of(480m));

        ledger.AllBalance.Should().BeTrue();
        ledger.NetOf(LedgerCode.CashShortOrOver).Should().Be(20m);
        ledger.NetOf(LedgerCode.CashOnHand).Should().Be(-20m);
    }

    [Fact]
    public async Task An_over_till_goes_the_other_way()
    {
        var ledger = new InMemoryLedgerRepository();

        await Poster(ledger).PostCashUpAsync(Session(), Money.Of(500m),
            Money.Of(512.50m));

        ledger.AllBalance.Should().BeTrue();
        ledger.NetOf(LedgerCode.CashOnHand).Should().Be(12.50m);
        ledger.NetOf(LedgerCode.CashShortOrOver).Should().Be(-12.50m);
    }

    private static PharmaX.Application.Finance.JournalPoster Poster(
        InMemoryLedgerRepository ledger) =>
        new(ledger, new FakeDocumentNumbering(),
            new PharmaX.Application.Abstractions.PharmaXOptions(),
            new FakeClock(Noon));

    private static TillSession Session() =>
        TillSession.Open(Guid.NewGuid(), Guid.NewGuid(), "1",
                         Guid.NewGuid(), Money.Of(500m), Noon);
}
