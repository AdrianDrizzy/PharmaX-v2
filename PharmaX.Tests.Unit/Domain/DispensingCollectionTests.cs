using FluentAssertions;
using PharmaX.Domain.Dispensing;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Sales;

namespace PharmaX.Tests.Unit.Domain;

/// <summary>
/// The hand-off from the dispensary to the till, as rules rather than as
/// screens: what belongs in a cashier's queue, what may leave it, and
/// what must not happen to a script once the patient has paid.
/// </summary>
public class DispensingCollectionTests
{
    private static readonly DateTime Noon =
        new(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void An_issued_script_with_something_to_pay_waits_at_the_till()
    {
        var dispensing = Issued(tariff: 90m, scheme: 60m);

        dispensing.CoPaymentTotal.Amount.Should().Be(30m);
        dispensing.AwaitsCollection.Should().BeTrue();
    }

    /// <remarks>
    /// A queue that fills up with scripts nobody has to pay for is a
    /// queue the cashiers learn to ignore, and then they miss the ones
    /// that matter.
    /// </remarks>
    [Fact]
    public void A_fully_covered_script_waits_for_nothing()
    {
        var dispensing = Issued(tariff: 90m, scheme: 90m);

        dispensing.CoPaymentTotal.IsZero.Should().BeTrue();
        dispensing.AwaitsCollection.Should().BeFalse();
    }

    [Fact]
    public void A_script_still_being_dispensed_is_not_in_the_queue()
    {
        var dispensing = InProgress(tariff: 90m, scheme: 60m);

        dispensing.AwaitsCollection.Should().BeFalse();
    }

    [Fact]
    public void Paying_takes_the_script_out_of_the_queue()
    {
        var dispensing = Issued(tariff: 90m, scheme: 60m);
        var saleId = Guid.NewGuid();

        dispensing.RecordCollection(saleId, Noon);

        dispensing.SaleId.Should().Be(saleId);
        dispensing.CollectedAtUtc.Should().Be(Noon);
        dispensing.AwaitsCollection.Should().BeFalse();
    }

    /// <summary>
    /// Two cashiers, one queue, one patient: the second till to reach
    /// the same script is refused rather than charging them twice.
    /// </summary>
    [Fact]
    public void A_script_cannot_be_paid_for_twice()
    {
        var dispensing = Issued(tariff: 90m, scheme: 60m);
        dispensing.RecordCollection(Guid.NewGuid(), Noon);

        var act = () => dispensing.RecordCollection(Guid.NewGuid(), Noon);

        act.Should().Throw<DomainException>()
           .WithMessage("*already been paid for*");
    }

    [Fact]
    public void Only_an_issued_script_can_be_collected()
    {
        var dispensing = InProgress(tariff: 90m, scheme: 60m);

        var act = () => dispensing.RecordCollection(Guid.NewGuid(), Noon);

        act.Should().Throw<DomainException>()
           .WithMessage("*issued dispensing*");
    }

    /// <summary>
    /// Reversing a paid script would put the stock back and leave the
    /// patient paid up for medicine they no longer have.
    /// </summary>
    [Fact]
    public void A_paid_script_cannot_be_reversed_without_refunding_it()
    {
        var dispensing = Issued(tariff: 90m, scheme: 60m);
        dispensing.RecordCollection(Guid.NewGuid(), Noon);

        var act = () => dispensing.Reverse("Wrong strength");

        act.Should().Throw<DomainException>()
           .WithMessage("*through returns*");
    }

    [Fact]
    public void An_unpaid_script_can_still_be_reversed()
    {
        var dispensing = Issued(tariff: 90m, scheme: 60m);

        dispensing.Reverse("Wrong strength");

        dispensing.Status.Should().Be(DispensingStatus.Reversed);
    }

    // ---- The sale's half of the same rule --------------------------------

    [Fact]
    public void A_sale_tied_to_a_dispensing_knows_it_is_a_collection()
    {
        var sale = OpenSale();
        var dispensingId = Guid.NewGuid();

        sale.CollectFor(dispensingId);

        sale.IsCollection.Should().BeTrue();
        sale.DispensingId.Should().Be(dispensingId);
    }

    [Fact]
    public void An_ordinary_sale_is_not_a_collection()
    {
        OpenSale().IsCollection.Should().BeFalse();
    }

    /// <summary>
    /// The script's lines go on first, so they are the ones marked as the
    /// script's; over-the-counter items follow them.
    /// </summary>
    [Fact]
    public void A_basket_with_items_in_it_cannot_become_a_collection()
    {
        var sale = OpenSale();

        sale.AddLine(SaleLine.Create(Guid.NewGuid(), "Shampoo", 1m,
            Money.Of(40m), 15m, "A", PriceSource.StandardRetail));

        var act = () => sale.CollectFor(Guid.NewGuid());

        act.Should().Throw<DomainException>()
           .WithMessage("*empty sale*");
    }

    // ---- Builders --------------------------------------------------------

    private static Dispensing InProgress(decimal tariff, decimal scheme)
    {
        var dispensing = Dispensing.Begin(Guid.NewGuid(), Guid.NewGuid(),
                                          Guid.NewGuid(), repeatNumber: 0);

        var line = dispensing.AddLine(DispensingLine.Create(
            productId: Guid.NewGuid(),
            description: "Amoxicillin 500mg",
            quantity: 21m,
            directions: "One three times a day",
            tariffAmount: Money.Of(tariff)));

        line.SetSchemeContribution(Money.Of(scheme));

        return dispensing;
    }

    private static Dispensing Issued(decimal tariff, decimal scheme)
    {
        var dispensing = InProgress(tariff, scheme);
        dispensing.Issue(Guid.NewGuid(), "DSP0001", Noon);
        return dispensing;
    }

    private static Sale OpenSale() =>
        Sale.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                  Guid.NewGuid(), Noon);
}
