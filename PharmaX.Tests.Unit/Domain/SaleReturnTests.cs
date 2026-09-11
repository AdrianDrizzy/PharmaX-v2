using FluentAssertions;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Sales;

namespace PharmaX.Tests.Unit.Domain;

public class SaleReturnTests
{
    private static readonly DateTime Now =
        new(2026, 9, 10, 14, 0, 0, DateTimeKind.Utc);

    private static readonly DateOnly Today = new(2026, 9, 10);

    [Fact]
    public void Only_a_finalised_sale_can_be_returned_against()
    {
        var open = Sale.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                             Guid.NewGuid(), Now);

        var act = () => SaleReturn.Against(open, ReturnKind.GoodsReturn,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Wrong size.",
            Now);

        act.Should().Throw<DomainException>()
           .WithMessage("*finalised sale*");
    }

    /// <summary>
    /// A refund is money leaving the drawer, so it is never the
    /// operator's decision alone (BR-POS-09).
    /// </summary>
    [Fact]
    public void A_return_needs_a_supervisor_and_a_reason()
    {
        var sale = FinalisedSale();

        var noAuthoriser = () => SaleReturn.Against(sale,
            ReturnKind.GoodsReturn, Guid.NewGuid(), Guid.NewGuid(),
            Guid.Empty, "Wrong size.", Now);

        noAuthoriser.Should().Throw<DomainException>()
                    .WithMessage("*BR-POS-09*");

        var noReason = () => SaleReturn.Against(sale,
            ReturnKind.GoodsReturn, Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), "   ", Now);

        noReason.Should().Throw<DomainException>().WithMessage("*reason*");
    }

    [Fact]
    public void A_refund_needs_an_open_till_session()
    {
        var act = () => SaleReturn.Against(FinalisedSale(),
            ReturnKind.GoodsReturn, tillSessionId: Guid.Empty,
            Guid.NewGuid(), Guid.NewGuid(), "Wrong size.", Now);

        act.Should().Throw<DomainException>().WithMessage("*POS-013*");
    }

    /// <summary>
    /// Previous returns are separate documents, so without netting them
    /// off a line could be refunded twice over.
    /// </summary>
    [Fact]
    public void More_cannot_come_back_than_went_out()
    {
        var sale = FinalisedSale(quantity: 5m);
        var saleReturn = ReturnAgainst(sale);

        var tooMany = () => saleReturn.AddLine(sale.Lines[0], 6m,
            alreadyReturned: 0m, restockToShelf: true, Today);

        tooMany.Should().Throw<DomainException>()
               .WithMessage("*only 5 can come back*");

        var pastPrevious = () => saleReturn.AddLine(sale.Lines[0], 3m,
            alreadyReturned: 3m, restockToShelf: true, Today);

        pastPrevious.Should().Throw<DomainException>()
                    .WithMessage("*already returned*");
    }

    [Fact]
    public void A_voided_line_has_nothing_to_return()
    {
        var sale = FinalisedSale();
        var saleReturn = ReturnAgainst(sale);

        // Void it after finalising is impossible, so build a sale with a
        // line voided before it was finalised.
        var withVoid = Sale.Open(Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Now);

        withVoid.AddLine(Line("Kept", 24m, 1m));
        withVoid.AddLine(Line("Voided", 10m, 1m));
        withVoid.VoidLine(2, "Scanned twice.", Guid.NewGuid());
        withVoid.AddPayment(SalePayment.Create(TenderType.Cash,
                                               Money.Of(24m)));
        withVoid.Finalise("S-001-000009", Now);

        var against = ReturnAgainst(withVoid);

        var act = () => against.AddLine(withVoid.Lines[1], 1m, 0m, true,
                                        Today);

        act.Should().Throw<DomainException>()
           .WithMessage("*nothing to return*");
    }

    [Fact]
    public void The_same_line_cannot_appear_twice_on_one_return()
    {
        var sale = FinalisedSale(quantity: 5m);
        var saleReturn = ReturnAgainst(sale);

        saleReturn.AddLine(sale.Lines[0], 1m, 0m, true, Today);

        var act = () => saleReturn.AddLine(sale.Lines[0], 1m, 0m, true,
                                           Today);

        act.Should().Throw<DomainException>()
           .WithMessage("*already on this return*");
    }

    /// <summary>
    /// Expired stock never goes back on a shelf, whatever the operator
    /// ticked: putting it back is how it reaches a patient.
    /// </summary>
    [Fact]
    public void An_expired_batch_can_be_credited_but_never_restocked()
    {
        var sale = FinalisedSaleWithBatch(expiry: Today.AddDays(-1));
        var saleReturn = ReturnAgainst(sale);

        var act = () => saleReturn.AddLine(sale.Lines[0], 1m, 0m,
            restockToShelf: true, Today);

        act.Should().Throw<DomainException>()
           .WithMessage("*cannot go back on the shelf*");

        // Credited without restocking is allowed, and is the right
        // answer: the customer gets their money, the stock is gone.
        var line = saleReturn.AddLine(sale.Lines[0], 1m, 0m,
            restockToShelf: false, Today);

        line.RestockToShelf.Should().BeFalse();
    }

    [Fact]
    public void An_unexpired_batch_can_go_back()
    {
        var sale = FinalisedSaleWithBatch(expiry: Today.AddMonths(6));
        var saleReturn = ReturnAgainst(sale);

        var line = saleReturn.AddLine(sale.Lines[0], 1m, 0m, true, Today);

        line.RestockToShelf.Should().BeTrue();
    }

    /// <summary>
    /// A credit note credits value without goods coming back, so
    /// nothing on one can go on a shelf even if the tick was set.
    /// </summary>
    [Fact]
    public void Nothing_on_a_credit_note_is_ever_restocked()
    {
        var sale = FinalisedSale();
        var creditNote = ReturnAgainst(sale, ReturnKind.CreditNote);

        var line = creditNote.AddLine(sale.Lines[0], 1m, 0m,
            restockToShelf: true, Today);

        line.RestockToShelf.Should().BeFalse();
        creditNote.Restockable.Should().BeEmpty();
        creditNote.WriteOffValue.Amount.Should().Be(24m);
    }

    /// <summary>
    /// A customer gets back what they paid, not the shelf price today,
    /// so a discounted line credits at its discounted rate.
    /// </summary>
    [Fact]
    public void A_discounted_line_is_credited_at_what_was_paid()
    {
        var sale = Sale.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                             Guid.NewGuid(), Now);

        var line = sale.AddLine(Line("Discounted", 50m, 2m));
        line.ApplyDiscount(Money.Of(20m), operatorLimitPercent: 100m, null);

        sale.AddPayment(SalePayment.Create(TenderType.Cash, Money.Of(80m)));
        sale.Finalise("S-001-000010", Now);

        var saleReturn = ReturnAgainst(sale);
        var returned = saleReturn.AddLine(sale.Lines[0], 1m, 0m, true, Today);

        // 100 gross less 20 discount is 80 for two, so 40 each.
        returned.UnitPrice.Amount.Should().Be(40m);
        returned.LineTotal.Amount.Should().Be(40m);
    }

    /// <summary>
    /// The mirror of BR-POS-01: what is refunded has to equal what is
    /// credited, or the drawer will not balance at cash-up.
    /// </summary>
    [Fact]
    public void The_refund_must_equal_the_credit()
    {
        var sale = FinalisedSale();
        var saleReturn = ReturnAgainst(sale);

        saleReturn.AddLine(sale.Lines[0], 1m, 0m, true, Today);
        saleReturn.AddRefund(SalePayment.Create(TenderType.Cash,
                                                Money.Of(20m)));

        var act = () => saleReturn.Finalise("R-001-000001", Now);

        act.Should().Throw<DomainException>()
           .WithMessage("*does not equal the credit*");
    }

    [Fact]
    public void A_balanced_return_finalises()
    {
        var sale = FinalisedSale();
        var saleReturn = ReturnAgainst(sale);

        saleReturn.AddLine(sale.Lines[0], 1m, 0m, true, Today);
        saleReturn.AddRefund(SalePayment.Create(TenderType.Cash,
                                                Money.Of(24m)));

        saleReturn.Finalise("R-001-000001", Now);

        saleReturn.Status.Should().Be(ReturnStatus.Finalised);
        saleReturn.DocumentNumber.Should().Be("R-001-000001");
        saleReturn.Total.Amount.Should().Be(24m);
    }

    [Fact]
    public void An_empty_return_cannot_be_finalised()
    {
        var act = () => ReturnAgainst(FinalisedSale())
            .Finalise("R-001-000001", Now);

        act.Should().Throw<DomainException>()
           .WithMessage("*at least one line*");
    }

    [Fact]
    public void A_finalised_return_cannot_be_changed()
    {
        var sale = FinalisedSale(quantity: 5m);
        var saleReturn = ReturnAgainst(sale);

        saleReturn.AddLine(sale.Lines[0], 1m, 0m, true, Today);
        saleReturn.AddRefund(SalePayment.Create(TenderType.Cash,
                                                Money.Of(24m)));
        saleReturn.Finalise("R-001-000001", Now);

        var act = () => saleReturn.AddLine(sale.Lines[0], 1m, 1m, true,
                                           Today);

        act.Should().Throw<DomainException>().WithMessage("*another one*");
    }

    [Fact]
    public void A_return_carries_the_original_document_number()
    {
        var sale = FinalisedSale();
        var saleReturn = ReturnAgainst(sale);

        saleReturn.OriginalDocumentNumber.Should().Be(sale.DocumentNumber);
        saleReturn.OriginalSaleId.Should().Be(sale.Id);
    }

    private static SaleReturn ReturnAgainst(Sale sale,
        ReturnKind kind = ReturnKind.GoodsReturn) =>
        SaleReturn.Against(sale, kind, Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), "Customer changed their mind.", Now);

    private static Sale FinalisedSale(decimal quantity = 1m)
    {
        var sale = Sale.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                             Guid.NewGuid(), Now);

        sale.AddLine(Line("Paracetamol 500mg", 24m, quantity));
        sale.AddPayment(SalePayment.Create(TenderType.Cash,
            Money.Of(24m * quantity)));
        sale.Finalise("S-001-000001", Now);

        return sale;
    }

    private static Sale FinalisedSaleWithBatch(DateOnly expiry)
    {
        var sale = Sale.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                             Guid.NewGuid(), Now);

        sale.AddLine(SaleLine.Create(Guid.NewGuid(), "Amoxicillin 250mg",
            1m, Money.Of(60m), 15m, "A", PriceSource.StandardRetail,
            batchId: Guid.NewGuid(), batchExpiry: expiry));

        sale.AddPayment(SalePayment.Create(TenderType.Cash, Money.Of(60m)));

        // Finalised before the batch expired, which is how an expired
        // item comes back over the counter in the first place.
        sale.Finalise("S-001-000002",
            expiry.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
                  .AddDays(-30));

        return sale;
    }

    private static SaleLine Line(string description, decimal price,
                                 decimal quantity) =>
        SaleLine.Create(Guid.NewGuid(), description, quantity,
            Money.Of(price), 15m, "A", PriceSource.StandardRetail);
}
