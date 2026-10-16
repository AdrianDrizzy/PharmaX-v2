using PharmaX.Application.Abstractions;
using PharmaX.Application.Inventory.Dtos;
using PharmaX.Application.Repositories;
using PharmaX.Application.Results;
using PharmaX.Application.Security;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Inventory;

public interface IStocktakeService
{
    Task<Result<IReadOnlyList<StocktakeListItemDto>>> RecentAsync(
        CancellationToken ct = default);

    Task<Result<StocktakeDto>> GetAsync(Guid id,
        CancellationToken ct = default);

    /// <summary>
    /// Opens a count and snapshots what the system currently believes
    /// onto every line, so trading during the count does not corrupt the
    /// variance.
    /// </summary>
    Task<Result<StocktakeDto>> OpenAsync(StocktakeScopeDto scope,
        CancellationToken ct = default);

    Task<Result<StocktakeDto>> RecordCountAsync(Guid id, int lineNo,
        decimal? counted, CancellationToken ct = default);

    Task<Result> ExplainVarianceAsync(Guid id, string reason,
        CancellationToken ct = default);

    Task<Result<StocktakeDto>> PostAsync(Guid id,
        CancellationToken ct = default);

    Task<Result> CancelAsync(Guid id, string reason,
        CancellationToken ct = default);
}

public sealed class StocktakeService : IStocktakeService
{
    /// <summary>
    /// Above this value of net variance, posting needs a written reason.
    /// The same shape as the till's variance threshold, and for the same
    /// reason: a difference nobody explained is a difference nobody
    /// investigated.
    /// </summary>
    private const decimal VarianceReasonThreshold = 500m;

    private readonly IStocktakeRepository _stocktakes;
    private readonly IProductRepository _products;
    private readonly IBatchRepository _batches;
    private readonly IStockService _stock;
    private readonly IDocumentNumbering _numbering;
    private readonly IPermissionService _permissions;
    private readonly IAuditWriter _audit;
    private readonly IOutbox _outbox;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly PharmaXOptions _options;

    public StocktakeService(IStocktakeRepository stocktakes,
        IProductRepository products, IBatchRepository batches,
        IStockService stock, IDocumentNumbering numbering,
        IPermissionService permissions, IAuditWriter audit, IOutbox outbox,
        IUnitOfWork uow, ICurrentUser user, IClock clock,
        PharmaXOptions options)
    {
        _stocktakes = stocktakes;
        _products = products;
        _batches = batches;
        _stock = stock;
        _numbering = numbering;
        _permissions = permissions;
        _audit = audit;
        _outbox = outbox;
        _uow = uow;
        _user = user;
        _clock = clock;
        _options = options;
    }

    public async Task<Result<IReadOnlyList<StocktakeListItemDto>>> RecentAsync(
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.StockTakeCapture);

        var stocktakes = await _stocktakes.RecentAsync(ct: ct);

        IReadOnlyList<StocktakeListItemDto> items =
            stocktakes.Select(StocktakeListItemDto.From).ToList();

