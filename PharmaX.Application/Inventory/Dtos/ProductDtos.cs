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
    IReadOnlyList<string> Barcodes)
{
    public static ProductDto From(Product p)
    {
        ArgumentNullException.ThrowIfNull(p);

        return new ProductDto(
            p.Id, p.Code, p.Description, p.GenericName,
            p.RetailPrice.Amount, p.CostPrice.Amount, p.VatRatePercent,
            p.TaxCode, p.Schedule, p.IsBatchTracked, p.IsActive,
            p.ReorderLevel, p.QuantityOnHand, p.PackSize, p.Barcodes);
    }
}

/// <summary>
/// One page of results plus how many there were altogether, so a list
/// screen can say "200 of 4,318" rather than implying it showed the lot.
/// </summary>
public sealed record ProductPageDto(
    IReadOnlyList<ProductListItemDto> Items, int TotalCount);

/// <summary>
/// What an edit screen sends back. Deliberately not the same shape as
/// ProductDto: quantity on hand and the identifier are not editable, and a
/// DTO that carries them invites a screen to try.
/// </summary>
public sealed record ProductEditDto(
    string Code, string Description, string? GenericName,
    decimal RetailPrice, decimal CostPrice, decimal VatRatePercent,
    string TaxCode, int Schedule, bool IsBatchTracked,
    decimal ReorderLevel, string? PackSize);
