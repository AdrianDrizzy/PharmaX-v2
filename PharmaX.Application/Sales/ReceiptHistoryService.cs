using PharmaX.Application.Abstractions;
using PharmaX.Application.Repositories;
using PharmaX.Application.Results;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Application.Security;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Sales;

/// <summary>One line of the receipts list.</summary>
/// <param name="HiddenReason">
/// Why it was taken off the list, where it was; null while it is listed.
/// </param>
public sealed record ReceiptSummaryDto(Guid SaleId, string DocumentNumber,
    DateTime OccurredAtLocal, string Operator, decimal Total, string Tenders,
    string? HiddenReason = null)
{
    public bool IsHidden => HiddenReason is not null;
}

/// <summary>What the receipts history is asked for: a span of days and a page.</summary>
/// <param name="Find">Part of a receipt number, or a cashier's name.</param>
/// <param name="IncludeHidden">
/// Receipts taken off the list as well. Only for whoever may hide them.
/// </param>
public sealed record ReceiptQuery(DateOnly From, DateOnly To, string? Find,
    bool IncludeHidden, int Skip, int Take);

public sealed record ReceiptPageDto(IReadOnlyList<ReceiptSummaryDto> Items, int TotalCount);

/// <summary>
/// The receipts printed, any day, for anybody to find and print again:
/// the customer back at the counter wants a copy whoever is serving them.
/// </summary>
/// <remarks>
/// Open to every signed-in user, unlike the till's own reprint (F12),
/// which belongs to whoever may sell. A copy is still a copy: it prints
/// "COPY - REPRINTED" and the reprint is recorded against whoever asked.
/// Taking a receipt off the list (pos.receipt.hide) hides the receipt
/// only; the sale, the cash-up and the VAT are untouched.
/// </remarks>
public interface IReceiptHistoryService
{
    Task<IReadOnlyList<ReceiptSummaryDto>> DayAsync(DateOnly day, CancellationToken ct = default);

    /// One page of the receipts between two days, newest first.
    Task<ReceiptPageDto> PageAsync(ReceiptQuery query, CancellationToken ct = default);

    /// <summary>The receipt ready to print again: its lines, its particulars and its cashier.</summary>
    Task<Result<SaleDto>> ReceiptAsync(string documentNumber, CancellationToken ct = default);

    Task RecordReprintAsync(SaleDto sale, CancellationToken ct = default);

    /// Whether this user may take receipts off the list and put them back.
    bool MayHide { get; }

    /// Takes a receipt off the list, with the reason; the sale stays.
    Task<Result> HideAsync(Guid saleId, string reason, CancellationToken ct = default);

    /// Puts a hidden receipt back on the list.
    Task<Result> ShowAsync(Guid saleId, CancellationToken ct = default);
}

public sealed class ReceiptHistoryService : IReceiptHistoryService
{
    private readonly ISaleRepository _sales;
    private readonly IUserRepository _users;
    private readonly IReceiptParticularsService _particulars;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;
    private readonly IPermissionService _permissions;

    public ReceiptHistoryService(ISaleRepository sales, IUserRepository users,
        IReceiptParticularsService particulars, IAuditWriter audit, IUnitOfWork uow,
        IPermissionService permissions)
    {
        _sales = sales;
        _users = users;
        _particulars = particulars;
        _audit = audit;
        _uow = uow;
        _permissions = permissions;
    }

    public bool MayHide => _permissions.Has(PermissionCode.ReceiptHide);

    /// The pharmacy's day, midnight to midnight where the till stands.
    private static DateTime StartOf(DateOnly day) =>
        day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();

    public async Task<IReadOnlyList<ReceiptSummaryDto>> DayAsync(DateOnly day,
        CancellationToken ct = default)
    {
        var sales = await _sales.FinalisedBetweenAsync(StartOf(day), StartOf(day.AddDays(1)), ct);
        var names = new Dictionary<Guid, string>();
        var list = new List<ReceiptSummaryDto>(sales.Count);

        foreach (var sale in sales.Where(s => s.DocumentNumber.Length > 0 && !s.IsReceiptHidden))
            list.Add(await SummaryAsync(sale, names, ct));

        return list;
    }

