using FluentAssertions;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Primitives;

namespace PharmaX.Tests.Unit.Domain;

public class StocktakeTests
{
    private static readonly DateTime Now =
        new(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc);

    private const decimal Threshold = 500m;

    [Fact]
    public void A_stocktake_needs_a_scope()
    {
        var act = () => Stocktake.Open("S-001-000001", Guid.NewGuid(),
            Guid.NewGuid(), "   ", Now);

        act.Should().Throw<DomainException>().WithMessage("*scope*");
    }

    /// <summary>
    /// A zero and an uncounted line are different answers: zero means
    /// the shelf was empty, uncounted means nobody looked. Conflating
    /// them is how a stocktake writes off stock that was never checked.
    /// </summary>
    [Fact]
    public void An_uncounted_line_is_not_a_zero()
    {
        var stocktake = WithLines(("Panado", 10m, 5m));

        stocktake.Lines[0].IsCounted.Should().BeFalse();
        stocktake.Lines[0].Variance.Should().Be(0m,
            "an uncounted line has no variance, not a variance of minus ten");

        stocktake.RecordCount(1, 0m);

        stocktake.Lines[0].IsCounted.Should().BeTrue();
        stocktake.Lines[0].Variance.Should().Be(-10m);
    }

    [Fact]
    public void Clearing_a_count_returns_the_line_to_uncounted()
    {
        var stocktake = WithLines(("Panado", 10m, 5m));

        stocktake.RecordCount(1, 8m);
        stocktake.ClearCount(1);

        stocktake.Lines[0].IsCounted.Should().BeFalse();
        stocktake.UncountedLines.Should().Be(1);
    }

    [Fact]
    public void Posting_with_an_uncounted_line_is_refused()
    {
        var stocktake = WithLines(("Panado", 10m, 5m),
                                  ("Disprin", 20m, 3m));

        stocktake.RecordCount(1, 10m);

        var act = () => stocktake.Post(Guid.NewGuid(), Now, Threshold);

        act.Should().Throw<DomainException>()
           .WithMessage("*1 line(s) have not been counted*");
    }

    [Fact]
    public void An_empty_stocktake_counts_nothing()
    {
        var act = () => Stocktake
            .Open("S-001-000001", Guid.NewGuid(), Guid.NewGuid(),
                  "Full count", Now)
            .Post(Guid.NewGuid(), Now, Threshold);

        act.Should().Throw<DomainException>().WithMessage("*counts nothing*");
    }

    [Fact]
    public void Variance_is_signed_so_over_and_short_are_distinguishable()
    {
        var stocktake = WithLines(("Short item", 10m, 20m),
                                  ("Over item", 10m, 20m));

        stocktake.RecordCount(1, 7m);    // three short
        stocktake.RecordCount(2, 12m);   // two over

        stocktake.Lines[0].Variance.Should().Be(-3m);
        stocktake.Lines[1].Variance.Should().Be(2m);

        stocktake.NetVarianceQuantity.Should().Be(-1m);
        stocktake.NetVarianceValue.Amount.Should().Be(-20m);
    }

    /// <summary>
    /// A count that is over on one item and short on another nets to
    /// something that hides the shortage the accountant cares about, so
    /// shrinkage is reported on its own.
    /// </summary>
    [Fact]
    public void Shrinkage_is_only_the_loss_never_a_gain()
    {
        var over = WithLines(("Over item", 10m, 20m));
        over.RecordCount(1, 15m);

        over.NetVarianceValue.Amount.Should().Be(100m);
        over.ShrinkageValue.Amount.Should().Be(0m,
            "finding extra stock is not shrinkage");

        var short_ = WithLines(("Short item", 10m, 20m));
        short_.RecordCount(1, 5m);

        short_.ShrinkageValue.Amount.Should().Be(-100m);
    }

