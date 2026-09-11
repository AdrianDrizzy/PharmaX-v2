using PharmaX.Domain.Common;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Primitives;

namespace PharmaX.Domain.Inventory;

public enum StocktakeStatus { Counting, Posted, Cancelled }

/// <summary>
/// A physical count against what the system believed. The counterpart to
/// a goods receipt: together they are what make a stock figure something
/// anyone can trust.
/// </summary>
/// <remarks>
/// The expected quantity is snapshotted onto each line when the count is
/// opened, not read at posting time. Trading carries on while the shelf
/// is counted, so comparing a Tuesday count against Thursday's on-hand
/// would report every sale in between as a shortage.
/// </remarks>
public sealed class Stocktake : AuditableEntity
{
    private readonly List<StocktakeLine> _lines = new();

    public string StocktakeNumber { get; private set; } = "";
    public Guid BranchId { get; private set; }
    public DateTime OpenedAtUtc { get; private set; }
    public Guid OpenedBy { get; private set; }

    /// What was counted: "Full count", "Schedule 5 cupboard", a
    /// department name. Free text because a pharmacy counts by whatever
    /// division makes sense that week.
    public string Scope { get; private set; } = "";

    public StocktakeStatus Status { get; private set; } =
        StocktakeStatus.Counting;

    public DateTime? PostedAtUtc { get; private set; }
    public Guid? PostedBy { get; private set; }
    public string? VarianceReason { get; private set; }
    public string? CancelReason { get; private set; }

    public IReadOnlyList<StocktakeLine> Lines => _lines;

    public int CountedLines => _lines.Count(l => l.IsCounted);
    public int UncountedLines => _lines.Count(l => !l.IsCounted);

    /// <summary>
    /// Lines whose count differs from what was expected. The only part
    /// of a stocktake anyone actually reads.
    /// </summary>
    public IReadOnlyList<StocktakeLine> Variances =>
        _lines.Where(l => l.IsCounted && l.Variance != 0m).ToList();

    /// Signed: positive is more on the shelf than the system thought.
    public decimal NetVarianceQuantity =>
        _lines.Where(l => l.IsCounted).Sum(l => l.Variance);

    public Money NetVarianceValue => _lines
        .Where(l => l.IsCounted)
        .Aggregate(Money.Zero(), (sum, l) => sum + l.VarianceValue);

    /// <summary>
    /// What the count is worth against the pharmacy's books, which is
    /// the figure that reaches the accountant.
    /// </summary>
    public Money ShrinkageValue
    {
        get
        {
            var net = NetVarianceValue;
            return net.IsNegative ? net : Money.Zero(net.Currency);
        }
    }

    private Stocktake() { }

    public static Stocktake Open(string stocktakeNumber, Guid branchId,
        Guid openedBy, string scope, DateTime openedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(scope))
            throw new DomainException(
                "A stocktake needs a scope: what was counted has to be "
                + "recorded, or the variance means nothing.");

