using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Inventory;
using PharmaX.Application.Inventory.Dtos;
using PharmaX.Application.Sales;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Application.Security;
using PharmaX.Application.Tills;
using PharmaX.Domain.Batches;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Primitives;
using PharmaX.Persistence;

namespace PharmaX.Tests.Integration.Persistence;

/// <summary>
/// A count taken against a real database, including the case the design
/// exists for: trading carrying on while the shelf is being counted.
/// </summary>
public class StocktakeFlowTests : IClassFixture<PharmaXFixture>
{
    private readonly PharmaXFixture _fixture;

    public StocktakeFlowTests(PharmaXFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_count_short_writes_the_stock_down_and_records_why()
    {
        await SignInAsync();

        var code = await GivenAProductAsync("STK-SHORT", "Short item",
                                            onHand: 100m, cost: 4m);

        var stocktakeId = await OpenCountAsync("Aisle one", code);

        using (var scope = _fixture.NewScope())
        {
            var stocktakes = PharmaXFixture.Resolve<IStocktakeService>(scope);
            var line = await LineFor(stocktakes, stocktakeId, code);

            line.Expected.Should().Be(100m);

            var counted = await stocktakes.RecordCountAsync(stocktakeId,
                line.LineNo, 88m);

            counted.IsSuccess.Should().BeTrue(counted.Error.Message);
            counted.Value.NetVarianceQuantity.Should().Be(-12m);
            counted.Value.NetVarianceValue.Should().Be(-48m);
            counted.Value.ShrinkageValue.Should().Be(-48m);
        }

        using (var scope = _fixture.NewScope())
        {
            var stocktakes = PharmaXFixture.Resolve<IStocktakeService>(scope);

            var posted = await stocktakes.PostAsync(stocktakeId);
            posted.IsSuccess.Should().BeTrue(posted.Error.Message);
            posted.Value.Status.Should().Be("Posted");
        }

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

            var product = await db.Products.AsNoTracking()
                .SingleAsync(p => p.Code == code);

            product.QuantityOnHand.Should().Be(88m);

            // The ledger explains the change rather than the balance
            // simply being different tomorrow.
            var movement = await db.StockMovements.AsNoTracking()
                .SingleAsync(m => m.ProductId == product.Id
                                  && m.SourceDocumentType == "Stocktake");

            movement.MovementType.Should().Be(MovementType.StockTake);
            movement.Quantity.Should().Be(-12m);
            movement.BalanceAfter.Should().Be(88m);
            movement.ReasonCode.Should().Be("COUNT-SHORT");
        }
    }