        return Result<IReadOnlyList<StocktakeListItemDto>>.Ok(items);
    }

    public async Task<Result<StocktakeDto>> GetAsync(Guid id,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.StockTakeCapture);

        var stocktake = await _stocktakes.GetAsync(id, ct);

        return stocktake is null
            ? Result<StocktakeDto>.Fail("STK-000", "Stocktake not found.")
            : Result<StocktakeDto>.Ok(
                StocktakeDto.From(stocktake, VarianceReasonThreshold));
    }

    public async Task<Result<StocktakeDto>> OpenAsync(StocktakeScopeDto scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        _permissions.Demand(PermissionCode.StockTakeCapture);

        if (string.IsNullOrWhiteSpace(scope.Description))
            return Result<StocktakeDto>.Fail("STK-001",
                "Describe what is being counted.");

        // One open count at a time. Two people counting the same shelf
        // against two different snapshots produce two contradictory
        // variances and no way to tell which is right.
        if (await _stocktakes.OpenCountAsync(ct) is Stocktake existing)
            return Result<StocktakeDto>.Fail("STK-002",
                $"Stocktake {existing.StocktakeNumber} ({existing.Scope}) "
                + "is still counting. Post or cancel it first.");

        var products = await _products.SearchAsync(scope.SearchTerm ?? "",
                                                   take: int.MaxValue, ct: ct);

        var included = products
            .Where(p => p.IsActive)
            .Where(p => !scope.BatchTrackedOnly || p.IsBatchTracked)
            .Where(p => !scope.BelowReorderOnly || p.IsBelowReorderLevel)
            .ToList();

        if (included.Count == 0)
            return Result<StocktakeDto>.Fail("STK-003",
                "Nothing matches that scope, so there is nothing to count.");

        var number = await _numbering.NextAsync(DocumentSeries.Stocktake, ct);

        var stocktake = Stocktake.Open(number,
            Guid.Parse(_options.Workstation.BranchId), _user.Id,
            scope.Description, _clock.UtcNow);

        foreach (var product in included.OrderBy(p => p.Description))
        {
            if (!product.IsBatchTracked)
            {
                stocktake.AddLine(StocktakeLine.For(product.Id,
                    product.Description, product.QuantityOnHand,
                    product.CostPrice));

                continue;
            }

            // A line per batch: a person counting a shelf reads the
            // batch number off the box, and a single total for the
            // product would hide a mislabelled batch entirely.
            var batches = (await _batches.ForProductAsync(product.Id, ct))
                .Where(b => b.QuantityOnHand > 0m
                            || b.Status == Domain.Batches.BatchStatus.Available)
                .OrderBy(b => b.ExpiryDate)
                .ToList();

            if (batches.Count == 0)
            {
                stocktake.AddLine(StocktakeLine.For(product.Id,
                    product.Description, product.QuantityOnHand,
                    product.CostPrice));

                continue;
            }

            foreach (var batch in batches)
                stocktake.AddLine(StocktakeLine.For(product.Id,
                    product.Description, batch.QuantityOnHand,
                    batch.CostPrice, batch.Id, batch.BatchNumber,
                    batch.ExpiryDate));
        }

        await _stocktakes.AddAsync(stocktake, ct);

        _audit.Applied("inv.stocktake.open", "Stocktake", stocktake.Id,
            $"{stocktake.Lines.Count} line(s): {scope.Description}");

        await _uow.SaveChangesAsync(ct);

        return Result<StocktakeDto>.Ok(
            StocktakeDto.From(stocktake, VarianceReasonThreshold));
    }

    public async Task<Result<StocktakeDto>> RecordCountAsync(Guid id,
        int lineNo, decimal? counted, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.StockTakeCapture);

        var stocktake = await _stocktakes.GetAsync(id, ct);
        if (stocktake is null)
            return Result<StocktakeDto>.Fail("STK-000",
                "Stocktake not found.");

        // Null clears the count. A zero and an uncounted line are
        // different answers: zero means the shelf was empty, uncounted
        // means nobody looked.
        if (counted is decimal value) stocktake.RecordCount(lineNo, value);
        else stocktake.ClearCount(lineNo);

        await _uow.SaveChangesAsync(ct);

        return Result<StocktakeDto>.Ok(
            StocktakeDto.From(stocktake, VarianceReasonThreshold));
    }

    public async Task<Result> ExplainVarianceAsync(Guid id, string reason,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.StockTakeCapture);

        var stocktake = await _stocktakes.GetAsync(id, ct);
        if (stocktake is null) return Result.Fail("STK-000",
            "Stocktake not found.");

        stocktake.ExplainVariance(reason);

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result<StocktakeDto>> PostAsync(Guid id,
        CancellationToken ct = default)
    {
        // Posting writes stock off, so it is its own permission rather
        // than something anyone who can count can also do.
        _permissions.Demand(PermissionCode.StockTakePost);

        await using var tx = await _uow.BeginTransactionAsync(ct);

        var stocktake = await _stocktakes.GetAsync(id, ct);
        if (stocktake is null)
            return Result<StocktakeDto>.Fail("STK-000",
                "Stocktake not found.");

        stocktake.Post(_user.Id, _clock.UtcNow, VarianceReasonThreshold);

        // The point at which the system's figures change to match the
        // shelf, inside this transaction: a post that fails to save
        // moves no stock.
        await _stock.ApplyStocktakeAsync(stocktake, ct);

        _audit.Applied("inv.stocktake.post", "Stocktake", stocktake.Id,
            $"Net variance {stocktake.NetVarianceValue} over "
            + $"{stocktake.Variances.Count} line(s). "
            + (stocktake.VarianceReason ?? ""));

        _outbox.Append("Stocktake", stocktake.Id,
            $"{{\"id\":\"{stocktake.Id}\",\"number\":\""
            + $"{stocktake.StocktakeNumber}\"}}");

        await _uow.SaveChangesAsync(ct);
        await tx.DisposeAsync();

        return Result<StocktakeDto>.Ok(
            StocktakeDto.From(stocktake, VarianceReasonThreshold));
    }

    public async Task<Result> CancelAsync(Guid id, string reason,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.StockTakeCapture);

        var stocktake = await _stocktakes.GetAsync(id, ct);
        if (stocktake is null) return Result.Fail("STK-000",
            "Stocktake not found.");

        stocktake.Cancel(reason);

        _audit.Applied("inv.stocktake.cancel", "Stocktake", stocktake.Id,
                       reason);

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }
}
