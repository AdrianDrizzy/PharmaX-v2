using FluentAssertions;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Sales;

namespace PharmaX.Tests.Unit.Domain;

/// <summary>
/// The aggregate holds its own invariants: an unbalanced or unauthorised
/// sale cannot be finalised, whatever a form or a presenter believes.
/// </summary>
public class SaleTests
{
    private static readonly DateTime Now =
        new(2026, 9, 10, 9, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void A_sale_requires_an_open_till_session()
    {
        var act = () => Sale.Open(Guid.NewGuid(), Guid.NewGuid(),
            tillSessionId: Guid.Empty, Guid.NewGuid(), Now);

        act.Should().Throw<DomainException>().WithMessage("*POS-013*");
    }

    [Fact]
    public void Lines_are_numbered_from_one_in_the_order_added()
    {
        var sale = OpenSale();

        sale.AddLine(Line("Paracetamol 500mg", 24m));
        sale.AddLine(Line("Aspirin 300mg", 18m));

        sale.Lines.Select(l => l.LineNo).Should().Equal(1, 2);
    }

    [Fact]
    public void Cannot_finalise_an_empty_sale()
    {
        var sale = OpenSale();

        var act = () => sale.Finalise("R-001-000001", Now);

        act.Should().Throw<DomainException>()
           .WithMessage("*at least one line*");
    }

    [Fact]
    public void Cannot_finalise_when_the_tender_does_not_equal_the_total()
    {
        var sale = OpenSale();
        sale.AddLine(Line("Paracetamol 500mg", 24m));
        sale.AddPayment(SalePayment.Create(TenderType.Cash, Money.Of(20m)));

        var act = () => sale.Finalise("R-001-000001", Now);

        act.Should().Throw<DomainException>().WithMessage("*BR-POS-01*");
    }

    [Fact]
    public void Finalises_when_the_tender_balances()
    {
        var sale = OpenSale();
        sale.AddLine(Line("Paracetamol 500mg", 24m));
        sale.AddPayment(SalePayment.Create(TenderType.Cash, Money.Of(24m)));

        sale.Finalise("R-001-000001", Now);

        sale.Status.Should().Be(SaleStatus.Finalised);
        sale.DocumentNumber.Should().Be("R-001-000001");
    }

    [Fact]
    public void A_finalised_sale_cannot_be_changed()
    {
        var sale = FinalisedSale();

        var act = () => sale.AddLine(Line("Aspirin 300mg", 18m));

        act.Should().Throw<DomainException>().WithMessage("*BR-POS-07*");
    }

    /// <summary>
    /// Checked at finalisation, not only when the line was added
    /// (BR-POS-03): a basket can sit open across midnight.
    /// </summary>
    [Fact]
    public void An_expired_batch_is_caught_at_finalisation()
    {
        var sale = OpenSale();

        sale.AddLine(SaleLine.Create(Guid.NewGuid(), "Amoxicillin 250mg",
            1m, Money.Of(60m), 15m, "A", PriceSource.StandardRetail,
            batchId: Guid.NewGuid(),
            batchExpiry: new DateOnly(2026, 8, 31)));

        sale.AddPayment(SalePayment.Create(TenderType.Cash, Money.Of(60m)));

        var act = () => sale.Finalise("R-001-000001", Now);

        act.Should().Throw<DomainException>()
           .WithMessage("*expired 2026-08*");
    }

    [Fact]
    public void A_voided_line_contributes_nothing_to_the_total()
    {
        var sale = OpenSale();
        sale.AddLine(Line("Paracetamol 500mg", 24m));
        sale.AddLine(Line("Aspirin 300mg", 18m));

        sale.VoidLine(2, "Customer changed their mind.", Guid.NewGuid());

        sale.Total.Amount.Should().Be(24m);
    }

    [Fact]
    public void Voiding_a_line_needs_a_reason()
    {
        var sale = OpenSale();
        sale.AddLine(Line("Paracetamol 500mg", 24m));

        var act = () => sale.VoidLine(1, "  ", Guid.NewGuid());

        act.Should().Throw<DomainException>().WithMessage("*reason*");
    }

    [Fact]
    public void A_discount_above_the_operator_limit_needs_a_supervisor()
    {
        var line = Line("Paracetamol 500mg", 100m);

        var act = () => line.ApplyDiscount(Money.Of(20m),
            operatorLimitPercent: 10m, authoriserId: null);

        act.Should().Throw<DomainException>().WithMessage("*BR-POS-05*");
    }

    [Fact]
    public void A_supervisor_authorises_a_discount_above_the_limit()
    {
        var line = Line("Paracetamol 500mg", 100m);
        var supervisor = Guid.NewGuid();

        line.ApplyDiscount(Money.Of(20m), operatorLimitPercent: 10m,
                           authoriserId: supervisor);

        line.DiscountAmount.Amount.Should().Be(20m);
        line.OverrideAuthoriserId.Should().Be(supervisor);
    }

    [Fact]
    public void Change_is_never_negative_on_an_under_tendered_sale()
    {
        var sale = OpenSale();
        sale.AddLine(Line("Paracetamol 500mg", 24m));
        sale.AddPayment(SalePayment.Create(TenderType.Cash, Money.Of(20m)));

        sale.Change.Amount.Should().Be(0m);
    }

    /// <remarks>
    /// There is no payment gateway. The card machine and the medical aid
    /// platform are worked beside the till, so by the time the cashier
    /// records the tender the money has already moved - and refusing the
    /// sale for want of a copied-out code stopped the till over a
    /// transaction that had gone through.
    /// </remarks>
    [Theory]
    [InlineData(TenderType.Card)]
    [InlineData(TenderType.Eft)]
    [InlineData(TenderType.MedicalAid)]
    public void A_tender_settled_elsewhere_needs_no_reference(
        TenderType tender)
    {
        var payment = SalePayment.Create(tender, Money.Of(24m));

        payment.Tender.Should().Be(tender);
        payment.Amount.Amount.Should().Be(24m);
        payment.Reference.Should().BeNull();
    }

    [Fact]
    public void A_reference_is_kept_where_the_cashier_records_one()
    {
        SalePayment.Create(TenderType.Card, Money.Of(24m), "AUTH 004417")
            .Reference.Should().Be("AUTH 004417");
    }

    [Fact]
    public void An_unknown_tender_name_is_refused()
    {
        var act = () => SalePayment.Create("Bitcoin", Money.Of(24m));

        act.Should().Throw<DomainException>()
           .WithMessage("*Unknown tender type*");
    }

    private static Sale OpenSale() =>
        Sale.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                  Guid.NewGuid(), Now);

    private static Sale FinalisedSale()
    {
        var sale = OpenSale();
        sale.AddLine(Line("Paracetamol 500mg", 24m));
        sale.AddPayment(SalePayment.Create(TenderType.Cash, Money.Of(24m)));
        sale.Finalise("R-001-000001", Now);
        return sale;
    }

    private static SaleLine Line(string description, decimal price) =>
        SaleLine.Create(Guid.NewGuid(), description, 1m, Money.Of(price),
                        15m, "A", PriceSource.StandardRetail);
}
