using FluentAssertions;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Suppliers;

namespace PharmaX.Tests.Unit.Domain;

public class GoodsReceiptTests
{
    private static readonly DateOnly Today = new(2026, 9, 10);

    /// <summary>
    /// BR-INV-02. Checked on the aggregate as well as in the service,
    /// because the aggregate is the last line of defence: a screen that
    /// gets past the service check must still be refused here.
    /// </summary>
    [Fact]
    public void A_batch_tracked_line_needs_a_batch_and_an_expiry()
    {
        var receipt = Receipt();

        var noBatch = () => receipt.AddLine(Guid.NewGuid(), "Amoxicillin",
            5m, Money.Of(10m), isBatchTracked: true,
            batchNumber: null, expiryDate: Today.AddMonths(6));

        noBatch.Should().Throw<DomainException>()
               .WithMessage("*BR-INV-02*");

        var noExpiry = () => receipt.AddLine(Guid.NewGuid(), "Amoxicillin",
            5m, Money.Of(10m), isBatchTracked: true,
            batchNumber: "B-1", expiryDate: null);

        noExpiry.Should().Throw<DomainException>()
                .WithMessage("*BR-INV-02*");
    }

    [Fact]
    public void An_untracked_line_needs_neither()
    {
        var receipt = Receipt();

        receipt.AddLine(Guid.NewGuid(), "Plasters", 10m, Money.Of(5m),
            isBatchTracked: false);

        receipt.Lines.Should().ContainSingle();
    }

    [Fact]
    public void An_empty_receipt_cannot_be_posted()
    {
        var act = () => Receipt().Post();

        act.Should().Throw<DomainException>()
           .WithMessage("*empty receipt*");
    }

    /// <summary>
    /// Posting is one-way: a mistake is corrected by an adjustment,
    /// never by unposting and editing, because the stock has already
    /// moved and a report may already have been printed.
    /// </summary>
    [Fact]
    public void A_posted_receipt_cannot_be_changed()
    {
        var receipt = Receipt();
        receipt.AddLine(Guid.NewGuid(), "Plasters", 10m, Money.Of(5m), false);
        receipt.Post();

        var addLine = () => receipt.AddLine(Guid.NewGuid(), "More plasters",
            1m, Money.Of(5m), false);

        addLine.Should().Throw<DomainException>()
               .WithMessage("*adjustment*");

        var postAgain = () => receipt.Post();
        postAgain.Should().Throw<DomainException>();
    }

    [Fact]
    public void The_total_is_the_sum_of_the_lines()
    {
        var receipt = Receipt();

        receipt.AddLine(Guid.NewGuid(), "A", 3m, Money.Of(10m), false);
        receipt.AddLine(Guid.NewGuid(), "B", 2m, Money.Of(7.50m), false);

        receipt.Total.Amount.Should().Be(45.00m);
    }

    /// <summary>
    /// The delivery note and the invoice disagree often enough that the
    /// variance has to be a recorded fact rather than something the
    /// person capturing it reconciles away.
    /// </summary>
    [Fact]
    public void An_invoice_variance_is_kept_not_hidden()
    {
        var receipt = Receipt();
        receipt.AddLine(Guid.NewGuid(), "A", 5m, Money.Of(10m), false);
        receipt.RecordInvoiceTotal(Money.Of(55m));

        receipt.Total.Amount.Should().Be(50m);
        receipt.InvoiceVariance!.Value.Amount.Should().Be(5m);
    }

    [Fact]
    public void No_invoice_total_means_no_variance()
    {
        var receipt = Receipt();
        receipt.AddLine(Guid.NewGuid(), "A", 5m, Money.Of(10m), false);

        receipt.InvoiceVariance.Should().BeNull();
    }

