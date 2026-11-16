using PharmaX.Application.Abstractions;
using PharmaX.Application.Results;
using PharmaX.Application.Security;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Reference;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Reference;

public interface ITariffRepository
{
    /// <summary>
    /// Tariffs with their society and product already resolved, because
    /// a list of identifiers is no use on a screen.
    /// </summary>
    Task<IReadOnlyList<TariffListItemDto>> ListAsync(Guid? schemeId,
        string term, ListFilter? filter = null, CancellationToken ct = default);

    Task<Tariff?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// The tariff that prices this product for this society on this day,
    /// if there is one.
    /// </summary>
    Task<Tariff?> EffectiveAsync(Guid schemeId, Guid productId, DateOnly asAt,
        CancellationToken ct = default);

    Task AddAsync(Tariff tariff, CancellationToken ct = default);
}

public interface ITariffService
{
    Task<Result<IReadOnlyList<TariffListItemDto>>> ListAsync(
        Guid? schemeId = null, string term = "", ListFilter? filter = null,
        CancellationToken ct = default);

    Task<Result<Guid>> CreateAsync(TariffEditDto edit,
        CancellationToken ct = default);

    Task<Result> UpdateAsync(Guid id, TariffEditDto edit,
        CancellationToken ct = default);

    Task<Result> SetActiveAsync(Guid id, bool active,
        CancellationToken ct = default);

    /// <summary>
    /// What a society pays for a product today, or null where no tariff
    /// covers it and the retail price stands.
    /// </summary>
    Task<Result<decimal?>> PriceForAsync(Guid schemeId, Guid productId,
        DateOnly asAt, CancellationToken ct = default);
}

public sealed class TariffService : ITariffService
{
    private readonly ITariffRepository _tariffs;
    private readonly IPermissionService _permissions;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public TariffService(ITariffRepository tariffs,
        IPermissionService permissions, IAuditWriter audit, IUnitOfWork uow,
        IClock clock)
    {
        _tariffs = tariffs;
        _permissions = permissions;
        _audit = audit;
        _uow = uow;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<TariffListItemDto>>> ListAsync(
        Guid? schemeId = null, string term = "", ListFilter? filter = null,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ProductRead);

        var rows = await _tariffs.ListAsync(schemeId, term, filter, ct);
        return Result<IReadOnlyList<TariffListItemDto>>.Ok(rows);
    }

    public async Task<Result<Guid>> CreateAsync(TariffEditDto edit,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(edit);

        _permissions.Demand(PermissionCode.PriceChange);

        // Two tariffs covering the same product for the same society on
        // the same day would leave the price to whichever the query
        // happened to return first.
        var existing = await _tariffs.EffectiveAsync(edit.SchemeId,
            edit.ProductId, edit.EffectiveFrom, ct);

        if (existing is not null)
            return Result<Guid>.Fail("TAR-002",
                "A tariff already covers that product for that society on "
                + $"{edit.EffectiveFrom:dd/MM/yyyy}. Close the existing one "
                + "first, or start this one the day after it ends.");

        var tariff = Tariff.Create(edit.SchemeId, edit.ProductId,
            Money.Of(edit.Price), Money.Of(edit.DispensingFee),
            edit.EffectiveFrom, edit.EffectiveTo);

        await _tariffs.AddAsync(tariff, ct);

        _audit.Applied("ref.tariff.create", "Tariff", tariff.Id,
            $"{Money.Of(edit.Price)} plus {Money.Of(edit.DispensingFee)} fee");

        await _uow.SaveChangesAsync(ct);
        return Result<Guid>.Ok(tariff.Id);
    }

    public async Task<Result> UpdateAsync(Guid id, TariffEditDto edit,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(edit);

        _permissions.Demand(PermissionCode.PriceChange);

        var tariff = await _tariffs.GetAsync(id, ct);
        if (tariff is null)
            return Result.Fail("TAR-000", "That tariff no longer exists.");

        var before = tariff.Total;

        tariff.Reprice(Money.Of(edit.Price), Money.Of(edit.DispensingFee));
        tariff.Reschedule(edit.EffectiveFrom, edit.EffectiveTo);

        // A tariff change moves what a society is billed, so it is worth
        // the same audit trail a retail price change gets.
        _audit.Applied("ref.tariff.update", "Tariff", tariff.Id,
                       $"{before} to {tariff.Total}");

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result> SetActiveAsync(Guid id, bool active,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.PriceChange);

        var tariff = await _tariffs.GetAsync(id, ct);
        if (tariff is null)
            return Result.Fail("TAR-000", "That tariff no longer exists.");

        if (active) tariff.Reactivate(); else tariff.Deactivate();

        _audit.Applied(
            $"ref.tariff.{(active ? "reactivate" : "deactivate")}",
            "Tariff", tariff.Id);

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result<decimal?>> PriceForAsync(Guid schemeId,
        Guid productId, DateOnly asAt, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ProductRead);

        var tariff = await _tariffs.EffectiveAsync(schemeId, productId, asAt, ct);
        return Result<decimal?>.Ok(tariff?.Total.Amount);
    }

    /// Today, as the till sees it.
    public DateOnly Today => DateOnly.FromDateTime(_clock.UtcNow.ToLocalTime());
}
