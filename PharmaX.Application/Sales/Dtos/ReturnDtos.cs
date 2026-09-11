using PharmaX.Domain.Sales;

namespace PharmaX.Application.Sales.Dtos;

/// <summary>
/// One line of the original sale, with what is still returnable on it.
/// The returnable figure is what the screen offers, because previous
/// returns are separate documents the operator cannot see from here.
/// </summary>
public sealed record ReturnableLineDto(
    int LineNo, Guid ProductId, string Description, decimal QuantitySold,
    decimal AlreadyReturned, decimal Returnable, decimal UnitPrice,
    string TaxCode, string? BatchNumber, DateOnly? Expiry,
    bool IsExpired, bool IsVoided);

public sealed record ReturnableSaleDto(
    Guid SaleId, string DocumentNumber, DateTime OccurredAtUtc,
    decimal Total, IReadOnlyList<ReturnableLineDto> Lines,
    IReadOnlyList<string> TendersUsed);

public sealed record ReturnLineEditDto(int OriginalLineNo,
    decimal Quantity, bool RestockToShelf, string? LineReason);

public sealed record ReturnEditDto(
    Guid SaleId, string Kind, string Reason, Guid AuthorisedBy,
    IReadOnlyList<ReturnLineEditDto> Lines,
    IReadOnlyList<PaymentDto> Refunds);

public sealed record ReturnLineDto(
    int LineNo, int OriginalLineNo, string Description, decimal Quantity,
    decimal UnitPrice, decimal LineTotal, bool RestockToShelf,
    string? LineReason)
{
    public static ReturnLineDto From(SaleReturnLine l)
    {
        ArgumentNullException.ThrowIfNull(l);

        return new ReturnLineDto(l.LineNo, l.OriginalLineNo, l.Description,
            l.Quantity, l.UnitPrice.Amount, l.LineTotal.Amount,
            l.RestockToShelf, l.LineReason);
    }
}

public sealed record ReturnDto(
    Guid Id, string DocumentNumber, string Kind, string Status,
    string OriginalDocumentNumber, DateTime OccurredAtUtc, string Reason,
    decimal Total, decimal Refunded, decimal WriteOffValue,
    int RestockedLines, IReadOnlyList<ReturnLineDto> Lines)
{
    public static ReturnDto From(SaleReturn r)
    {
        ArgumentNullException.ThrowIfNull(r);

        return new ReturnDto(r.Id, r.DocumentNumber, r.Kind.ToString(),
            r.Status.ToString(), r.OriginalDocumentNumber, r.OccurredAtUtc,
            r.Reason, r.Total.Amount, r.Refunded.Amount,
            r.WriteOffValue.Amount, r.Restockable.Count,
            r.Lines.Select(ReturnLineDto.From).ToList());
    }
}

public sealed record ReturnListItemDto(
    Guid Id, string DocumentNumber, string Kind,
    string OriginalDocumentNumber, DateTime OccurredAtUtc, string Reason,
    decimal Total, int Lines);