        return new Stocktake
        {
            StocktakeNumber = stocktakeNumber,
            BranchId = branchId,
            OpenedBy = openedBy,
            Scope = scope.Trim(),
            OpenedAtUtc = openedAtUtc
        };
    }

    /// <summary>
    /// Adds a line with the expected quantity as it stands now. Called
    /// only while the count is being set up.
    /// </summary>
    public StocktakeLine AddLine(StocktakeLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        EnsureCounting();
        line.AssignTo(Id, _lines.Count + 1);
        _lines.Add(line);
        return line;
    }

    public void RecordCount(int lineNo, decimal counted)
    {
        EnsureCounting();

        var line = _lines.SingleOrDefault(l => l.LineNo == lineNo)
                   ?? throw new DomainException($"Line {lineNo} not found.");

        line.Count(counted);
    }

    /// <summary>
    /// Clears a count so the line reads as uncounted again. A zero and
    /// an uncounted line are different answers: zero means the shelf was
    /// empty, uncounted means nobody looked.
    /// </summary>
    public void ClearCount(int lineNo)
    {
        EnsureCounting();

        var line = _lines.SingleOrDefault(l => l.LineNo == lineNo)
                   ?? throw new DomainException($"Line {lineNo} not found.");

        line.ClearCount();
    }

    public void ExplainVariance(string reason)
    {
        EnsureCounting();

        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("A variance reason cannot be blank.");

        VarianceReason = reason.Trim();
    }

    /// <summary>
    /// True where the net variance is large enough that posting without
    /// an explanation would be writing off stock nobody accounted for.
    /// </summary>
    public bool RequiresVarianceReason(decimal thresholdValue) =>
        Math.Abs(NetVarianceValue.Amount) > thresholdValue;

    /// <summary>
    /// Posts the count, which is the point at which the system's figures
    /// change to match the shelf. One-way: a mistake is corrected by an
    /// adjustment or a fresh count, never by unposting.
    /// </summary>
    public void Post(Guid postedBy, DateTime postedAtUtc,
                     decimal varianceThresholdValue)
    {
        EnsureCounting();

        if (_lines.Count == 0)
            throw new DomainException("An empty stocktake counts nothing.");

        // An uncounted line posted as zero writes off stock nobody
        // looked at. Refused rather than assumed, every time.
        if (UncountedLines > 0)
            throw new DomainException(
                $"{UncountedLines} line(s) have not been counted. Count "
                + "them, or remove them from this stocktake: posting an "
                + "uncounted line would write off stock nobody looked at.");

        if (RequiresVarianceReason(varianceThresholdValue)
            && string.IsNullOrWhiteSpace(VarianceReason))
            throw new DomainException(
                $"The net variance is {NetVarianceValue}, which needs a "
                + "written reason before it can be posted.");

        PostedBy = postedBy;
        PostedAtUtc = postedAtUtc;
        Status = StocktakeStatus.Posted;
    }

    public void Cancel(string reason)
    {
        EnsureCounting();

        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException(
                "Cancelling a stocktake requires a reason.");

        CancelReason = reason.Trim();
        Status = StocktakeStatus.Cancelled;
    }

    private void EnsureCounting()
    {
        if (Status != StocktakeStatus.Counting)
            throw new DomainException(
                $"This stocktake is {Status.ToString().ToLowerInvariant()} "
                + "and cannot be changed. Open a new count.");
    }
}

/// <summary>
/// One thing counted. A batch-tracked product gets a line per batch,
/// because that is what a person counting a shelf actually does: they
/// read the batch number off the box.
/// </summary>
public sealed class StocktakeLine : Entity
{
    public Guid StocktakeId { get; private set; }
    public int LineNo { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid? BatchId { get; private set; }
    public string Description { get; private set; } = "";
    public string? BatchNumber { get; private set; }
    public DateOnly? BatchExpiry { get; private set; }

    /// What the system believed when the count was opened.
    public decimal ExpectedQuantity { get; private set; }

    public decimal? CountedQuantity { get; private set; }
    public Money UnitCost { get; private set; } = Money.Zero();

    public bool IsCounted => CountedQuantity is not null;

    /// Signed: positive is more on the shelf than the system thought.
    public decimal Variance =>
        CountedQuantity is decimal counted
            ? counted - ExpectedQuantity
            : 0m;

    public Money VarianceValue => UnitCost * Variance;

    private StocktakeLine() { }

    public static StocktakeLine For(Guid productId, string description,
        decimal expectedQuantity, Money unitCost, Guid? batchId = null,
        string? batchNumber = null, DateOnly? batchExpiry = null) =>
        new()
        {
            ProductId = productId,
            Description = description,
            ExpectedQuantity = expectedQuantity,
            UnitCost = unitCost,
            BatchId = batchId,
            BatchNumber = batchNumber,
            BatchExpiry = batchExpiry
        };

    internal void AssignTo(Guid stocktakeId, int lineNo)
    {
        StocktakeId = stocktakeId;
        LineNo = lineNo;
    }

    internal void Count(decimal counted)
    {
        if (counted < 0m)
            throw new DomainException(
                "A count cannot be negative: a shelf holds nothing or "
                + "something.");

        CountedQuantity = Math.Round(counted, 3,
                                     MidpointRounding.AwayFromZero);
    }

    internal void ClearCount() => CountedQuantity = null;
}
