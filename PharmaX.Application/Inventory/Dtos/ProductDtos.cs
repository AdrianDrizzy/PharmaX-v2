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
    decimal? UnitsPerPack = null,

    // The batches still on the shelf, earliest expiry first. Filled by
    // the product service's GetAsync for the editor; null where a
    // caller built the DTO from the product alone.
    IReadOnlyList<ProductBatchDto>? Batches = null)
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
/// One batch of a product that still holds stock, as the product editor
/// lists it.
/// </summary>
public sealed record ProductBatchDto(
    Guid BatchId, string BatchNumber, DateOnly Expiry, int DaysToExpiry,
    decimal QuantityOnHand);

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
    bool IsBatchTracked, bool IsActive, Guid? BatchId = null)
{
    /// Already past its expiry date, and still on the shelf.
    public bool IsExpired => DaysToExpiry < 0;
}

public sealed record ProductBatchPageDto(
    IReadOnlyList<ProductBatchListItemDto> Items, int TotalCount);

/// <summary>
/// What an edit screen sends back. Deliberately not the same shape as
/// ProductDto: the identifier is never editable, and the stock fields are
/// optional and null by default, so a caller that does not edit stock
/// cannot move it by leaving them out.
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
    decimal? UnitsPerPack = null,

    // The barcodes it scans by. Null leaves them as they are, for the
    // callers that do not edit them; an empty list clears them.
    IReadOnlyList<string>? Barcodes = null,

    // Whether it is still offered for sale. Null leaves it as it is.
    bool? IsActive = null,

    // The counted quantity of a product whose stock is not on a batch.
    // Null leaves it as it is. A change is posted as a stock adjustment
    // and needs inv.stocktake.post.
    decimal? QuantityOnHand = null,

    // The batches on the shelf as the editor left them, plus any it
    // added. Null leaves them as they are; a batch left out is kept.
    IReadOnlyList<ProductBatchEditDto>? Batches = null);

/// <summary>
/// One batch row of the product editor. No batch id is a batch the
/// operator added. Nullable where the operator can leave a cell blank,
/// so the service can say which one is missing.
/// </summary>
public sealed record ProductBatchEditDto(
    Guid? BatchId, string BatchNumber, DateOnly? Expiry,
    decimal? QuantityOnHand);

/// <summary>
/// One cell committed on the product list. The value is text because
/// that is what the grid hands back; the service parses it.
/// </summary>
/// <param name="Extra">
/// The expiry date (yyyy-MM-dd) when <paramref name="Column"/> is
/// <see cref="ProductCell.OpenBatch"/>, which is a batch number and a
/// date together. Null for every other column.
/// </param>
public sealed record ProductCellEditDto(
    Guid ProductId,
    Guid? BatchId,
    ProductCell Column,
    string Value,
    string? Extra = null);

/// <summary>Which column of the product list was committed.</summary>
public enum ProductCell
{
    Code,
    Description,
    RetailPrice,
    QuantityOnHand,
    Schedule,
    BatchTracked,
    Active,
    BatchNumber,
    Expiry,
    DaysLeft,

    /// A batch number and an expiry for stock that had neither.
    OpenBatch
}
