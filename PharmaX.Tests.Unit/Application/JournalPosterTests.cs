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
    /// The drawer is debited what the sale was worth, and no more - even
    /// when the customer hands over more and is given change.
    /// </summary>
    /// <remarks>
    /// This test used to prove that over-tender was refused outright
    /// (BR-POS-01), which is what left the till unable to give change
    /// (DEF-0019). The rule it was really guarding is kept: the change
    /// goes back out of the drawer, so the journal debits cash with the
    /// sale's worth, the books balance, and the change is recorded on the
    /// sale rather than lost.
    /// </remarks>
    [Fact]
    public async Task Cash_over_the_total_gives_change_and_debits_only_the_sale()
    {
        var builder = new SaleServiceBuilder();
        var sut = builder.Build();

        var opened = await sut.OpenAsync();
        await sut.AddLineAsync(opened.Value.Id, "PAR0500", 1m);

        var finalised = await sut.FinaliseAsync(opened.Value.Id, new[]
        {
            new PaymentDto("Cash", 50.00m, null)
        });

        finalised.IsSuccess.Should().BeTrue(finalised.Error.Message);
        finalised.Value.Total.Should().Be(24.00m);
        finalised.Value.Change.Should().Be(26.00m);

        builder.Ledger.AllBalance.Should().BeTrue();
        builder.Ledger.NetOf(LedgerCode.CashOnHand).Should().Be(24.00m,
            "the change went back out of the drawer");

        var sale = await sut.GetAsync(opened.Value.Id);
        sale.Value.Totals.Tendered.Should().Be(50.00m);
        sale.Value.Totals.Change.Should().Be(26.00m);
        sale.Value.Payments!.Sum(p => p.Amount).Should().Be(24.00m);
    }

    /// <summary>
    /// Only cash gives change: a card or an account tendered for more than
    /// the sale is a keying error, and nothing is posted.
    /// </summary>
    [Fact]
    public async Task A_sale_cannot_be_tendered_more_than_it_is_worth_on_card()
    {
        var builder = new SaleServiceBuilder();
        var sut = builder.Build();

        var opened = await sut.OpenAsync();
        await sut.AddLineAsync(opened.Value.Id, "PAR0500", 1m);

        var act = () => sut.FinaliseAsync(opened.Value.Id, new[]
        {
            new PaymentDto("Card", 50.00m, null)
        });

        await act.Should()
            .ThrowAsync<PharmaX.Domain.Exceptions.DomainException>()
            .WithMessage("*only cash can give change*");

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

    // ---- Medical aid claims ---------------------------------------------

    /// <summary>
    /// The entry the books were missing entirely.
    /// </summary>
    /// <remarks>
    /// A ninety-dollar dispensing where the scheme pays sixty recorded
    /// thirty dollars of revenue before this: the patient's co-payment
    /// went through the till and the scheme's share went nowhere at all.
    /// For a dispensary that is most of the turnover.
    /// </remarks>
    [Fact]
    public async Task A_claim_debits_the_scheme_and_recognises_the_revenue()
    {
        var ledger = new InMemoryLedgerRepository();

        var claim = PharmaX.Domain.Claims.MedicalAidClaim.Raise("CLM001",
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "MEM1", "00",
            Money.Of(60m), new DateOnly(2026, 9, 13));

        await Poster(ledger).PostClaimAsync(claim, "Alpha Med", new[]
        {
            PharmaX.Domain.Pricing.VatCalculator.FromInclusive(
                Money.Of(60m), 15m)
        });

        ledger.AllBalance.Should().BeTrue();

        // 60.00 inclusive of 15% is 52.17 net and 7.83 of VAT.
        ledger.NetOf(LedgerCode.MedicalAidDebtors).Should().Be(60m);
        ledger.NetOf(LedgerCode.Sales).Should().Be(-52.17m);
        ledger.NetOf(LedgerCode.VatControl).Should().Be(-7.83m);
    }

    /// <remarks>
    /// Kept apart from Trade Debtors on purpose: a pharmacy chases a
    /// scheme and chases a patient in completely different ways.
    /// </remarks>
    [Fact]
    public async Task A_claim_does_not_touch_trade_debtors()
    {
        var ledger = new InMemoryLedgerRepository();

        var claim = PharmaX.Domain.Claims.MedicalAidClaim.Raise("CLM002",
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "MEM1", null,
            Money.Of(60m), new DateOnly(2026, 9, 13));

        await Poster(ledger).PostClaimAsync(claim, "Alpha Med", new[]
        {
            PharmaX.Domain.Pricing.VatCalculator.FromInclusive(
                Money.Of(60m), 15m)
        });

        ledger.NetOf(LedgerCode.TradeDebtors).Should().Be(0m);
    }

    /// <summary>
    /// A remittance that pays less than was claimed - the normal case.
    /// </summary>
    [Fact]
    public async Task A_short_paid_remittance_books_the_difference()
    {
        var ledger = new InMemoryLedgerRepository();

        await Poster(ledger).PostRemittanceAsync("Alpha Med",
            paid: Money.Of(55m), writtenOff: Money.Of(5m),
            receivedOn: new DateOnly(2026, 9, 13), reference: "RA-9912");

        ledger.AllBalance.Should().BeTrue();
        ledger.NetOf(LedgerCode.Bank).Should().Be(55m);
        ledger.NetOf(LedgerCode.ClaimsWrittenOff).Should().Be(5m);

        // The debtor comes down by the whole sixty: fifty-five received
        // and five given up on.
        ledger.NetOf(LedgerCode.MedicalAidDebtors).Should().Be(-60m);
    }

    [Fact]
    public async Task A_rejected_claim_is_all_write_off_and_no_bank()
    {
        var ledger = new InMemoryLedgerRepository();

        await Poster(ledger).PostRemittanceAsync("Alpha Med",
            paid: Money.Zero(), writtenOff: Money.Of(60m),
            receivedOn: new DateOnly(2026, 9, 13), reference: null);

        ledger.AllBalance.Should().BeTrue();
        ledger.NetOf(LedgerCode.Bank).Should().Be(0m);
        ledger.NetOf(LedgerCode.ClaimsWrittenOff).Should().Be(60m);
        ledger.NetOf(LedgerCode.MedicalAidDebtors).Should().Be(-60m);
    }

    [Fact]
    public async Task A_remittance_of_nothing_posts_nothing()
    {
        var ledger = new InMemoryLedgerRepository();

        await Poster(ledger).PostRemittanceAsync("Alpha Med",
            Money.Zero(), Money.Zero(), new DateOnly(2026, 9, 13), null);

        ledger.Posted.Should().BeEmpty();
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
