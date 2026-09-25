using PharmaX.Application.Abstractions;
using PharmaX.Application.Inventory;
using PharmaX.Application.Finance;
using PharmaX.Application.Repositories;
using PharmaX.Application.Results;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Application.Security;
using PharmaX.Domain.Sales;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Sales;

public interface IReturnService
{
    /// <summary>
    /// Finds the sale a customer is returning against, by the number on
    /// their receipt, with the still-returnable quantity worked out per
    /// line.
    /// </summary>
    Task<Result<ReturnableSaleDto>> FindSaleAsync(string documentNumber,
        CancellationToken ct = default);

    Task<Result<ReturnDto>> GetAsync(Guid id, CancellationToken ct = default);

    Task<Result<IReadOnlyList<ReturnListItemDto>>> RecentAsync(
        CancellationToken ct = default);

    Task<Result<ReturnDto>> CreateAsync(ReturnEditDto edit,
        CancellationToken ct = default);
}

public sealed class ReturnService : IReturnService
{
    private readonly ISaleRepository _sales;
    private readonly IReturnRepository _returns;
    private readonly IJournalPoster _journals;
    private readonly IStockService _stock;
    private readonly IDocumentNumbering _numbering;
    private readonly IPermissionService _permissions;
    private readonly IAuditWriter _audit;
    private readonly IOutbox _outbox;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public ReturnService(ISaleRepository sales, IReturnRepository returns,
        IJournalPoster journals, IStockService stock,
        IDocumentNumbering numbering, IPermissionService permissions,
        IAuditWriter audit, IOutbox outbox, IUnitOfWork uow,
        ICurrentUser user, IClock clock)
    {
        _sales = sales;
        _returns = returns;
        _journals = journals;
        _stock = stock;
        _numbering = numbering;
        _permissions = permissions;
        _audit = audit;
        _outbox = outbox;
        _uow = uow;
        _user = user;
        _clock = clock;
    }

    public async Task<Result<ReturnableSaleDto>> FindSaleAsync(
        string documentNumber, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.SaleCreate);

        if (string.IsNullOrWhiteSpace(documentNumber))
            return Result<ReturnableSaleDto>.Fail("RET-001",
                "Enter the document number from the receipt.");

        var sale = await _sales.ByDocumentNumberAsync(documentNumber.Trim(), ct);

        if (sale is null)
            return Result<ReturnableSaleDto>.Fail("RET-002",
                $"No sale with document number {documentNumber}.");

        if (sale.Status != SaleStatus.Finalised)
            return Result<ReturnableSaleDto>.Fail("RET-003",
                "That sale was never completed, so there is nothing to "
                + "return against.");

        // Every previous return against this sale, so the screen offers
        // what is genuinely left rather than the original quantities.
        var returned = await _returns.ReturnedQuantitiesAsync(sale.Id, ct);

        // In line order, as the receipt the customer is holding reads.
        var lines = sale.Lines.OrderBy(l => l.LineNo).Select(l =>
        {
            var already = returned.GetValueOrDefault(l.LineNo, 0m);

            return new ReturnableLineDto(
                LineNo: l.LineNo,
                ProductId: l.ProductId,
                Description: l.Description,
                QuantitySold: l.Quantity,
                AlreadyReturned: already,
                Returnable: l.IsVoided ? 0m : l.Quantity - already,

                // What was actually charged per unit, discount included.
                UnitPrice: l.Quantity == 0m
                    ? l.UnitPrice.Amount
                    : decimal.Round(l.LineTotal.Amount / l.Quantity, 2,
                                    MidpointRounding.AwayFromZero),

                TaxCode: l.TaxCode,
                BatchNumber: null,
                Expiry: l.BatchExpiry,
                IsExpired: l.BatchExpiry is DateOnly e && e < _clock.Today,
                IsVoided: l.IsVoided);
        }).ToList();

