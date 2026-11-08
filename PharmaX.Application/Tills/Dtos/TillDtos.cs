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

    /// Opened on a day before <paramref name="today"/>, by this computer's
    /// clock: still open, so everything sold now goes into that day's shift.
    public bool OpenedBefore(DateOnly today) =>
        DateOnly.FromDateTime(OpenedAtUtc.ToLocalTime()) < today;
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

/// <summary>
/// Money taken out of a drawer during its shift, as the cash-up, the slip
/// and the Z-read list it. A voided one is listed and counts for nothing.
/// </summary>
public sealed record PayoutDto(Guid Id, Guid TillSessionId, PayoutKind Kind,
    decimal Amount, string Note, DateTime PaidAtLocal, string PaidByName,
    bool IsVoided = false, string? VoidReason = null)
{
    public string KindName => Kind.DisplayName();

    public static PayoutDto From(TillPayout p, string paidByName)
    {
        ArgumentNullException.ThrowIfNull(p);

        return new PayoutDto(p.Id, p.TillSessionId, p.Kind, p.Amount.Amount,
            p.Note, p.PaidAtUtc.ToLocalTime(), paidByName, p.IsVoided,
            p.VoidReason);
    }

    /// What counts against the drawer: the payouts not voided.
    public static decimal Total(IEnumerable<PayoutDto>? payouts) =>
        payouts?.Where(p => !p.IsVoided).Sum(p => p.Amount) ?? 0m;
}
