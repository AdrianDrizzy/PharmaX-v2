using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Reporting;
using PharmaX.Application.Security;
using PharmaX.Domain.Batches;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Primitives;
using PharmaX.Persistence;

namespace PharmaX.Tests.Integration.Reporting;

/// <summary>
/// What is on the shelf, batch by batch, with the date each one stops
/// being sellable.
/// </summary>
/// <remarks>
/// The question this was written to answer - "everything in stock, with
/// its batch, quantity and expiry" - had no report behind it. StockOnHand
/// reports a row per product and has no expiry to show, and the "Stock
/// Expiring" preset that claimed to answer it named that same definition
/// and passed it an expiringOnly and a withinDays that it never read. So
/// the menu item existed, ran, and returned the plain stock list.
/// </remarks>
public class StockByBatchReportTests : IClassFixture<PharmaXFixture>
{
    private const string Tracked = "BATCHED1";
    private const string Untracked = "PLAIN1";

    private readonly PharmaXFixture _fixture;

    public StockByBatchReportTests(PharmaXFixture fixture) =>
        _fixture = fixture;

    [Fact]
    public async Task Every_batch_is_a_row_with_its_quantity_and_expiry()
    {
        await GivenStockAsync();

        var rows = await RunAsync("inv.stock.by-batch");

        var batched = rows.Where(r => Code(r) == Tracked).ToList();

        batched.Should().HaveCount(3,
            "three batches still hold stock, and an expired one is still "
            + "physically on the shelf until somebody takes it off");

        // Soonest first, which is the order anyone reading this wants,
        // and it puts what is already expired at the top where it
        // belongs.
        batched.Select(r => r["Batch"]).Should()
               .ContainInOrder("B-GONE", "B-NEAR", "B-FAR");

        batched[0]["Days"].Should().Be(-30, "it went off a month ago");

        batched[1]["OnHand"].Should().Be(5m);
        batched[1]["Expiry"].Should().NotBeNull();

        // Value is the batch's own cost, not the product's.
        batched[1]["Value"].Should().Be(5m * 12m);
    }

    /// <remarks>
    /// The front shop is most of the shelf by line count and none of it
    /// is batch tracked. A report that dropped it would be a list of
    /// batches wearing the name of a list of stock.
    /// </remarks>
    [Fact]
    public async Task A_product_with_no_batches_still_appears_once()
    {
        await GivenStockAsync();

        var rows = await RunAsync("inv.stock.by-batch");

        var plain = rows.Where(r => Code(r) == Untracked).ToList();

        plain.Should().ContainSingle();
        plain[0]["Batch"].Should().BeNull();
        plain[0]["Expiry"].Should().BeNull();
        plain[0]["OnHand"].Should().Be(40m);
    }

    [Fact]
    public async Task A_batch_counted_down_to_nothing_is_left_out_unless_asked_for()
    {
        await GivenStockAsync();

        var without = await RunAsync("inv.stock.by-batch");

        without.Should().NotContain(r => (string?)r["Batch"] == "B-EMPTY",
            "stock means stock");

        var with = await RunAsync("inv.stock.by-batch",
            new Dictionary<string, string?> { ["includeZero"] = "true" });

        with.Should().Contain(r => (string?)r["Batch"] == "B-EMPTY");
    }

    [Fact]
    public async Task Expiring_within_a_window_narrows_to_that_window()
    {
        await GivenStockAsync();

        var rows = await RunAsync("inv.stock.by-batch",
            new Dictionary<string, string?> { ["withinDays"] = "60" });

        rows.Should().Contain(r => (string?)r["Batch"] == "B-NEAR",
            "it expires in thirty days");

        rows.Should().NotContain(r => (string?)r["Batch"] == "B-FAR",
            "it expires in two years");

        rows.Should().NotContain(r => Code(r) == Untracked,
            "a date filter cannot keep a line that has no date");
    }

    /// <remarks>
    /// The preset that was pointing at a definition which could not
    /// answer it.
    /// </remarks>
    [Fact]
    public async Task The_stock_expiring_preset_reports_only_dated_stock()
    {
        await GivenStockAsync();

        var rows = await RunAsync("inv.stock.expiring");

        rows.Should().NotBeEmpty();
        rows.Should().OnlyContain(r => r["Expiry"] != null);
        rows.Should().NotContain(r => Code(r) == Untracked);
    }

    [Fact]
    public async Task The_totals_count_what_has_already_expired()
    {
        await GivenStockAsync();

        using var scope = _fixture.NewScope();
        await SignInAsync();

        var result = await RunResultAsync("inv.stock.by-batch",
            new Dictionary<string, string?>());

        result.Totals.Should().ContainKey("Expired");
        result.Totals["Expired"].Should().Be(1, "B-GONE expired last month");
    }

    // ---- Steps -----------------------------------------------------------

    private static string? Code(IReadOnlyDictionary<string, object?> row) =>
        row["Code"] as string;

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>>
        RunAsync(string preset,
            IReadOnlyDictionary<string, string?>? slicers = null) =>
        (await RunResultAsync(preset,
            slicers ?? new Dictionary<string, string?>())).Rows;

    private async Task<ReportResult> RunResultAsync(string preset,
        IReadOnlyDictionary<string, string?> slicers)
    {
        await SignInAsync();

        using var scope = _fixture.NewScope();
        var runner = PharmaXFixture.Resolve<IReportRunner>(scope);
        var clock = PharmaXFixture.Resolve<IClock>(scope);

        return await runner.RunAsync(new ReportRequest(
            PresetKey: preset,
            BranchCode: "1",
            From: clock.Today.AddMonths(-1),
            To: clock.Today,
            Slicers: slicers));
    }

    private async Task SignInAsync()
    {
        using var scope = _fixture.NewScope();
        var auth = PharmaXFixture.Resolve<IAuthenticationService>(scope);

        var result = await auth.AuthenticateAsync("admin", "ChangeMe#1");
        result.IsSuccess.Should().BeTrue(result.Error.Message);
    }

    /// <summary>
    /// One tracked product with a near batch, a far batch, an emptied
    /// batch and an expired one, and one untracked product beside it.
    /// </summary>
    private async Task GivenStockAsync()
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);
        var clock = PharmaXFixture.Resolve<IClock>(scope);

        if (await db.Products.AnyAsync(p => p.Code == Tracked)) return;

        var tracked = Product.Create(Tracked, "Batched item", Money.Of(30m),
            schedule: 0, isBatchTracked: true);

        tracked.SetQuantityOnHand(13m);

        var plain = Product.Create(Untracked, "Plain item", Money.Of(20m),
            schedule: 0, isBatchTracked: false);

        plain.SetQuantityOnHand(40m);

        db.Products.AddRange(tracked, plain);

        var cost = Money.Of(12m);

        // Emptied by being issued rather than received at zero: a
        // receipt of nothing is refused by the aggregate, and rightly.
        var emptied = Batch.Receive(tracked.Id, "B-EMPTY",
            clock.Today.AddYears(1), 3m, cost, clock.UtcNow);

        emptied.Issue(3m);

        db.Batches.AddRange(
            Batch.Receive(tracked.Id, "B-NEAR", clock.Today.AddDays(30), 5m,
                cost, clock.UtcNow),
            Batch.Receive(tracked.Id, "B-FAR", clock.Today.AddYears(2), 8m,
                cost, clock.UtcNow),
            emptied,
            Batch.Receive(tracked.Id, "B-GONE", clock.Today.AddDays(-30), 2m,
                cost, clock.UtcNow));

        await db.SaveChangesAsync();
    }
}