    public async Task<ReceiptPageDto> PageAsync(ReceiptQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var (from, to) = query.From <= query.To ? (query.From, query.To) : (query.To, query.From);
        var find = string.IsNullOrWhiteSpace(query.Find) ? null : query.Find.Trim();

        // A cashier is found by name, which the sale does not carry: the
        // ids of the accounts whose name has the words in it.
        IReadOnlyCollection<Guid> operators = find is null
            ? Array.Empty<Guid>()
            : (await _users.ListAsync(includeInactive: true, ct))
                .Where(u => u.Username.Contains(find, StringComparison.OrdinalIgnoreCase)
                            || (u.DisplayName ?? "").Contains(find, StringComparison.OrdinalIgnoreCase))
                .Select(u => u.Id)
                .ToList();

        var (page, total) = await _sales.ReceiptsAsync(StartOf(from), StartOf(to.AddDays(1)),
            find, operators, query.IncludeHidden && MayHide, Math.Max(0, query.Skip),
            Math.Max(1, query.Take), ct);

        var names = new Dictionary<Guid, string>();
        var items = new List<ReceiptSummaryDto>(page.Count);

        foreach (var sale in page)
            items.Add(await SummaryAsync(sale, names, ct));

        return new ReceiptPageDto(items, total);
    }

    private async Task<ReceiptSummaryDto> SummaryAsync(Domain.Sales.Sale sale,
        Dictionary<Guid, string> names, CancellationToken ct)
    {
        if (!names.TryGetValue(sale.OperatorId, out var name))
        {
            name = (await _users.GetAsync(sale.OperatorId, ct))?.Username ?? "";
            names[sale.OperatorId] = name;
        }

        var dto = SaleDto.From(sale);
        var tenders = string.Join(", ", (dto.Payments ?? Array.Empty<PaymentDto>())
            .Select(p => p.TenderType).Distinct());

        return new ReceiptSummaryDto(sale.Id, sale.DocumentNumber,
            DateTime.SpecifyKind(sale.OccurredAtUtc, DateTimeKind.Utc).ToLocalTime(),
            name, dto.Totals.Total, tenders, sale.ReceiptHiddenReason);
    }

    public async Task<Result<SaleDto>> ReceiptAsync(string documentNumber,
        CancellationToken ct = default)
    {
        var number = (documentNumber ?? "").Trim().ToUpperInvariant();
        var sale = number.Length == 0 ? null : await _sales.ByDocumentNumberAsync(number, ct);

        if (sale is not { Status: Domain.Sales.SaleStatus.Finalised })
            return Result<SaleDto>.Fail("POS-024", $"There is no receipt {number}.");

        if (sale.IsReceiptHidden)
            return Result<SaleDto>.Fail("POS-025",
                $"Receipt {number} was taken off the list ({sale.ReceiptHiddenReason}). "
                + "Show it again to print a copy.");

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

    public async Task<Result> HideAsync(Guid saleId, string reason, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ReceiptHide);

        var sale = await _sales.GetAsync(saleId, ct);
        if (sale is null) return Result.Fail("POS-024", "That receipt no longer exists.");

        if (sale.IsReceiptHidden)
            return Result.Fail("POS-026", $"Receipt {sale.DocumentNumber} is already hidden.");

        try
        {
            sale.HideReceipt(reason);
        }
        catch (DomainException ex)
        {
            return Result.Fail("POS-027", ex.Message);
        }

        _audit.Applied(PermissionCode.ReceiptHide, "Sale", sale.Id,
            $"Receipt {sale.DocumentNumber} taken off the list: {sale.ReceiptHiddenReason}");

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result> ShowAsync(Guid saleId, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ReceiptHide);

        var sale = await _sales.GetAsync(saleId, ct);
        if (sale is null) return Result.Fail("POS-024", "That receipt no longer exists.");

        if (!sale.IsReceiptHidden) return Result.Ok();

        var was = sale.ReceiptHiddenReason;
        sale.ShowReceipt();

        _audit.Applied("pos.receipt.show", "Sale", sale.Id,
            $"Receipt {sale.DocumentNumber} back on the list (was hidden: {was})");

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }
}
