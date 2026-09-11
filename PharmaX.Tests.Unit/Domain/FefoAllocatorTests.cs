using FluentAssertions;
using PharmaX.Domain.Batches;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Primitives;

namespace PharmaX.Tests.Unit.Domain;

/// <summary>
/// The rule that stops the pharmacy selling expired medicine, tested
/// without a database because it has no dependencies at all.
/// </summary>
public class FefoAllocatorTests
{
    private static readonly DateOnly Today = new(2026, 9, 10);

    [Fact]
    public void Takes_the_batch_that_expires_first()
    {
        var later = Batch("B-LATER", Today.AddMonths(12), 10m);
        var sooner = Batch("B-SOONER", Today.AddMonths(2), 10m);

        var allocations = FefoAllocator.Allocate(
            new[] { later, sooner }, required: 4m, Today);

        allocations.Should().HaveCount(1);
        allocations[0].BatchId.Should().Be(sooner.Id);
        allocations[0].Quantity.Should().Be(4m);
    }

    [Fact]
    public void Spans_batches_in_expiry_order_when_one_is_not_enough()
    {
        var sooner = Batch("B-SOONER", Today.AddMonths(2), 3m);
        var later = Batch("B-LATER", Today.AddMonths(9), 10m);

        var allocations = FefoAllocator.Allocate(
            new[] { later, sooner }, required: 5m, Today);

        allocations.Should().HaveCount(2);
        allocations[0].BatchId.Should().Be(sooner.Id);
        allocations[0].Quantity.Should().Be(3m);
        allocations[1].BatchId.Should().Be(later.Id);
        allocations[1].Quantity.Should().Be(2m);
    }

    [Fact]
    public void Skips_an_expired_batch_even_when_it_has_stock()
    {
        var expired = Batch("B-EXPIRED", Today.AddDays(-1), 100m);
        var good = Batch("B-GOOD", Today.AddMonths(6), 5m);

        var allocations = FefoAllocator.Allocate(
            new[] { expired, good }, required: 5m, Today);

        allocations.Should().OnlyContain(a => a.BatchId == good.Id);
    }

    [Fact]
    public void Skips_a_quarantined_batch()
    {
        var quarantined = Batch("B-HOLD", Today.AddMonths(1), 100m);
        quarantined.Quarantine("Awaiting a recall decision.");

        var good = Batch("B-GOOD", Today.AddMonths(6), 5m);

        var allocations = FefoAllocator.Allocate(
            new[] { quarantined, good }, required: 5m, Today);

        allocations.Should().OnlyContain(a => a.BatchId == good.Id);
    }

    [Fact]
    public void A_batch_expiring_today_is_still_sellable()
    {
        var expiringToday = Batch("B-TODAY", Today, 5m);

        var allocations = FefoAllocator.Allocate(
            new[] { expiringToday }, required: 1m, Today);

        allocations.Should().HaveCount(1);
    }

    [Fact]
    public void Refuses_rather_than_short_supplying()
    {
        var only = Batch("B-ONLY", Today.AddMonths(3), 2m);

        var act = () => FefoAllocator.Allocate(
            new[] { only }, required: 5m, Today);

        act.Should().Throw<DomainException>()
           .WithMessage("*3 short*");
    }

    /// <summary>
    /// Received order breaks a tie on expiry, so two batches with the
    /// same expiry date are consumed oldest first.
    /// </summary>
    [Fact]
    public void Breaks_an_expiry_tie_on_the_receipt_date()
    {
        var expiry = Today.AddMonths(4);

        var newer = Batch("B-NEWER", expiry, 10m,
                          receivedAtUtc: new DateTime(2026, 8, 1, 0, 0, 0,
                                                      DateTimeKind.Utc));
        var older = Batch("B-OLDER", expiry, 10m,
                          receivedAtUtc: new DateTime(2026, 6, 1, 0, 0, 0,
                                                      DateTimeKind.Utc));

        var allocations = FefoAllocator.Allocate(
            new[] { newer, older }, required: 1m, Today);

        allocations[0].BatchId.Should().Be(older.Id);
    }

    private static Batch Batch(string number, DateOnly expiry,
        decimal quantity, DateTime? receivedAtUtc = null) =>
        PharmaX.Domain.Batches.Batch.Receive(
            productId: Guid.NewGuid(),
            batchNumber: number,
            expiryDate: expiry,
            quantity: quantity,
            costPrice: Money.Of(10m),
            receivedAtUtc: receivedAtUtc
                ?? new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));
}
