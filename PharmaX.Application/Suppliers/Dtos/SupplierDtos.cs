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
/// rather than an identifier because the operator is reading a delivery
/// note, not a database.
/// </summary>
public sealed record GoodsReceiptLineEditDto(
    string ProductCode, decimal Quantity, decimal UnitCost,
    string? BatchNumber, DateOnly? ExpiryDate,
    int? PurchaseOrderLineNo = null);

public sealed record GoodsReceiptEditDto(
    Guid SupplierId, Guid? PurchaseOrderId, string? SupplierInvoiceNumber,
    DateOnly ReceivedOn, decimal? InvoiceTotal,
    IReadOnlyList<GoodsReceiptLineEditDto> Lines);

public sealed record GoodsReceiptLineDto(
    int LineNo, Guid ProductId, string Description, decimal Quantity,
    decimal UnitCost, decimal LineTotal, string? BatchNumber,
    DateOnly? ExpiryDate)
{
    public static GoodsReceiptLineDto From(GoodsReceiptLine l)
    {
        ArgumentNullException.ThrowIfNull(l);

        return new GoodsReceiptLineDto(l.LineNo, l.ProductId, l.Description,
            l.Quantity, l.UnitCost.Amount, l.LineTotal.Amount,
            l.BatchNumber, l.ExpiryDate);
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
            r.Lines.Select(GoodsReceiptLineDto.From).ToList());
    }
}

public sealed record GoodsReceiptListItemDto(
    Guid Id, string ReceiptNumber, string SupplierName,
    string? SupplierInvoiceNumber, DateOnly ReceivedOn, int Lines,
    decimal Total, bool IsPosted);

public sealed record PurchaseOrderLineEditDto(string ProductCode,
    decimal Quantity, decimal UnitCost);

public sealed record PurchaseOrderEditDto(Guid SupplierId,
    DateOnly? ExpectedOn, IReadOnlyList<PurchaseOrderLineEditDto> Lines);

public sealed record PurchaseOrderLineDto(
    int LineNo, Guid ProductId, string Description, decimal QuantityOrdered,
    decimal QuantityReceived, decimal Outstanding, decimal UnitCost,
    decimal LineTotal)
{
    public static PurchaseOrderLineDto From(PurchaseOrderLine l)
    {
        ArgumentNullException.ThrowIfNull(l);

        return new PurchaseOrderLineDto(l.LineNo, l.ProductId, l.Description,
            l.QuantityOrdered, l.QuantityReceived, l.Outstanding,
            l.UnitCost.Amount, l.LineTotal.Amount);
    }
}

public sealed record PurchaseOrderDto(
    Guid Id, string OrderNumber, Guid SupplierId, string SupplierName,
    DateOnly OrderedOn, DateOnly? ExpectedOn, string Status,
    decimal Total, IReadOnlyList<PurchaseOrderLineDto> Lines)
{
    public static PurchaseOrderDto From(PurchaseOrder o, string supplierName)
    {
        ArgumentNullException.ThrowIfNull(o);

        return new PurchaseOrderDto(o.Id, o.OrderNumber, o.SupplierId,
            supplierName, o.OrderedOn, o.ExpectedOn, o.Status.ToString(),
            o.Total.Amount,
            o.Lines.Select(PurchaseOrderLineDto.From).ToList());
    }
}
