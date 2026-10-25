using System.Text.Json;
using PharmaX.Domain.Sales;

namespace PharmaX.Application.Sales.Dtos;

/// <param name="VatRatePercent">
/// The VAT rate the line was sold at, which the receipt prints beside
/// the VAT total and marks a zero-rated line by.
/// </param>
/// <param name="ProductCode">
/// The product's code, as the receipt heads each line with it. Not on
/// the sale line itself; filled in for printing.
/// </param>
public sealed record SaleLineDto(
    int LineNo, Guid ProductId, string Description, decimal Quantity,
    decimal UnitPrice, decimal Discount, string TaxCode,
    decimal LineTotal, string? BatchNumber, DateOnly? Expiry,
    bool IsRated, bool IsVoided, decimal VatRatePercent = 0m,
    string? ProductCode = null, bool IsScript = false)
{
    public static SaleLineDto From(SaleLine l)
    {
        ArgumentNullException.ThrowIfNull(l);

        return new SaleLineDto(
            l.LineNo, l.ProductId, l.Description, l.Quantity,
            l.UnitPrice.Amount, l.DiscountAmount.Amount, l.TaxCode,
            l.LineTotal.Amount, l.BatchNumber, l.BatchExpiry, l.IsRated,
            l.IsVoided, l.VatRatePercent, IsScript: l.IsScript);
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
/// <param name="Particulars">
/// Who the receipt is for - the account, the patient, the script - where
/// the sale was for somebody in particular. Filled in for printing.
/// </param>
/// <param name="OperatorName">
/// Who rang the sale up, filled in where a receipt is found to be printed
/// again: a copy names the cashier who sold it, not whoever reprinted it.
/// </param>
public sealed record SaleDto(
    Guid Id, string DocumentNumber, IReadOnlyList<SaleLineDto> Lines,
    SaleTotalsDto Totals, IReadOnlyList<PaymentDto>? Payments = null,
    string Status = "Open", Guid? AccountId = null,
    Guid? QuotationId = null, DateTime? OccurredAtUtc = null,
    ReceiptParticularsDto? Particulars = null, string? OperatorName = null)
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
