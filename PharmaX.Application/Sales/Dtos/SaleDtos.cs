using System.Text.Json;
using PharmaX.Domain.Sales;

namespace PharmaX.Application.Sales.Dtos;

public sealed record SaleLineDto(
    int LineNo, Guid ProductId, string Description, decimal Quantity,
    decimal UnitPrice, decimal Discount, string TaxCode,
    decimal LineTotal, string? BatchNumber, DateOnly? Expiry,
    bool IsRated, bool IsVoided)
{
    public static SaleLineDto From(SaleLine l)
    {
        ArgumentNullException.ThrowIfNull(l);

        return new SaleLineDto(
            l.LineNo, l.ProductId, l.Description, l.Quantity,
            l.UnitPrice.Amount, l.DiscountAmount.Amount, l.TaxCode,
            l.LineTotal.Amount, l.BatchNumber, l.BatchExpiry, l.IsRated,
            l.IsVoided);
    }
}

public sealed record SaleTotalsDto(
    decimal Subtotal, decimal Discount, decimal VatTotal,
    decimal Total, decimal Tendered, decimal Change)
{
    public static SaleTotalsDto Empty { get; } =
        new(0m, 0m, 0m, 0m, 0m, 0m);
}

public sealed record SaleDto(
    Guid Id, string DocumentNumber, IReadOnlyList<SaleLineDto> Lines,
    SaleTotalsDto Totals)
{
    public static SaleDto From(Sale s)
    {
        ArgumentNullException.ThrowIfNull(s);

        var vat = s.Lines.Where(l => !l.IsVoided)
                         .Aggregate(0m, (sum, l) => sum + l.Vat.VatAmount.Amount);

        return new SaleDto(
            s.Id,
            s.DocumentNumber,
            s.Lines.Select(SaleLineDto.From).ToList(),
            new SaleTotalsDto(
                s.Subtotal.Amount, s.DiscountTotal.Amount, vat,
                s.Total.Amount, s.Tendered.Amount,
                Math.Max(0m, s.Tendered.Amount - s.Total.Amount)));
    }

    public string ToJson() => JsonSerializer.Serialize(this);
}

public sealed record PaymentDto(string TenderType, decimal Amount,
                               string? Reference);

public sealed record FinaliseDto(string DocumentNumber, decimal Total);
