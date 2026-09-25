using PharmaX.Application.Abstractions;
using PharmaX.Application.Inventory.Dtos;
using PharmaX.Application.Repositories;
using PharmaX.Application.Results;
using PharmaX.Application.Security;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Inventory;

public interface IProductService
{
    Task<Result<ProductPageDto>> SearchAsync(string term, int take = 200,
                                             CancellationToken ct = default);

    /// <summary>
    /// The same search, expanded a batch at a time: what is on the shelf,
    /// which batch it came in on, how much is left and when it expires.
    /// </summary>
    Task<Result<ProductBatchPageDto>> SearchBatchesAsync(string term,
        int take = 200, CancellationToken ct = default);

    Task<Result<ProductDto>> GetAsync(Guid id, CancellationToken ct = default);

    Task<Result<Guid>> CreateAsync(ProductEditDto edit,
                                   CancellationToken ct = default);

    Task<Result> UpdateAsync(Guid id, ProductEditDto edit,
                             CancellationToken ct = default);

    Task<Result> DeactivateAsync(Guid id, CancellationToken ct = default);
}

public sealed class ProductService : IProductService
{
    private readonly IProductRepository _products;
    private readonly IBatchRepository _batches;
    private readonly IPermissionService _permissions;
    private readonly IAuditWriter _audit;
    private readonly IOutbox _outbox;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public ProductService(IProductRepository products,
        IBatchRepository batches, IPermissionService permissions,
        IAuditWriter audit, IOutbox outbox, IUnitOfWork uow, IClock clock)
    {
        _products = products;
        _batches = batches;
        _permissions = permissions;
        _audit = audit;
        _outbox = outbox;
        _uow = uow;
        _clock = clock;
    }

    public async Task<Result<ProductPageDto>> SearchAsync(string term,
        int take = 200, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ProductRead);

        // A cashier's catalogue is the over-the-counter one, filtered in
        // the query rather than on the screen.
        var scope = _permissions.ProductScope();

        var products = await _products.SearchAsync(term, take, scope, ct);
        var total = await _products.CountAsync(term, scope, ct);

        IReadOnlyList<ProductListItemDto> items =
            products.Select(ProductListItemDto.From).ToList();

