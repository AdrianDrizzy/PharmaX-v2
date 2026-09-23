using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Inventory;
using PharmaX.Application.Security;
using PharmaX.Domain.Batches;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Primitives;
using PharmaX.Persistence;

namespace PharmaX.Tests.Integration.Persistence;

/// <summary>
/// The product list's batch view: every product the search matched,
/// expanded a batch at a time, with quantities and expiry dates.
/// </summary>
/// <remarks>
/// The rule that matters here is the one about membership. Ticking a box
/// on a list screen may change how the rows are grouped; it may not
/// change which products are on it. A product that is not batch tracked,
/// or whose batches have all been used up, therefore keeps its place with
/// an empty batch column rather than disappearing - otherwise an operator
/// who ticks the box to look for an expiry date finds the product they
/// were looking at has gone.
/// </remarks>
public class ProductBatchViewTests : IClassFixture<PharmaXFixture>
{
    private const string Tracked = "PBV-TRACKED";
    private const string Untracked = "PBV-PLAIN";
    private const string Emptied = "PBV-EMPTY";
    private const string Expired = "PBV-OLD";

    private readonly PharmaXFixture _fixture;

    public ProductBatchViewTests(PharmaXFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Each_batch_with_stock_is_its_own_row()
    {
        await GivenStockAsync();

        var rows = await SearchAsync(Tracked);

        rows.Should().HaveCount(2, "two batches still hold stock");

        rows.Select(r => r.BatchNumber).Should()
            .ContainInOrder("PBV-NEAR", "PBV-FAR");

        rows[0].QuantityOnHand.Should().Be(5m);
        rows[0].Expiry.Should().NotBeNull();
        rows[0].DaysToExpiry.Should().NotBeNull();
        rows[0].IsBatchTracked.Should().BeTrue();
    }

    [Fact]
    public async Task A_product_with_no_batches_keeps_its_place_in_the_list()
    {
        await GivenStockAsync();

        var row = (await SearchAsync(Untracked)).Should().ContainSingle()
            .Subject;

        row.BatchNumber.Should().BeNull();
        row.Expiry.Should().BeNull();
        row.DaysToExpiry.Should().BeNull();
        row.QuantityOnHand.Should().Be(40m, "its own balance stands in");
    }

    /// <remarks>
    /// An emptied batch is history. It would otherwise show as a row of
    /// zeroes against a date, which reads as stock that is about to
    /// expire when there is none of it left.
    /// </remarks>
    [Fact]
    public async Task A_batch_counted_down_to_nothing_is_not_stock()
    {
        await GivenStockAsync();

        var rows = await SearchAsync(Emptied);

        rows.Should().ContainSingle("the product stays, the dead batch goes");
        rows[0].BatchNumber.Should().BeNull();
    }

    [Fact]
    public async Task An_expired_batch_is_flagged_and_still_reported()
    {
        await GivenStockAsync();

        var rows = await SearchAsync("PBV-");

        var expired = rows.Where(r => r.IsExpired).ToList();

        expired.Should().ContainSingle();
        expired[0].BatchNumber.Should().Be("PBV-GONE");
        expired[0].DaysToExpiry.Should().BeNegative();
    }

    /// <remarks>
    /// The count under the grid still reports products, so the two views
    /// of the same search agree on how much there is.
    /// </remarks>
    [Fact]
    public async Task The_total_counts_products_not_batches()
    {
        await GivenStockAsync();

        using var scope = _fixture.NewScope();
        await SignInAsync();

        var products = PharmaXFixture.Resolve<IProductService>(scope);

        var flat = await products.SearchAsync("PBV-");
        var batched = await products.SearchBatchesAsync("PBV-");

        batched.IsSuccess.Should().BeTrue(batched.Error.Message);

        batched.Value.TotalCount.Should().Be(flat.Value.TotalCount);

        batched.Value.Items.Select(r => r.ProductId).Distinct()
            .Should().HaveCount(flat.Value.Items.Count,
                "every product on the flat list is on the batch list too");
    }

    // ---- Steps -----------------------------------------------------------

    private async Task<IReadOnlyList<Application.Inventory.Dtos
        .ProductBatchListItemDto>> SearchAsync(string term)
    {
        await SignInAsync();

        using var scope = _fixture.NewScope();
        var products = PharmaXFixture.Resolve<IProductService>(scope);

        var result = await products.SearchBatchesAsync(term);

        result.IsSuccess.Should().BeTrue(result.Error.Message);
        return result.Value.Items;
    }

    private async Task SignInAsync()
    {
        using var scope = _fixture.NewScope();
        var auth = PharmaXFixture.Resolve<IAuthenticationService>(scope);

        var result = await auth.AuthenticateAsync("admin", "ChangeMe#1");
        result.IsSuccess.Should().BeTrue(result.Error.Message);
    }

    private async Task GivenStockAsync()
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);
        var clock = PharmaXFixture.Resolve<IClock>(scope);

        if (await db.Products.AnyAsync(p => p.Code == Tracked)) return;

        var tracked = Product.Create(Tracked, "PBV tracked item",
            Money.Of(30m), schedule: 0, isBatchTracked: true);

        tracked.SetQuantityOnHand(13m);

        var plain = Product.Create(Untracked, "PBV plain item", Money.Of(20m),
            schedule: 0, isBatchTracked: false);

        plain.SetQuantityOnHand(40m);

        var emptied = Product.Create(Emptied, "PBV emptied item",
            Money.Of(25m), schedule: 0, isBatchTracked: true);

        emptied.SetQuantityOnHand(0m);

        var expired = Product.Create(Expired, "PBV expired item",
            Money.Of(25m), schedule: 0, isBatchTracked: true);

        expired.SetQuantityOnHand(2m);

        db.Products.AddRange(tracked, plain, emptied, expired);

        var cost = Money.Of(12m);

        // Issued down to nothing rather than received at zero: the
        // aggregate refuses a receipt of nothing, and rightly.
        var dead = Batch.Receive(emptied.Id, "PBV-DEAD",
            clock.Today.AddYears(1), 3m, cost, clock.UtcNow);

        dead.Issue(3m);

        db.Batches.AddRange(
            Batch.Receive(tracked.Id, "PBV-NEAR", clock.Today.AddDays(30), 5m,
                cost, clock.UtcNow),
            Batch.Receive(tracked.Id, "PBV-FAR", clock.Today.AddYears(2), 8m,
                cost, clock.UtcNow),
            dead,
            Batch.Receive(expired.Id, "PBV-GONE", clock.Today.AddDays(-20),
                2m, cost, clock.UtcNow));

        await db.SaveChangesAsync();
    }
}
