using PharmaX.Domain.Inventory;

namespace PharmaX.Application.Inventory.Dtos;

public sealed record StocktakeListItemDto(
    Guid Id, string StocktakeNumber, string Scope, DateTime OpenedAtUtc,
    string Status, int TotalLines, int CountedLines, int UncountedLines,
    decimal NetVarianceValue)
{
    public static StocktakeListItemDto From(Stocktake s)
    {
        ArgumentNullException.ThrowIfNull(s);

        return new StocktakeListItemDto(s.Id, s.StocktakeNumber, s.Scope,
            s.OpenedAtUtc, s.Status.ToString(), s.Lines.Count,
            s.CountedLines, s.UncountedLines, s.NetVarianceValue.Amount);
    }
}

public sealed record StocktakeLineDto(
    int LineNo, Guid ProductId, string Description, string? BatchNumber,
    DateOnly? BatchExpiry, decimal Expected, decimal? Counted,
    decimal Variance, decimal UnitCost, decimal VarianceValue,
    bool IsCounted)
{
    public static StocktakeLineDto From(StocktakeLine l)
    {
        ArgumentNullException.ThrowIfNull(l);

        return new StocktakeLineDto(l.LineNo, l.ProductId, l.Description,
            l.BatchNumber, l.BatchExpiry, l.ExpectedQuantity,
            l.CountedQuantity, l.Variance, l.UnitCost.Amount,
            l.VarianceValue.Amount, l.IsCounted);
    }
}

public sealed record StocktakeDto(
    Guid Id, string StocktakeNumber, string Scope, DateTime OpenedAtUtc,
    string Status, string? VarianceReason, int CountedLines,
    int UncountedLines, decimal NetVarianceQuantity,
    decimal NetVarianceValue, decimal ShrinkageValue,
    bool RequiresVarianceReason, IReadOnlyList<StocktakeLineDto> Lines)
{
    public static StocktakeDto From(Stocktake s, decimal threshold)
    {
        ArgumentNullException.ThrowIfNull(s);

        return new StocktakeDto(s.Id, s.StocktakeNumber, s.Scope,
            s.OpenedAtUtc, s.Status.ToString(), s.VarianceReason,
            s.CountedLines, s.UncountedLines, s.NetVarianceQuantity,
            s.NetVarianceValue.Amount, s.ShrinkageValue.Amount,
            s.RequiresVarianceReason(threshold),
            s.Lines.Select(StocktakeLineDto.From).ToList());
    }
}

/// <summary>
/// How much of the shelf a count covers. A full count is the annual
/// event; a partial count of one cupboard is what actually happens most
/// weeks, and it must not touch anything outside its scope.
/// </summary>
public sealed record StocktakeScopeDto(
    string Description, string? SearchTerm = null,
    bool BatchTrackedOnly = false, bool BelowReorderOnly = false);