        return Result<ProductPageDto>.Ok(new ProductPageDto(items, total));
    }

    public async Task<Result<ProductBatchPageDto>> SearchBatchesAsync(
        string term, int take = 200, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ProductRead);

        var scope = _permissions.ProductScope();

        var products = await _products.SearchAsync(term, take, scope, ct);
        var total = await _products.CountAsync(term, scope, ct);

        var batches = await _batches.ForProductsAsync(
            products.Select(p => p.Id).ToList(), ct);

        var byProduct = batches.ToLookup(b => b.ProductId);
        var today = _clock.Today;
        var rows = new List<ProductBatchListItemDto>();

        foreach (var product in products)
        {
            // Only what is still on the shelf. A batch counted down to
            // nothing is history, and the movement report is where
            // history is read.
            var held = byProduct[product.Id]
                .Where(b => b.QuantityOnHand > 0m)
                .OrderBy(b => b.ExpiryDate)
                .ToList();

            if (held.Count == 0)
            {
                // Not batch tracked, or tracked and nothing left. Either
                // way the product keeps its place in the list rather than
                // disappearing because a box was ticked.
                rows.Add(new ProductBatchListItemDto(
                    product.Id, product.Code, product.Description,
                    BatchNumber: null, Expiry: null, DaysToExpiry: null,
                    product.QuantityOnHand, product.IsBatchTracked,
                    product.IsActive));

                continue;
            }

            foreach (var batch in held)
            {
                rows.Add(new ProductBatchListItemDto(
                    product.Id, product.Code, product.Description,
                    batch.BatchNumber, batch.ExpiryDate,
                    batch.ExpiryDate.DayNumber - today.DayNumber,
                    batch.QuantityOnHand, product.IsBatchTracked,
                    product.IsActive));
            }
        }

        return Result<ProductBatchPageDto>.Ok(
            new ProductBatchPageDto(rows, total));
    }

    public async Task<Result<ProductDto>> GetAsync(Guid id,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ProductRead);

        var product = await _products.GetAsync(id, ct);

        // Out of scope reads exactly like absent: saying "you may not see
        // that one" confirms it exists.
        return product is null || !InScope(product)
            ? Result<ProductDto>.Fail("INV-000", "Product not found.")
            : Result<ProductDto>.Ok(ProductDto.From(product));
    }

    private bool InScope(Product product) =>
        !product.RequiresPharmacist
        || _permissions.ProductScope() == ProductScope.All;

    public async Task<Result<Guid>> CreateAsync(ProductEditDto edit,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(edit);

        // Adding a product sets its first price, which is not a price
        // change: there was no price before it. Maintaining the catalogue
        // is its own permission, so a stock controller can add what a
        // delivery brought in without being handed the shelf prices.
        _permissions.Demand(PermissionCode.ProductMaintain);

        if (edit.Schedule >= 3)
            _permissions.Demand(PermissionCode.ProductDispensary);

        var existing = await _products.FindByCodeOrBarcodeAsync(edit.Code,
            ct: ct);
        if (existing is not null)
            return Result<Guid>.Fail("INV-002",
                $"Product code {edit.Code} is already in use.");

        var product = Product.Create(
            code: edit.Code,
            description: edit.Description,
            retailPrice: Money.Of(edit.RetailPrice),
            vatRatePercent: edit.VatRatePercent,
            taxCode: edit.TaxCode,
            schedule: edit.Schedule,
            isBatchTracked: edit.IsBatchTracked);

        product.ChangeCostPrice(Money.Of(edit.CostPrice));
        product.SetReorderLevel(edit.ReorderLevel);
        product.SetGenericName(edit.GenericName);
        product.SetPackSize(edit.PackSize);
        product.SetUnitsPerPack(edit.UnitsPerPack is > 0m
            ? edit.UnitsPerPack
            : null);

        product.Classify(edit.DepartmentId, edit.CategoryId,
                         edit.ManufacturerId);

        await _products.AddAsync(product, ct);

        _audit.Applied("inv.product.create", "Product", product.Id);
        await _uow.SaveChangesAsync(ct);

        return Result<Guid>.Ok(product.Id);
    }

    public async Task<Result> UpdateAsync(Guid id, ProductEditDto edit,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(edit);

        var product = await _products.GetAsync(id, ct);
        if (product is null || !InScope(product))
            return Result.Fail("INV-000", "Product not found.");

        // Editing the catalogue at all needs the maintenance permission.
        // Nothing was demanded here before, so anyone who could read a
        // product - a cashier - could rename it or change its cost.
        _permissions.Demand(PermissionCode.ProductMaintain);

        // Only a price change needs the price permission on top.
        if (product.RetailPrice.Amount != edit.RetailPrice)
        {
            _permissions.Demand(PermissionCode.PriceChange);

            _audit.Applied("inv.price.change", "Product", product.Id,
                $"{product.RetailPrice} to {Money.Of(edit.RetailPrice)}");

            product.ChangeRetailPrice(Money.Of(edit.RetailPrice));
            _outbox.Append("ProductPrice", product.Id,
                $"{{\"productId\":\"{product.Id}\",\"retail\":{edit.RetailPrice}}}");
        }

        product.Rename(edit.Description);
        product.ChangeCostPrice(Money.Of(edit.CostPrice));
        product.SetReorderLevel(edit.ReorderLevel);
        product.SetGenericName(edit.GenericName);
        product.SetPackSize(edit.PackSize);
        product.SetUnitsPerPack(edit.UnitsPerPack is > 0m
            ? edit.UnitsPerPack
            : null);

        product.Classify(edit.DepartmentId, edit.CategoryId,
                         edit.ManufacturerId);

        _audit.Applied("inv.product.maintain", "Product", product.Id);

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result> DeactivateAsync(Guid id,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ProductMaintain);

        var product = await _products.GetAsync(id, ct);
        if (product is null) return Result.Fail("INV-000", "Product not found.");

        // Discontinued, never deleted: a deleted product takes its sales
        // history off every report that has already been printed.
        product.Deactivate();

        _audit.Applied("inv.product.deactivate", "Product", product.Id);
        await _uow.SaveChangesAsync(ct);

        return Result.Ok();
    }
}
