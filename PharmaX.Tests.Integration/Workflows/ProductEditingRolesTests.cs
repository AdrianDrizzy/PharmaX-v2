using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Inventory;
using PharmaX.Application.Inventory.Dtos;
using PharmaX.Application.Security;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Security;
using PharmaX.Persistence;

namespace PharmaX.Tests.Integration.Workflows;

/// <summary>
/// The pharmacist, the assistant and the administrator keep the products:
/// the selling price, the stock on hand and the rest of the product, from
/// the editor and from the list (23 October 2026). Stock moves as an
/// adjustment with a reason, never as a figure written over.
/// </summary>
public class ProductEditingRolesTests : IClassFixture<PharmaXFixture>
{
    private readonly WorkflowSteps _steps;

    public ProductEditingRolesTests(PharmaXFixture fixture) =>
        _steps = new WorkflowSteps(fixture);

    [Theory]
    [InlineData("pe.pharm", Role.Standard.Pharmacist)]
    [InlineData("pe.assist", Role.Standard.PharmacistAssistant)]
    [InlineData("pe.admin", Role.Standard.Administrator)]
    public async Task The_role_edits_price_stock_and_the_rest_of_a_product(
        string username, string role)
    {
        var code = $"PE-{username.ToUpperInvariant()}";
        var id = await _steps.ProductAsync(code, "Editable item", 5m, quantity: 10m);

        await _steps.SignInAsync(username, role);

        using (var scope = _steps.Fixture.NewScope())
        {
            var products = PharmaXFixture.Resolve<IProductService>(scope);
            var product = (await products.GetAsync(id)).Value;

            var saved = await products.UpdateAsync(id, Edit(product) with
            {
                Description = "Edited item",
                RetailPrice = 7.50m,
                CostPrice = 4m,
                ReorderLevel = 3m,
                QuantityOnHand = 8m,
                StockReason = "Two found broken on the shelf"
            });

            saved.IsSuccess.Should().BeTrue(saved.IsFailure ? saved.Error.Message : "");
        }

        using (var scope = _steps.Fixture.NewScope())
        {
            var products = PharmaXFixture.Resolve<IProductService>(scope);

            // And from the list: the price and the shelf a cell at a time.
            (await products.ApplyCellEditAsync(new ProductCellEditDto(id, null,
                ProductCell.RetailPrice, "8.25"))).IsSuccess.Should().BeTrue();

            var counted = await products.ApplyCellEditAsync(new ProductCellEditDto(
                id, null, ProductCell.QuantityOnHand, "9", "Recounted"));
            counted.IsSuccess.Should().BeTrue(counted.IsFailure ? counted.Error.Message : "");
        }

        using var check = _steps.Fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(check);
        var edited = await db.Products.AsNoTracking().SingleAsync(p => p.Id == id);

        edited.Description.Should().Be("Edited item");
        edited.RetailPrice.Amount.Should().Be(8.25m);
        edited.CostPrice.Amount.Should().Be(4m);
        edited.ReorderLevel.Should().Be(3m);
        edited.QuantityOnHand.Should().Be(9m);

        var movements = (await db.StockMovements.AsNoTracking()
                .Where(m => m.ProductId == id).ToListAsync())
            .OrderBy(m => m.OccurredAtUtc).ThenBy(m => m.BalanceAfter).ToList();

        movements.Select(m => (m.MovementType, m.Quantity, m.BalanceAfter))
            .Should().Equal((MovementType.Adjustment, -2m, 8m),
                            (MovementType.Adjustment, 1m, 9m));
        movements[0].ReasonCode.Should().StartWith("COUNT-SHORT: Two found");
        movements[1].ReasonCode.Should().Be("COUNT-OVER: Recounted");

        var reasons = await db.AuditEntries.AsNoTracking()
            .Where(a => a.EntityId == id && a.Action == "inv.stock.adjust")
            .Select(a => a.Reason).ToListAsync();

        reasons.Should().BeEquivalentTo(
            $"{code} on hand 10 to 8. Two found broken on the shelf",
            $"{code} on hand 8 to 9. Recounted");

        (await db.AuditEntries.AsNoTracking()
            .CountAsync(a => a.EntityId == id && a.Action == "inv.price.change"))
            .Should().Be(2, "both price changes are on the trail");
    }

    [Fact]
    public async Task A_stock_change_with_no_reason_is_refused_and_nothing_is_kept()
    {
        var id = await _steps.ProductAsync("PE-NOWHY", "Needs a reason", 5m, quantity: 10m);
        await _steps.SignInAsync("pe.pharm.why", Role.Standard.Pharmacist);

        using (var scope = _steps.Fixture.NewScope())
        {
            var products = PharmaXFixture.Resolve<IProductService>(scope);
            var product = (await products.GetAsync(id)).Value;

            var refused = await products.UpdateAsync(id, Edit(product) with
            {
                Description = "Should not be kept",
                QuantityOnHand = 4m,
                StockReason = null
            });

            refused.IsFailure.Should().BeTrue();
            refused.Error.Code.Should().Be("INV-033");
        }

        var (onHand, _) = await _steps.StockOfAsync(id);
        onHand.Should().Be(10m);
    }

    [Fact]
    public async Task A_cashier_may_look_a_product_up_but_not_change_it()
    {
        var id = await _steps.ProductAsync("PE-TILL", "Counter item", 5m, quantity: 10m);
        await _steps.SignInAsync("pe.cashier", Role.Standard.Cashier);

        using var scope = _steps.Fixture.NewScope();
        var products = PharmaXFixture.Resolve<IProductService>(scope);
        var product = (await products.GetAsync(id)).Value;

        var edit = () => products.UpdateAsync(id, Edit(product) with { RetailPrice = 1m });
        await edit.Should().ThrowAsync<PermissionDeniedException>();

        var count = () => products.ApplyCellEditAsync(new ProductCellEditDto(id, null,
            ProductCell.QuantityOnHand, "1", "Counted"));
        await count.Should().ThrowAsync<PermissionDeniedException>();
    }

    private static ProductEditDto Edit(ProductDto p) => new(
        Code: p.Code,
        Description: p.Description,
        GenericName: p.GenericName,
        RetailPrice: p.RetailPrice,
        CostPrice: p.CostPrice,
        VatRatePercent: p.VatRatePercent,
        TaxCode: p.TaxCode,
        Schedule: p.Schedule,
        IsBatchTracked: p.IsBatchTracked,
        ReorderLevel: p.ReorderLevel,
        PackSize: p.PackSize,
        DepartmentId: p.DepartmentId,
        CategoryId: p.CategoryId,
        ManufacturerId: p.ManufacturerId,
        UnitsPerPack: p.UnitsPerPack,
        Barcodes: p.Barcodes);
}