        return Result<ReturnableSaleDto>.Ok(new ReturnableSaleDto(
            sale.Id, sale.DocumentNumber, sale.OccurredAtUtc,
            sale.Total.Amount, lines,
            sale.Payments.Select(p => p.Tender.ToString()).Distinct().ToList()));
    }

    public async Task<Result<ReturnDto>> GetAsync(Guid id,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.SaleCreate);

        var saleReturn = await _returns.GetAsync(id, ct);

        return saleReturn is null
            ? Result<ReturnDto>.Fail("RET-000", "Return not found.")
            : Result<ReturnDto>.Ok(ReturnDto.From(saleReturn));
    }

    public async Task<Result<IReadOnlyList<ReturnListItemDto>>> RecentAsync(
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.SaleCreate);

        var returns = await _returns.RecentAsync(ct: ct);

        IReadOnlyList<ReturnListItemDto> items = returns
            .Select(r => new ReturnListItemDto(r.Id, r.DocumentNumber,
                r.Kind.ToString(), r.OriginalDocumentNumber,
                r.OccurredAtUtc, r.Reason, r.Total.Amount, r.Lines.Count))
            .ToList();

        return Result<IReadOnlyList<ReturnListItemDto>>.Ok(items);
    }

    public async Task<Result<ReturnDto>> CreateAsync(ReturnEditDto edit,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(edit);

        // A refund is money leaving the drawer, so it is its own
        // permission and a supervisor-escalatable one. The authority is
        // the operator's own, or that of the supervisor who authorised
        // it - whose grant is what is checked. Demanding it of a cashier
        // who had just had a manager authorise the refund refused every
        // cashier refund there has ever been (DEF-0007).
        var authoriser = edit.AuthorisedBy == Guid.Empty
            ? (Guid?)null
            : edit.AuthorisedBy;

        _permissions.DemandOrAuthorised(PermissionCode.ReturnAuthorise,
            authoriser == _user.Id ? null : authoriser);

        // A supervisor authorising their own refund is recorded as such.
        var authorisedBy = authoriser ?? _user.Id;

        if (_user.TillSessionId is null)
            return Result<ReturnDto>.Fail("POS-013",
                "Open a till session before refunding: the money comes "
                + "out of a drawer.");

        if (!Enum.TryParse<ReturnKind>(edit.Kind, out var kind))
            return Result<ReturnDto>.Fail("RET-004",
                $"Unknown return kind '{edit.Kind}'.");

        if (edit.Lines.Count == 0)
            return Result<ReturnDto>.Fail("RET-005",
                "Choose at least one line to return.");

        await using var tx = await _uow.BeginTransactionAsync(ct);

        var sale = await _sales.GetAsync(edit.SaleId, ct);
        if (sale is null)
            return Result<ReturnDto>.Fail("RET-002", "Sale not found.");

        var returned = await _returns.ReturnedQuantitiesAsync(sale.Id, ct);

        var saleReturn = SaleReturn.Against(sale, kind,
            _user.TillSessionId.Value, _user.Id, authorisedBy,
            edit.Reason, _clock.UtcNow);

        foreach (var line in edit.Lines)
        {
            var original = sale.Lines
                .SingleOrDefault(l => l.LineNo == line.OriginalLineNo);

            if (original is null)
                return Result<ReturnDto>.Fail("RET-006",
                    $"Line {line.OriginalLineNo} is not on that sale.");

            saleReturn.AddLine(original, line.Quantity,
                returned.GetValueOrDefault(original.LineNo, 0m),
                line.RestockToShelf, _clock.Today, line.LineReason);
        }

        foreach (var refund in edit.Refunds)
            saleReturn.AddRefund(SalePayment.Create(refund.TenderType,
                Domain.Primitives.Money.Of(refund.Amount),
                refund.Reference));

        var number = await _numbering.NextAsync(DocumentSeries.Return, ct);
        saleReturn.Finalise(number, _clock.UtcNow);

        await _returns.AddAsync(saleReturn, ct);

        // Restockable lines go back on the shelf; the rest are written
        // off, and the movements say which was which.
        await _stock.ApplyReturnAsync(saleReturn, ct);

        // The sale's entry, backwards: money out, revenue and VAT
        // reversed.
        await _journals.PostReturnAsync(saleReturn, ct);

        _outbox.Append("SaleReturn", saleReturn.Id,
            $"{{\"id\":\"{saleReturn.Id}\",\"number\":\"{number}\","
            + $"\"against\":\"{sale.DocumentNumber}\"}}");

        // The authoriser is on the audit entry as well as the document:
        // a pattern of one supervisor authorising every refund is what a
        // manager needs to be able to see (SEC-035).
        _audit.Applied("pos.return.authorise", "SaleReturn", saleReturn.Id,
            $"{saleReturn.Total} against {sale.DocumentNumber}, "
            + $"authorised by {authorisedBy}. {edit.Reason}");

        await _uow.SaveChangesAsync(ct);
        await tx.DisposeAsync();

        return Result<ReturnDto>.Ok(ReturnDto.From(saleReturn));
    }
}