    [Fact]
    public async Task A_count_over_writes_the_stock_up()
    {
        await SignInAsync();

        var code = await GivenAProductAsync("STK-OVER", "Over item",
                                            onHand: 10m, cost: 2m);

        var stocktakeId = await OpenCountAsync("Aisle two", code);

        using (var scope = _fixture.NewScope())
        {
            var stocktakes = PharmaXFixture.Resolve<IStocktakeService>(scope);
            var line = await LineFor(stocktakes, stocktakeId, code);

            await stocktakes.RecordCountAsync(stocktakeId, line.LineNo, 14m);
            await stocktakes.PostAsync(stocktakeId);
        }

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

            var product = await db.Products.AsNoTracking()
                .SingleAsync(p => p.Code == code);

            product.QuantityOnHand.Should().Be(14m);

            var movement = await db.StockMovements.AsNoTracking()
                .SingleAsync(m => m.ProductId == product.Id
                                  && m.SourceDocumentType == "Stocktake");

            movement.Quantity.Should().Be(4m);
            movement.ReasonCode.Should().Be("COUNT-OVER");
        }
    }

    /// <summary>
    /// The reason the expected quantity is snapshotted at open. Trading
    /// carries on while the shelf is counted, so a count compared
    /// against live on-hand would report every sale in between as a
    /// shortage.
    /// </summary>
    [Fact]
    public async Task Sales_during_the_count_do_not_become_variances()
    {
        await SignInAsync();

        var code = await GivenAProductAsync("STK-TRADING", "Traded item",
                                            onHand: 50m, cost: 3m);

        var stocktakeId = await OpenCountAsync("Counted while trading", code);

        // The shelf is counted at 50 - correct at the moment of counting.
        using (var scope = _fixture.NewScope())
        {
            var stocktakes = PharmaXFixture.Resolve<IStocktakeService>(scope);
            var line = await LineFor(stocktakes, stocktakeId, code);

            var counted = await stocktakes.RecordCountAsync(stocktakeId,
                line.LineNo, 50m);

            counted.Value.NetVarianceQuantity.Should().Be(0m,
                "the count agrees with what the system believed at open");
        }

        // Two are then sold before anyone gets round to posting.
        await OpenTillSessionAsync();

        using (var scope = _fixture.NewScope())
        {
            var sales = PharmaXFixture.Resolve<ISaleService>(scope);

            var opened = await sales.OpenAsync();
            await sales.AddLineAsync(opened.Value.Id, code, 2m);

            var finalised = await sales.FinaliseAsync(opened.Value.Id,
                new[] { new PaymentDto("Cash", 60.00m, null) });

            finalised.IsSuccess.Should().BeTrue(finalised.Error.Message);
        }

        using (var scope = _fixture.NewScope())
        {
            var stocktakes = PharmaXFixture.Resolve<IStocktakeService>(scope);

            var reread = await stocktakes.GetAsync(stocktakeId);

            // Still zero: the snapshot did not move, so the two sold are
            // a sale and not a shortage.
            reread.Value.NetVarianceQuantity.Should().Be(0m);

            var posted = await stocktakes.PostAsync(stocktakeId);
            posted.IsSuccess.Should().BeTrue(posted.Error.Message);
        }

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

            var product = await db.Products.AsNoTracking()
                .SingleAsync(p => p.Code == code);

            // 48, not 50: the count agreed with the snapshot, so nothing
            // was written, and the sale of two stands.
            product.QuantityOnHand.Should().Be(48m);

            (await db.StockMovements.AsNoTracking()
                .AnyAsync(m => m.ProductId == product.Id
                               && m.SourceDocumentType == "Stocktake"))
                .Should().BeFalse(
                    "a line that agrees writes no adjustment at all");
        }
    }

    /// <summary>
    /// A batch-tracked product gets a line per batch, because a single
    /// total for the product would hide a mislabelled batch entirely.
    /// </summary>
    [Fact]
    public async Task A_batch_tracked_product_is_counted_batch_by_batch()
    {
        await SignInAsync();

        Guid productId;
        Guid soonerId;

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);
            var clock = PharmaXFixture.Resolve<IClock>(scope);

            var product = Product.Create("STK-BATCH", "Batched item",
                Money.Of(20m), isBatchTracked: true);

            product.SetQuantityOnHand(30m);
            product.ChangeCostPrice(Money.Of(6m));
            db.Products.Add(product);
            productId = product.Id;

            var sooner = Batch.Receive(productId, "B-SOON",
                clock.Today.AddMonths(3), 10m, Money.Of(6m), clock.UtcNow);

            var later = Batch.Receive(productId, "B-LATE",
                clock.Today.AddMonths(15), 20m, Money.Of(6m), clock.UtcNow);

            db.Batches.AddRange(sooner, later);
            soonerId = sooner.Id;

            await db.SaveChangesAsync();
        }

        var stocktakeId = await OpenCountAsync("Batch shelf", "STK-BATCH");

        using (var scope = _fixture.NewScope())
        {
            var stocktakes = PharmaXFixture.Resolve<IStocktakeService>(scope);
            var stocktake = await stocktakes.GetAsync(stocktakeId);

            var lines = stocktake.Value.Lines
                .Where(l => l.ProductId == productId)
                .ToList();

            lines.Should().HaveCount(2, "one line per batch");
            lines.Should().OnlyContain(l => l.BatchNumber != null);

            var sooner = lines.Single(l => l.BatchNumber == "B-SOON");
            var later = lines.Single(l => l.BatchNumber == "B-LATE");

            // The near-expiry batch is two short; the other agrees.
            await stocktakes.RecordCountAsync(stocktakeId, sooner.LineNo, 8m);
            await stocktakes.RecordCountAsync(stocktakeId, later.LineNo, 20m);

            var posted = await stocktakes.PostAsync(stocktakeId);
            posted.IsSuccess.Should().BeTrue(posted.Error.Message);
        }

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

            var sooner = await db.Batches.AsNoTracking()
                .SingleAsync(b => b.Id == soonerId);

            sooner.QuantityOnHand.Should().Be(8m);

            var later = await db.Batches.AsNoTracking()
                .SingleAsync(b => b.ProductId == productId
                                  && b.BatchNumber == "B-LATE");

            later.QuantityOnHand.Should().Be(20m);

            var product = await db.Products.AsNoTracking()
                .SingleAsync(p => p.Id == productId);

            product.QuantityOnHand.Should().Be(28m,
                "the product total is the sum of what was counted");
        }
    }

    [Fact]
    public async Task Two_counts_cannot_be_open_at_once()
    {
        await SignInAsync();
        await GivenAProductAsync("STK-ONEATATIME", "Only one", 5m, 1m);

        var first = await OpenCountAsync("First count", "STK-ONEATATIME");

        using (var scope = _fixture.NewScope())
        {
            var stocktakes = PharmaXFixture.Resolve<IStocktakeService>(scope);

            var second = await stocktakes.OpenAsync(
                new StocktakeScopeDto("Second count", "STK-ONEATATIME"));

            second.IsFailure.Should().BeTrue();
            second.Error.Code.Should().Be("STK-002");
        }

        // Cancelling the first frees the shelf for another count.
        using (var scope = _fixture.NewScope())
        {
            var stocktakes = PharmaXFixture.Resolve<IStocktakeService>(scope);

            await stocktakes.CancelAsync(first, "Superseded.");

            var third = await stocktakes.OpenAsync(
                new StocktakeScopeDto("Third count", "STK-ONEATATIME"));

            third.IsSuccess.Should().BeTrue(third.Error.Message);
            await stocktakes.CancelAsync(third.Value.Id, "Tidying up.");
        }
    }

    [Fact]
    public async Task An_uncounted_line_blocks_the_post()
    {
        await SignInAsync();

        await GivenAProductAsync("STK-PAIR-A", "Pair A", 5m, 1m);
        await GivenAProductAsync("STK-PAIR-B", "Pair B", 5m, 1m);

        Guid stocktakeId;

        using (var scope = _fixture.NewScope())
        {
            var stocktakes = PharmaXFixture.Resolve<IStocktakeService>(scope);

            var opened = await stocktakes.OpenAsync(
                new StocktakeScopeDto("Pair count", "STK-PAIR"));

            opened.IsSuccess.Should().BeTrue(opened.Error.Message);
            opened.Value.Lines.Should().HaveCount(2);
            stocktakeId = opened.Value.Id;

            await stocktakes.RecordCountAsync(stocktakeId,
                opened.Value.Lines[0].LineNo, 5m);

            var act = () => stocktakes.PostAsync(stocktakeId);

            await act.Should()
                .ThrowAsync<Domain.Exceptions.DomainException>()
                .WithMessage("*have not been counted*");

            await stocktakes.CancelAsync(stocktakeId, "Test tidy-up.");
        }
    }

    [Fact]
    public async Task A_scope_that_matches_nothing_is_refused()
    {
        await SignInAsync();

        using var scope = _fixture.NewScope();
        var stocktakes = PharmaXFixture.Resolve<IStocktakeService>(scope);

        var result = await stocktakes.OpenAsync(
            new StocktakeScopeDto("Nothing", "NOSUCHPRODUCTCODEATALL"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("STK-003");
    }

    [Fact]
    public async Task Posting_is_audited_with_the_variance_and_the_reason()
    {
        await SignInAsync();

        var code = await GivenAProductAsync("STK-AUDIT", "Audited item",
                                            onHand: 200m, cost: 10m);

        var stocktakeId = await OpenCountAsync("Audited count", code);

        using (var scope = _fixture.NewScope())
        {
            var stocktakes = PharmaXFixture.Resolve<IStocktakeService>(scope);
            var line = await LineFor(stocktakes, stocktakeId, code);

            // 100 short at 10 each is 1000, well over the threshold.
            await stocktakes.RecordCountAsync(stocktakeId, line.LineNo, 100m);

            var reread = await stocktakes.GetAsync(stocktakeId);
            reread.Value.RequiresVarianceReason.Should().BeTrue();

            await stocktakes.ExplainVarianceAsync(stocktakeId,
                "Fridge failure over the long weekend; stock destroyed.");

            var posted = await stocktakes.PostAsync(stocktakeId);
            posted.IsSuccess.Should().BeTrue(posted.Error.Message);
        }

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

            var entry = await db.AuditEntries.AsNoTracking()
                .Where(a => a.EntityId == stocktakeId)
                .SingleAsync(a => a.Action == "inv.stocktake.post");

            entry.Reason.Should().Contain("Fridge failure");
            entry.Outcome.Should().Be("Applied");

            (await db.OutboxEntries.AsNoTracking()
                .AnyAsync(o => o.EntityId == stocktakeId
                               && o.EntityType == "Stocktake"))
                .Should().BeTrue();
        }
    }

    // ---- Steps -----------------------------------------------------------

    private static async Task<StocktakeLineDto> LineFor(
        IStocktakeService stocktakes, Guid stocktakeId, string code)
    {
        var stocktake = await stocktakes.GetAsync(stocktakeId);
        stocktake.IsSuccess.Should().BeTrue(stocktake.Error.Message);

        return stocktake.Value.Lines.Should().ContainSingle().Subject;
    }

    private async Task<Guid> OpenCountAsync(string description, string code)
    {
        using var scope = _fixture.NewScope();
        var stocktakes = PharmaXFixture.Resolve<IStocktakeService>(scope);

        var result = await stocktakes.OpenAsync(
            new StocktakeScopeDto(description, code));

        result.IsSuccess.Should().BeTrue(result.Error.Message);
        result.Value.StocktakeNumber.Should().MatchRegex(@"^S-001-\d{6}$");

        return result.Value.Id;
    }

    private async Task SignInAsync()
    {
        using var scope = _fixture.NewScope();
        var auth = PharmaXFixture.Resolve<IAuthenticationService>(scope);

        var result = await auth.AuthenticateAsync("admin", "ChangeMe#1");
        result.IsSuccess.Should().BeTrue(result.Error.Message);
    }

    private async Task<string> GivenAProductAsync(string code,
        string description, decimal onHand, decimal cost)
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        if (await db.Products.AnyAsync(p => p.Code == code)) return code;

        var product = Product.Create(code, description, Money.Of(30m));
        product.SetQuantityOnHand(onHand);
        product.ChangeCostPrice(Money.Of(cost));

        db.Products.Add(product);
        await db.SaveChangesAsync();

        return code;
    }

    private async Task OpenTillSessionAsync()
    {
        using var scope = _fixture.NewScope();
        var sessions = PharmaXFixture.Resolve<ITillSessionService>(scope);

        if (await sessions.CurrentAsync() is not null) return;

        var result = await sessions.OpenAsync(100m);
        result.IsSuccess.Should().BeTrue(result.Error.Message);
    }
}
