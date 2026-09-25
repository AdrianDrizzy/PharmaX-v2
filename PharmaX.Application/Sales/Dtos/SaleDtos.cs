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

/// <param name="Payments">
/// How it was paid, at the amounts that stayed in the drawer. Printed on
/// the receipt, which has to say how the customer paid.
/// </param>
/// <param name="AccountId">The account the sale is on, if any.</param>
public sealed record SaleDto(
    Guid Id, string DocumentNumber, IReadOnlyList<SaleLineDto> Lines,
    SaleTotalsDto Totals, IReadOnlyList<PaymentDto>? Payments = null,
    string Status = "Open", Guid? AccountId = null,
    Guid? QuotationId = null, DateTime? OccurredAtUtc = null)
{
    public static SaleDto From(Sale s)
    {
        ArgumentNullException.ThrowIfNull(s);

        var vat = s.Lines.Where(l => !l.IsVoided)
                         .Aggregate(0m, (sum, l) => sum + l.Vat.VatAmount.Amount);

        return new SaleDto(
            s.Id,
            s.DocumentNumber,
            // In line order: a sale read back from the database - a held
            // basket recalled, a receipt reprinted - comes with its lines
            // in whatever order the rows arrive.
            s.Lines.OrderBy(l => l.LineNo).Select(SaleLineDto.From).ToList(),
            new SaleTotalsDto(
                s.Subtotal.Amount, s.DiscountTotal.Amount, vat,
                s.Total.Amount, s.Tendered.Amount, s.Change.Amount),
            s.Payments.Select(p => new PaymentDto(p.Tender.ToString(),
                p.Amount.Amount, p.Reference)).ToList(),
            s.Status.ToString(), s.AccountId, s.QuotationId,
            s.OccurredAtUtc);
    }

    public string ToJson() => JsonSerializer.Serialize(this);
}

public sealed record PaymentDto(string TenderType, decimal Amount,
                               string? Reference);

/// <param name="Change">The change to hand back from the cash tender.</param>
public sealed record FinaliseDto(string DocumentNumber, decimal Total,
                                 decimal Change = 0m);

/// <summary>A parked basket, as the recall list shows it.</summary>
public sealed record HeldSaleDto(Guid Id, DateTime HeldAtLocal,
    string OperatorName, int Lines, decimal Total, string? Customer);
