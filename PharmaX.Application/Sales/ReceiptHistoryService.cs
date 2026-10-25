using PharmaX.Application.Abstractions;
using PharmaX.Application.Repositories;
using PharmaX.Application.Results;
using PharmaX.Application.Sales.Dtos;

namespace PharmaX.Application.Sales;

/// <summary>One line of the day's receipts.</summary>
public sealed record ReceiptSummaryDto(Guid SaleId, string DocumentNumber,
    DateTime OccurredAtLocal, string Operator, decimal Total, string Tenders);

/// <summary>
/// The day's receipts, for anybody to find and print again: the customer
/// back at the counter wants a copy whoever is serving them.
/// </summary>
/// <remarks>
/// Open to every signed-in user, unlike the till's own reprint (F12),
/// which belongs to whoever may sell. A copy is still a copy: it prints
/// "COPY - REPRINTED" and the reprint is recorded against whoever asked.
/// </remarks>
public interface IReceiptHistoryService
{
    Task<IReadOnlyList<ReceiptSummaryDto>> DayAsync(DateOnly day, CancellationToken ct = default);

    /// <summary>The receipt ready to print again: its lines, its particulars and its cashier.</summary>
    Task<Result<SaleDto>> ReceiptAsync(string documentNumber, CancellationToken ct = default);

    Task RecordReprintAsync(SaleDto sale, CancellationToken ct = default);
}

public sealed class ReceiptHistoryService : IReceiptHistoryService
{
    private readonly ISaleRepository _sales;
    private readonly IUserRepository _users;
    private readonly IReceiptParticularsService _particulars;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;

    public ReceiptHistoryService(ISaleRepository sales, IUserRepository users,
        IReceiptParticularsService particulars, IAuditWriter audit, IUnitOfWork uow)
    {
        _sales = sales;
        _users = users;
        _particulars = particulars;
        _audit = audit;
        _uow = uow;
    }

    public async Task<IReadOnlyList<ReceiptSummaryDto>> DayAsync(DateOnly day,
        CancellationToken ct = default)
    {
        // The pharmacy's day, midnight to midnight where the till stands.
        var from = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
        var to = day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();

        var sales = await _sales.FinalisedBetweenAsync(from, to, ct);
        var names = new Dictionary<Guid, string>();
        var list = new List<ReceiptSummaryDto>(sales.Count);

        foreach (var sale in sales.Where(s => s.DocumentNumber.Length > 0))
        {
            if (!names.TryGetValue(sale.OperatorId, out var name))
            {
                name = (await _users.GetAsync(sale.OperatorId, ct))?.Username ?? "";
                names[sale.OperatorId] = name;
            }

            var dto = SaleDto.From(sale);
            var tenders = string.Join(", ", (dto.Payments ?? Array.Empty<PaymentDto>()).Select(p => p.TenderType).Distinct());

            list.Add(new ReceiptSummaryDto(sale.Id, sale.DocumentNumber,
                DateTime.SpecifyKind(sale.OccurredAtUtc, DateTimeKind.Utc).ToLocalTime(),
                name, dto.Totals.Total, tenders));
        }

        return list;
    }

    public async Task<Result<SaleDto>> ReceiptAsync(string documentNumber,
        CancellationToken ct = default)
    {
        var number = (documentNumber ?? "").Trim().ToUpperInvariant();
        var sale = number.Length == 0 ? null : await _sales.ByDocumentNumberAsync(number, ct);

        if (sale is not { Status: Domain.Sales.SaleStatus.Finalised })
            return Result<SaleDto>.Fail("POS-024", $"There is no receipt {number}.");

        var cashier = await _users.GetAsync(sale.OperatorId, ct);
        var dto = SaleDto.From(sale) with { OperatorName = cashier?.Username };

        return Result<SaleDto>.Ok(await _particulars.ForReceiptAsync(dto, ct));
    }

    public async Task RecordReprintAsync(SaleDto sale, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sale);

        _audit.Applied("pos.receipt.reprint", "Sale", sale.Id,
            $"Receipt {sale.DocumentNumber} printed again from Receipts");

        await _uow.SaveChangesAsync(ct);
    }
}
