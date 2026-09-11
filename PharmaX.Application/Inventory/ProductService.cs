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
    private readonly IPermissionService _permissions;
    private readonly IAuditWriter _audit;
    private readonly IOutbox _outbox;
    private readonly IUnitOfWork _uow;

    public ProductService(IProductRepository products,
        IPermissionService permissions, IAuditWriter audit, IOutbox outbox,
        IUnitOfWork uow)
    {
        _products = products;
        _permissions = permissions;
        _audit = audit;
        _outbox = outbox;
        _uow = uow;
    }

    public async Task<Result<ProductPageDto>> SearchAsync(string term,
        int take = 200, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ProductRead);

        var products = await _products.SearchAsync(term, take, ct);
        var total = await _products.CountAsync(term, ct);

        IReadOnlyList<ProductListItemDto> items =
            products.Select(ProductListItemDto.From).ToList();

        return Result<ProductPageDto>.Ok(new ProductPageDto(items, total));
    }

    public async Task<Result<ProductDto>> GetAsync(Guid id,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ProductRead);

        var product = await _products.GetAsync(id, ct);

        return product is null
            ? Result<ProductDto>.Fail("INV-000", "Product not found.")
            : Result<ProductDto>.Ok(ProductDto.From(product));
    }

    public async Task<Result<Guid>> CreateAsync(ProductEditDto edit,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(edit);

        _permissions.Demand(PermissionCode.PriceChange);

        var existing = await _products.FindByCodeOrBarcodeAsync(edit.Code, ct);
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
        if (product is null) return Result.Fail("INV-000", "Product not found.");

        // Only a price change needs the price permission; renaming does
        // not, and demanding it for everything would put the whole screen
        // behind a permission most staff do not need.
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

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result> DeactivateAsync(Guid id,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.PriceChange);

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
