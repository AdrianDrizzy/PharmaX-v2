using PharmaX.Domain.Common;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Primitives;

namespace PharmaX.Domain.Tills;

public enum TillSessionStatus { Open, Suspended, Closed }

/// <summary>
/// The trading period of one till, from float in to cash-up. Nothing may be
/// sold without an open session (POS-013), which makes this aggregate a
/// prerequisite of the point of sale rather than an accessory to it.
/// </summary>
public sealed class TillSession : AuditableEntity
{
    public Guid BranchId { get; private set; }
    public Guid WorkstationId { get; private set; }
    public string TillNumber { get; private set; } = "";
    public Guid OpenedBy { get; private set; }
    public DateTime OpenedAtUtc { get; private set; }
    public Money OpeningFloat { get; private set; } = Money.Zero();

    public Guid? ClosedBy { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public Money? ClosingDeclared { get; private set; }
    public string? ClosingVarianceReason { get; private set; }

    public TillSessionStatus Status { get; private set; } = TillSessionStatus.Open;

    /// Count of interim X-reads. An X-read never resets a total; only the
    /// Z-read at close does, and only once.
    public int XReadCount { get; private set; }

    public bool IsOpen => Status == TillSessionStatus.Open;

    private TillSession() { }

    public static TillSession Open(Guid branchId, Guid workstationId,
        string tillNumber, Guid openedBy, Money openingFloat,
        DateTime openedAtUtc)
    {
        if (openingFloat.IsNegative)
            throw new DomainException("An opening float cannot be negative.");

        return new TillSession
        {
            BranchId = branchId,
            WorkstationId = workstationId,
            TillNumber = tillNumber,
            OpenedBy = openedBy,
            OpeningFloat = openingFloat,
            OpenedAtUtc = openedAtUtc
        };
    }

    public void RecordXRead()
    {
        EnsureOpen();
        XReadCount++;
    }

    /// <summary>
    /// Suspends trading without closing the session, so an operator can
    /// leave the till without a cash-up they would only have to reverse.
    /// </summary>
    public void Suspend()
    {
        EnsureOpen();
        Status = TillSessionStatus.Suspended;
    }

    public void Resume()
    {
        if (Status != TillSessionStatus.Suspended)
            throw new DomainException("Only a suspended session can resume.");

        Status = TillSessionStatus.Open;
    }

    public void Close(Guid closedBy, Money declared, DateTime closedAtUtc,
                      string? varianceReason)
    {
        if (Status == TillSessionStatus.Closed)
            throw new DomainException(
                "This session is already closed. A closed session is never " +
                "reopened; open a new one.");

        ClosedBy = closedBy;
        ClosedAtUtc = closedAtUtc;
        ClosingDeclared = declared;
        ClosingVarianceReason = varianceReason;
        Status = TillSessionStatus.Closed;
    }

    private void EnsureOpen()
    {
        if (Status != TillSessionStatus.Open)
            throw new DomainException(
                $"The till session is {Status.ToString().ToLowerInvariant()}.");
    }
}
