using PharmaX.Domain.Inventory;

namespace PharmaX.Application.Inventory.Dtos;

public sealed record ProductListItemDto(
    Guid Id, string Code, string Description, decimal RetailPrice,
    decimal QuantityOnHand, int Schedule, bool IsBatchTracked,
    bool IsActive, bool IsBelowReorderLevel)
{
    public static ProductListItemDto From(Product p)
    {
        ArgumentNullException.ThrowIfNull(p);

        return new ProductListItemDto(
            p.Id, p.Code, p.Description, p.RetailPrice.Amount,
            p.QuantityOnHand, p.Schedule, p.IsBatchTracked, p.IsActive,
            p.IsBelowReorderLevel);
    }
}

public sealed record ProductDto(
    Guid Id, string Code, string Description, string? GenericName,
    decimal RetailPrice, decimal CostPrice, decimal VatRatePercent,
    string TaxCode, int Schedule, bool IsBatchTracked, bool IsActive,
    decimal ReorderLevel, decimal QuantityOnHand, string? PackSize,
    IReadOnlyList<string> Barcodes,
    Guid? DepartmentId, Guid? CategoryId, Guid? ManufacturerId,
    decimal? UnitsPerPack = null)
{
    public static ProductDto From(Product p)
    {
        ArgumentNullException.ThrowIfNull(p);

        return new ProductDto(
            p.Id, p.Code, p.Description, p.GenericName,
            p.RetailPrice.Amount, p.CostPrice.Amount, p.VatRatePercent,
            p.TaxCode, p.Schedule, p.IsBatchTracked, p.IsActive,
            p.ReorderLevel, p.QuantityOnHand, p.PackSize, p.Barcodes,
            p.DepartmentId, p.CategoryId, p.ManufacturerId, p.UnitsPerPack);
    }
}

/// <summary>
/// One page of results plus how many there were altogether, so a list
/// screen can say "200 of 4,318" rather than implying it showed the lot.
/// </summary>
public sealed record ProductPageDto(
    IReadOnlyList<ProductListItemDto> Items, int TotalCount);

/// <summary>
/// One product and one of its batches: what it is, which batch, how much
/// of that batch is left and the day it stops being sellable.
/// </summary>
/// <remarks>
/// A product that is not batch tracked, or whose batches have all been
/// used up, still gets a row - with no batch and no expiry against it,
/// and its own balance as the quantity. Dropping it would make the list
/// change its membership when the operator ticked a box, which is the
/// one thing a view toggle must never do.
/// </remarks>
public sealed record ProductBatchListItemDto(
    Guid ProductId, string Code, string Description, string? BatchNumber,
    DateOnly? Expiry, int? DaysToExpiry, decimal QuantityOnHand,
    bool IsBatchTracked, bool IsActive)
{
    /// Already past its expiry date, and still on the shelf.
    public bool IsExpired => DaysToExpiry < 0;
}

public sealed record ProductBatchPageDto(
    IReadOnlyList<ProductBatchListItemDto> Items, int TotalCount);

/// <summary>
/// What an edit screen sends back. Deliberately not the same shape as
/// ProductDto: quantity on hand and the identifier are not editable, and a
/// DTO that carries them invites a screen to try.
/// </summary>
public sealed record ProductEditDto(
    string Code, string Description, string? GenericName,
    decimal RetailPrice, decimal CostPrice, decimal VatRatePercent,
    string TaxCode, int Schedule, bool IsBatchTracked,
    decimal ReorderLevel, string? PackSize,

    // Where the product is filed. Optional, and defaulted so that the
    // screens and tests that never set them keep compiling - but the
    // product editor sets all three, because a filing nothing can write
    // is a filter nothing can use.
    Guid? DepartmentId = null, Guid? CategoryId = null,
    Guid? ManufacturerId = null,

    // How many dispensing units the priced pack holds, so a script for
    // fewer is priced pro rata (DEF-0021). Blank for a product only ever
    // handed over as the pack.
    decimal? UnitsPerPack = null);
