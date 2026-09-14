using FluentAssertions;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Alerts;
using PharmaX.Application.Security;
using PharmaX.Domain.Batches;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Security;
using PharmaX.Tests.Unit.Fakes;

namespace PharmaX.Tests.Unit.Application;

/// <summary>
/// The standing list of stock problems: what earns a place on it, how
/// urgent each one is, and - most of all - what must stay off it.
/// </summary>
public class StockAlertServiceTests
{
    private static readonly DateTime Noon =
        new(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateOnly Today = new(2026, 9, 13);

    [Fact]
    public async Task A_batch_past_its_date_is_critical()
    {
        var builder = new AlertBuilder();
        var product = builder.AddProduct("AMX", "Amoxicillin 500mg");
        builder.AddBatch(product, "B1", Today.AddDays(-3), 40m);

        var alerts = (await builder.Build().CurrentAsync()).Value;

        var alert = alerts.Should().ContainSingle().Subject;
        alert.Kind.Should().Be(AlertKind.Expired);
        alert.Severity.Should().Be(AlertSeverity.Critical);
        alert.Summary.Should().Contain("expired 3 day(s) ago");
    }

    /// <remarks>
    /// Inside the critical window there is still time to act, which is
    /// exactly why it has to be loud now rather than in a month.
    /// </remarks>
    [Fact]
    public async Task A_batch_inside_the_critical_window_is_critical()
    {
        var builder = new AlertBuilder();
        var product = builder.AddProduct("AMX", "Amoxicillin 500mg");
        builder.AddBatch(product, "B1", Today.AddDays(20), 40m);

        var alert = (await builder.Build().CurrentAsync()).Value.Single();

        alert.Kind.Should().Be(AlertKind.ExpiringSoon);
        alert.Severity.Should().Be(AlertSeverity.Critical);
        alert.DaysLeft.Should().Be(20);
    }

    [Fact]
    public async Task A_batch_inside_the_warning_window_is_a_warning()
    {
        var builder = new AlertBuilder();
        var product = builder.AddProduct("AMX", "Amoxicillin 500mg");
        builder.AddBatch(product, "B1", Today.AddDays(60), 40m);

        var alert = (await builder.Build().CurrentAsync()).Value.Single();

        alert.Severity.Should().Be(AlertSeverity.Warning);
    }

    [Fact]
    public async Task A_batch_beyond_the_warning_window_is_not_mentioned()
    {
        var builder = new AlertBuilder();
        var product = builder.AddProduct("AMX", "Amoxicillin 500mg");
        builder.AddBatch(product, "B1", Today.AddDays(200), 40m);

        (await builder.Build().CurrentAsync()).Value.Should().BeEmpty();
    }

    /// <summary>
    /// The trap this whole feature turns on.
    /// </summary>
    /// <remarks>
    /// ReorderLevel defaults to zero and Product.IsBelowReorderLevel is
    /// "quantity at or below level", so on the default every product
    /// with nothing on the shelf is technically below its reorder level.
    /// The pharmacy's price list loads seven hundred products and no
    /// stock, which would have opened the alert centre on seven hundred
    /// rows - and an alert centre nobody can face reading is one nobody
    /// reads on the day it matters.
    /// </remarks>
    [Fact]
    public async Task A_product_with_no_reorder_level_is_never_low()
    {
        var builder = new AlertBuilder();

        var product = builder.AddProduct("SHAMP", "Shampoo");
        product.SetQuantityOnHand(0m);

        (await builder.Build().CurrentAsync()).Value.Should().BeEmpty();
    }

    [Fact]
    public async Task A_product_at_its_reorder_level_is_a_warning()
    {
        var builder = new AlertBuilder();

        var product = builder.AddProduct("SHAMP", "Shampoo");
        product.SetReorderLevel(10m);
        product.SetQuantityOnHand(10m);

        var alert = (await builder.Build().CurrentAsync()).Value.Single();

        alert.Kind.Should().Be(AlertKind.LowStock);
        alert.Severity.Should().Be(AlertSeverity.Warning);
        alert.Summary.Should().Be("10 left, reorder at 10.");
    }

    [Fact]
    public async Task A_product_that_has_run_out_is_critical()
    {
        var builder = new AlertBuilder();

        var product = builder.AddProduct("SHAMP", "Shampoo");
        product.SetReorderLevel(10m);
        product.SetQuantityOnHand(0m);

        var alert = (await builder.Build().CurrentAsync()).Value.Single();

        alert.Kind.Should().Be(AlertKind.OutOfStock);
        alert.Severity.Should().Be(AlertSeverity.Critical);
    }

    [Fact]
    public async Task A_discontinued_product_is_not_worth_reordering()
    {
        var builder = new AlertBuilder();

        var product = builder.AddProduct("SHAMP", "Shampoo");
        product.SetReorderLevel(10m);
        product.SetQuantityOnHand(0m);
        product.Deactivate();

        (await builder.Build().CurrentAsync()).Value.Should().BeEmpty();
    }

    /// <remarks>
    /// The list is worked through from the top, so the order is part of
    /// what it is for.
    /// </remarks>
    [Fact]
    public async Task The_worst_comes_first()
    {
        var builder = new AlertBuilder();

        var low = builder.AddProduct("SHAMP", "Shampoo");
        low.SetReorderLevel(10m);
        low.SetQuantityOnHand(8m);

        var soon = builder.AddProduct("AMX", "Amoxicillin 500mg");
        builder.AddBatch(soon, "B1", Today.AddDays(60), 40m);

        var expired = builder.AddProduct("PAR", "Paracetamol 500mg");
        builder.AddBatch(expired, "B2", Today.AddDays(-1), 12m);

        var alerts = (await builder.Build().CurrentAsync()).Value;

        alerts.Should().HaveCount(3);
        alerts[0].Kind.Should().Be(AlertKind.Expired);
        alerts.Last().Severity.Should().Be(AlertSeverity.Warning);
    }

    [Fact]
    public async Task Turning_the_alert_centre_off_empties_it()
    {
        var builder = new AlertBuilder { Enabled = false };
        var product = builder.AddProduct("AMX", "Amoxicillin 500mg");
        builder.AddBatch(product, "B1", Today.AddDays(-3), 40m);

        (await builder.Build().CurrentAsync()).Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Reading_the_alerts_needs_permission_to_read_products()
    {
        var builder = new AlertBuilder
        {
            Permissions = new FakePermissionRepository(
                granted: Array.Empty<string>())
        };

        var act = () => builder.Build().CurrentAsync();

        await act.Should().ThrowAsync<PermissionDeniedException>();
    }
}

internal sealed class AlertBuilder
{
    private static readonly DateTime Noon =
        new(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);

    public InMemoryProductRepository Products { get; } = new();
    public InMemoryBatchRepository Batches { get; } = new();
    public FakeAuditWriter Audit { get; } = new();
    public FakeCurrentUser User { get; } = new();
    public FakeClock Clock { get; } = new(Noon);
    public bool Enabled { get; set; } = true;

    public FakePermissionRepository Permissions { get; set; } =
        new(granted: new[] { PermissionCode.ProductRead });

    public Product AddProduct(string code, string description)
    {
        var product = Product.Create(code, description, Money.Of(20m));

        Products.Add(product);
        return product;
    }

    public Batch AddBatch(Product product, string number, DateOnly expiry,
                          decimal quantity) =>
        Batches.Add(Batch.Receive(product.Id, number, expiry, quantity,
                                  Money.Of(10m), Noon));

    public StockAlertService Build() =>
        new(Products, Batches,
            new PermissionService(User, Permissions, Audit),
            new PharmaXOptions
            {
                Alerts = new AlertOptions
                {
                    Enabled = Enabled,
                    ExpiryWarningDays = 90,
                    ExpiryCriticalDays = 30
                }
            },
            Clock);
}
