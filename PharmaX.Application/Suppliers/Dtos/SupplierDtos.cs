using PharmaX.Domain.Suppliers;

namespace PharmaX.Application.Suppliers.Dtos;

public sealed record SupplierListItemDto(
    Guid Id, string Code, string Name, string? ContactName,
    string? Telephone, int TermsDays, int LeadTimeDays, bool IsActive)
{
    public static SupplierListItemDto From(Supplier s)
    {
        ArgumentNullException.ThrowIfNull(s);

        return new SupplierListItemDto(s.Id, s.Code, s.Name, s.ContactName,
            s.Telephone, s.TermsDays, s.LeadTimeDays, s.IsActive);
    }
}

public sealed record SupplierDto(
    Guid Id, string Code, string Name, string? ContactName,
    string? Telephone, string? Email, string? AccountNumber,
    int TermsDays, int LeadTimeDays, bool IsActive)
{
    public static SupplierDto From(Supplier s)
    {
        ArgumentNullException.ThrowIfNull(s);

        return new SupplierDto(s.Id, s.Code, s.Name, s.ContactName,
            s.Telephone, s.Email, s.AccountNumber, s.TermsDays,
            s.LeadTimeDays, s.IsActive);
    }
}

public sealed record SupplierEditDto(
    string Code, string Name, string? ContactName, string? Telephone,
    string? Email, string? AccountNumber, int TermsDays, int LeadTimeDays);

/// <summary>
/// One line as the receiving screen captured it. The product is a code
/// because the operator is reading a delivery note, not a database.
/// </summary>
/// <param name="ProductId">
/// The product itself, where the line came from a purchase order: the
/// order already knows exactly which product it is, and that identity -
/// not the description, and not a code the operator might retype - is
/// what the receipt posts against.
/// </param>
public sealed record GoodsReceiptLineEditDto(
    string ProductCode, decimal Quantity, decimal UnitCost,
    string? BatchNumber, DateOnly? ExpiryDate,
    int? PurchaseOrderLineNo = null, Guid? ProductId = null);

public sealed record GoodsReceiptEditDto(
    Guid SupplierId, Guid? PurchaseOrderId, string? SupplierInvoiceNumber,
    DateOnly ReceivedOn, decimal? InvoiceTotal,
    IReadOnlyList<GoodsReceiptLineEditDto> Lines);

/// <summary>
/// The product a captured line names, with the cost price it is bought at.
/// </summary>
public sealed record GoodsReceiptProductDto(
    Guid ProductId, string Code, string Description, decimal CostPrice,
    bool IsBatchTracked);

/// <param name="SellingPrice">
/// What the item sells at once the receipt posts; previews only.
/// </param>
public sealed record GoodsReceiptLineDto(
    int LineNo, Guid ProductId, string Description, decimal Quantity,
    decimal UnitCost, decimal LineTotal, string? BatchNumber,
    DateOnly? ExpiryDate, string ProductCode = "", decimal? SellingPrice = null)
{
    public static GoodsReceiptLineDto From(GoodsReceiptLine l,
                                           string productCode = "")
    {
        ArgumentNullException.ThrowIfNull(l);

        return new GoodsReceiptLineDto(l.LineNo, l.ProductId, l.Description,
            l.Quantity, l.UnitCost.Amount, l.LineTotal.Amount,
            l.BatchNumber, l.ExpiryDate, productCode);
    }
}

public sealed record GoodsReceiptDto(
    Guid Id, string ReceiptNumber, Guid SupplierId, string SupplierName,
    string? SupplierInvoiceNumber, DateOnly ReceivedOn, bool IsPosted,
    decimal Total, decimal? InvoiceTotal, decimal? InvoiceVariance,
    IReadOnlyList<GoodsReceiptLineDto> Lines)
{
    public static GoodsReceiptDto From(GoodsReceipt r, string supplierName)
    {
        ArgumentNullException.ThrowIfNull(r);

        return new GoodsReceiptDto(r.Id, r.ReceiptNumber, r.SupplierId,
            supplierName, r.SupplierInvoiceNumber, r.ReceivedOn, r.IsPosted,
            r.Total.Amount, r.InvoiceTotal?.Amount,
            r.InvoiceVariance?.Amount,
            r.Lines.Select(l => GoodsReceiptLineDto.From(l)).ToList());
    }
}

public sealed record GoodsReceiptListItemDto(
    Guid Id, string ReceiptNumber, string SupplierName,
    string? SupplierInvoiceNumber, DateOnly ReceivedOn, int Lines,
    decimal Total, bool IsPosted);

/// <summary>
/// How a product is bought: how many are already on order, who it is
/// usually ordered from, and who it last came from at what cost.
/// </summary>
public sealed record ProductBuyingDto(
    Guid ProductId, decimal OnOrder, Guid? DefaultSupplierId,
    string? DefaultSupplierName, Guid? LastSupplierId,
    string? LastSupplierName, decimal? LastCost, DateOnly? LastReceivedOn);

public sealed record PurchaseOrderLineEditDto(string ProductCode,
    decimal Quantity, decimal UnitCost);

public sealed record PurchaseOrderEditDto(Guid SupplierId,
    DateOnly? ExpectedOn, IReadOnlyList<PurchaseOrderLineEditDto> Lines);

/// <param name="ProductCode">
/// The product's own code, so a screen working from the order - goods
/// receipt above all - can show and post the product it names rather than
/// trying to turn its description back into one.
/// </param>
public sealed record PurchaseOrderLineDto(
    int LineNo, Guid ProductId, string Description, decimal QuantityOrdered,
    decimal QuantityReceived, decimal Outstanding, decimal UnitCost,
    decimal LineTotal, string ProductCode = "")
{
    public static PurchaseOrderLineDto From(PurchaseOrderLine l,
                                            string productCode = "")
    {
        ArgumentNullException.ThrowIfNull(l);

        return new PurchaseOrderLineDto(l.LineNo, l.ProductId, l.Description,
            l.QuantityOrdered, l.QuantityReceived, l.Outstanding,
            l.UnitCost.Amount, l.LineTotal.Amount, productCode);
    }
}

public sealed record PurchaseOrderDto(
    Guid Id, string OrderNumber, Guid SupplierId, string SupplierName,
    DateOnly OrderedOn, DateOnly? ExpectedOn, string Status,
    decimal Total, IReadOnlyList<PurchaseOrderLineDto> Lines)
{
    /// <param name="codes">
    /// Product codes by product, so each line can carry its product's code.
    /// </param>
    public static PurchaseOrderDto From(PurchaseOrder o, string supplierName,
        IReadOnlyDictionary<Guid, string>? codes = null)
    {
        ArgumentNullException.ThrowIfNull(o);

        return new PurchaseOrderDto(o.Id, o.OrderNumber, o.SupplierId,
            supplierName, o.OrderedOn, o.ExpectedOn, o.Status.ToString(),
            o.Total.Amount,
            o.Lines.Select(l => PurchaseOrderLineDto.From(l,
                codes?.GetValueOrDefault(l.ProductId) ?? "")).ToList());
    }
}
