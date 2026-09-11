using PharmaX.Domain.Common;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Sales;

namespace PharmaX.Domain.Tills;

/// <summary>
/// What the operator counted, against what the system expected. Held per
/// tender because a cash shortfall and a card mismatch are different
/// problems with different remedies.
/// </summary>
public sealed class TillDeclaration : AuditableEntity
{
    private readonly List<DeclarationLine> _lines = new();
    private readonly List<DenominationCount> _counts = new();

    public Guid TillSessionId { get; private set; }
    public DateTime DeclaredAtUtc { get; private set; }
    public Guid DeclaredBy { get; private set; }
    public bool IsFinal { get; private set; }
    public string? VarianceReason { get; private set; }

    public IReadOnlyList<DeclarationLine> Lines => _lines;
    public IReadOnlyList<DenominationCount> Counts => _counts;

    public Money TotalDeclared => _lines.Aggregate(
        Money.Zero(), (sum, l) => sum + l.Declared);

    public Money TotalVariance => _lines.Aggregate(
        Money.Zero(), (sum, l) => sum + l.Variance);

    private TillDeclaration() { }

    public static TillDeclaration Create(Guid tillSessionId, Guid declaredBy,
                                         DateTime declaredAtUtc, bool isFinal)
    {
        if (tillSessionId == Guid.Empty)
            throw new DomainException(
                "A declaration must belong to a till session.");

        return new TillDeclaration
        {
            TillSessionId = tillSessionId,
            DeclaredBy = declaredBy,
            DeclaredAtUtc = declaredAtUtc,
            IsFinal = isFinal
        };
    }

    public void AddLine(TenderType tender, Money expected, Money declared) =>
        _lines.Add(new DeclarationLine(tender, expected, declared));

    public void CountDenomination(string key, int count, Money extended) =>
        _counts.Add(new DenominationCount(key, count, extended));

    /// <summary>
    /// A variance beyond the configured threshold cannot be closed away
    /// silently (POS-023).
    /// </summary>
    public bool RequiresReason(decimal thresholdAmount) =>
        Math.Abs(TotalVariance.Amount) > thresholdAmount;

    public void ExplainVariance(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("A variance reason cannot be blank.");

        VarianceReason = reason;
    }
}

public sealed record DeclarationLine(TenderType Tender, Money Expected,
                                     Money Declared)
{
    /// Positive is over, negative is short. Signed on purpose: an operator
    /// consistently over is as interesting as one consistently short.
    public Money Variance => Declared - Expected;
}

public sealed record DenominationCount(string Key, int Count, Money Extended);