    [Fact]
    public void Removing_a_line_renumbers_the_rest()
    {
        var receipt = Receipt();
        receipt.AddLine(Guid.NewGuid(), "A", 1m, Money.Of(1m), false);
        receipt.AddLine(Guid.NewGuid(), "B", 1m, Money.Of(1m), false);
        receipt.AddLine(Guid.NewGuid(), "C", 1m, Money.Of(1m), false);

        receipt.RemoveLine(2);

        // No gaps: the line numbers are what the supplier's query will
        // refer to.
        receipt.Lines.Select(l => l.LineNo).Should().Equal(1, 2);
        receipt.Lines.Select(l => l.Description).Should().Equal("A", "C");
    }

    [Fact]
    public void A_zero_quantity_line_is_refused()
    {
        var act = () => Receipt().AddLine(Guid.NewGuid(), "A", 0m,
            Money.Of(10m), false);

        act.Should().Throw<DomainException>().WithMessage("*positive*");
    }

    private static GoodsReceipt Receipt() =>
        GoodsReceipt.Create("G-001-000001", Guid.NewGuid(), Today,
                            Guid.NewGuid());
}

public class PurchaseOrderTests
{
    private static readonly DateOnly Today = new(2026, 9, 10);

    [Fact]
    public void An_empty_order_cannot_be_sent()
    {
        var act = () => Order().Send();

        act.Should().Throw<DomainException>().WithMessage("*empty order*");
    }

    /// <summary>
    /// Short deliveries are the norm rather than the exception, so the
    /// order stays open and says how much is still owed.
    /// </summary>
    [Fact]
    public void A_short_delivery_leaves_the_order_partially_received()
    {
        var order = Order();
        order.AddLine(Guid.NewGuid(), "Amoxicillin", 10m, Money.Of(8m));
        order.Send();

        order.RecordReceipt(1, 4m);

        order.Status.Should().Be(PurchaseOrderStatus.PartiallyReceived);
        order.Lines[0].Outstanding.Should().Be(6m);
        order.IsFullyReceived.Should().BeFalse();
    }

    [Fact]
    public void A_full_delivery_closes_the_order()
    {
        var order = Order();
        order.AddLine(Guid.NewGuid(), "Amoxicillin", 10m, Money.Of(8m));
        order.Send();

        order.RecordReceipt(1, 10m);

        order.Status.Should().Be(PurchaseOrderStatus.Received);
        order.IsFullyReceived.Should().BeTrue();
    }

    [Fact]
    public void Deliveries_accumulate_across_receipts()
    {
        var order = Order();
        order.AddLine(Guid.NewGuid(), "Amoxicillin", 10m, Money.Of(8m));
        order.Send();

        order.RecordReceipt(1, 4m);
        order.RecordReceipt(1, 6m);

        order.Lines[0].QuantityReceived.Should().Be(10m);
        order.Status.Should().Be(PurchaseOrderStatus.Received);
    }

    [Fact]
    public void A_sent_order_cannot_take_new_lines()
    {
        var order = Order();
        order.AddLine(Guid.NewGuid(), "A", 1m, Money.Of(1m));
        order.Send();

        var act = () => order.AddLine(Guid.NewGuid(), "B", 1m, Money.Of(1m));

        act.Should().Throw<DomainException>().WithMessage("*draft*");
    }

    [Fact]
    public void A_received_order_cannot_be_cancelled()
    {
        var order = Order();
        order.AddLine(Guid.NewGuid(), "A", 1m, Money.Of(1m));
        order.Send();
        order.RecordReceipt(1, 1m);

        var act = () => order.Cancel("Changed our minds.");

        act.Should().Throw<DomainException>()
           .WithMessage("*received order*");
    }

    [Fact]
    public void Cancelling_needs_a_reason()
    {
        var order = Order();
        order.AddLine(Guid.NewGuid(), "A", 1m, Money.Of(1m));
        order.Send();

        var act = () => order.Cancel("  ");

        act.Should().Throw<DomainException>().WithMessage("*reason*");
    }

    private static PurchaseOrder Order() =>
        PurchaseOrder.Raise("P-001-000001", Guid.NewGuid(), Today);
}