    [Fact]
    public void Only_varying_lines_appear_in_the_variance_list()
    {
        var stocktake = WithLines(("Agrees", 10m, 5m), ("Differs", 10m, 5m),
                                  ("Uncounted", 10m, 5m));

        stocktake.RecordCount(1, 10m);
        stocktake.RecordCount(2, 9m);

        stocktake.Variances.Should().ContainSingle()
                 .Which.Description.Should().Be("Differs");
    }

    /// <summary>
    /// A difference nobody explained is a difference nobody
    /// investigated, so above the threshold the reason is mandatory.
    /// </summary>
    [Fact]
    public void A_large_variance_needs_a_reason_before_posting()
    {
        var stocktake = WithLines(("Expensive", 100m, 50m));
        stocktake.RecordCount(1, 80m);   // 20 short at 50 each

        stocktake.RequiresVarianceReason(Threshold).Should().BeTrue();

        var act = () => stocktake.Post(Guid.NewGuid(), Now, Threshold);
        act.Should().Throw<DomainException>().WithMessage("*written reason*");

        stocktake.ExplainVariance("Breakage during the fridge failure.");
        stocktake.Post(Guid.NewGuid(), Now, Threshold);

        stocktake.Status.Should().Be(StocktakeStatus.Posted);
    }

    [Fact]
    public void A_small_variance_posts_without_one()
    {
        var stocktake = WithLines(("Cheap", 10m, 1m));
        stocktake.RecordCount(1, 9m);

        stocktake.RequiresVarianceReason(Threshold).Should().BeFalse();
        stocktake.Post(Guid.NewGuid(), Now, Threshold);

        stocktake.Status.Should().Be(StocktakeStatus.Posted);
        stocktake.PostedAtUtc.Should().Be(Now);
    }

    /// <summary>
    /// Posting is one-way: the figures have already changed and a
    /// variance report may already have been printed.
    /// </summary>
    [Fact]
    public void A_posted_stocktake_cannot_be_changed()
    {
        var stocktake = WithLines(("Panado", 10m, 5m));
        stocktake.RecordCount(1, 10m);
        stocktake.Post(Guid.NewGuid(), Now, Threshold);

        var recount = () => stocktake.RecordCount(1, 9m);
        recount.Should().Throw<DomainException>()
               .WithMessage("*posted*new count*");

        var repost = () => stocktake.Post(Guid.NewGuid(), Now, Threshold);
        repost.Should().Throw<DomainException>();
    }

    [Fact]
    public void A_cancelled_stocktake_cannot_be_counted()
    {
        var stocktake = WithLines(("Panado", 10m, 5m));
        stocktake.Cancel("Shop got too busy.");

        var act = () => stocktake.RecordCount(1, 10m);

        act.Should().Throw<DomainException>().WithMessage("*cancelled*");
    }

    [Fact]
    public void Cancelling_needs_a_reason()
    {
        var act = () => WithLines(("Panado", 10m, 5m)).Cancel("  ");

        act.Should().Throw<DomainException>().WithMessage("*reason*");
    }

    [Fact]
    public void A_negative_count_is_refused()
    {
        var act = () => WithLines(("Panado", 10m, 5m)).RecordCount(1, -1m);

        act.Should().Throw<DomainException>()
           .WithMessage("*cannot be negative*");
    }

    [Fact]
    public void A_count_is_rounded_to_three_places_like_every_quantity()
    {
        var stocktake = WithLines(("Syrup", 10m, 5m));
        stocktake.RecordCount(1, 9.5005m);

        stocktake.Lines[0].CountedQuantity.Should().Be(9.501m);
    }

    private static Stocktake WithLines(
        params (string Description, decimal Expected, decimal Cost)[] lines)
    {
        var stocktake = Stocktake.Open("S-001-000001", Guid.NewGuid(),
            Guid.NewGuid(), "Full count", Now);

        foreach (var (description, expected, cost) in lines)
            stocktake.AddLine(StocktakeLine.For(Guid.NewGuid(), description,
                expected, Money.Of(cost)));

        return stocktake;
    }
}
