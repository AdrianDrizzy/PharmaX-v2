using PharmaX.Domain.Tills;

namespace PharmaX.Application.Tills.Dtos;

public sealed record TillSessionDto(
    Guid Id, string TillNumber, string OpenedByName, DateTime OpenedAtUtc,
    decimal OpeningFloat, string Status, int XReadCount)
{
    public static TillSessionDto From(TillSession s, string openedByName)
    {
        ArgumentNullException.ThrowIfNull(s);

        return new TillSessionDto(
            s.Id, s.TillNumber, openedByName, s.OpenedAtUtc,
            s.OpeningFloat.Amount, s.Status.ToString(), s.XReadCount);
    }
}

/// <summary>
/// What the system believes should be in the drawer for one tender, and
/// how it got there. Split out per tender because a cash shortfall and a
/// card mismatch are different problems.
/// </summary>
public sealed record TenderExpectationDto(
    string TenderType, decimal Expected, int TransactionCount);

public sealed record VarianceLineDto(
    string TenderType, decimal Expected, decimal Declared, decimal Variance,
    bool RequiresReason);

public sealed record DenominationDto(string Key, decimal Value, bool IsNote,
                                     int SortOrder)
{
    public static DenominationDto From(Denomination d)
    {
        ArgumentNullException.ThrowIfNull(d);

        return new DenominationDto(d.Key, d.Value, d.IsNote, d.SortOrder);
    }
}

public sealed record XReadDto(
    Guid TillSessionId, DateTime ReadAtUtc, int ReadNumber,
    IReadOnlyList<TenderExpectationDto> Expectations, decimal GrandTotal);

public sealed record DeclarationDto(
    Guid TillSessionId, decimal TotalDeclared, decimal TotalVariance,
    IReadOnlyList<VarianceLineDto> Variances);
