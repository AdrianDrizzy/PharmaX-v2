using PharmaX.Domain.Common;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Sales;

namespace PharmaX.Domain.Patients;

public enum AccountEntryKind
{
    /// A sale put on the account. Increases what is owed.
    Charge = 1,

    /// Money received. Reduces it.
    Payment = 2,

    /// A correction, either way.
    Adjustment = 3,

    /// Given up on.
    WriteOff = 4
}

/// <summary>
/// One movement on a patient's account.
/// </summary>
/// <remarks>
/// Account.Balance is what the till checks against a credit limit, and
/// it has to be a single fast read. These are the history behind it: a
/// balance with no movements cannot be queried, disputed or explained,
/// and "you owe two hundred dollars" is not an answer anybody accepts
/// without a list.
///
/// The two are written together and only by AccountService, which is
/// what keeps them agreeing.
/// </remarks>
public sealed class AccountTransaction : AuditableEntity
{
    public Guid AccountId { get; private set; }
    public DateOnly OccurredOn { get; private set; }
    public AccountEntryKind Kind { get; private set; }

    /// <summary>
    /// Always positive. Which way it moves the balance is the kind's
    /// job.
    /// </summary>
    /// <remarks>
    /// The same reasoning as a journal line: a signed amount plus a
    /// direction gives two ways to say the same thing, and eventually
    /// they disagree.
    /// </remarks>
    public Money Amount { get; private set; } = Money.Zero();

    /// <summary>
    /// What this movement was, in the terms the patient would recognise:
    /// a receipt number, or the reference off their payment.
    /// </summary>
    public string? Reference { get; private set; }

    public string? Narration { get; private set; }

    /// How a payment was made. Null on every other kind of movement.
    public TenderType? Tender { get; private set; }

    /// <summary>
    /// The till session a payment was taken at, where it was.
    /// </summary>
    /// <remarks>
    /// Cash and EcoCash paid off an account land in a till's drawer or
    /// wallet like any sale, so the cash-up has to expect them. Left
    /// unrecorded, every account paid in cash showed the drawer over by
    /// exactly that amount (ADM-DEF-18).
    /// </remarks>
    public Guid? TillSessionId { get; private set; }

    /// True where this movement increases what the patient owes.
    public bool Increases => Kind is AccountEntryKind.Charge;

    /// The movement's effect on the balance, signed.
    public Money Effect => Increases ? Amount : Money.Zero() - Amount;

    private AccountTransaction() { }

    /// <summary>
    /// Money received off the account, and how it came in.
    /// </summary>
    public static AccountTransaction RecordPayment(Guid accountId,
        Money amount, DateOnly occurredOn, TenderType tender,
        Guid? tillSessionId, string? reference = null,
        string? narration = null)
    {
        if (tender.IsDeferred())
            throw new DomainException(
                $"A payment off an account cannot itself be made by "
                + $"{tender.DisplayName()}.");

        if (tender.IsCashLike() && tillSessionId is null)
            throw new DomainException(
                $"{tender.DisplayName()} is taken at an open till, so the "
                + "cash-up expects it.");

        var payment = Record(accountId, AccountEntryKind.Payment, amount,
                             occurredOn, reference, narration);

        payment.Tender = tender;
        payment.TillSessionId = tillSessionId;

        return payment;
    }

    public static AccountTransaction Record(Guid accountId,
        AccountEntryKind kind, Money amount, DateOnly occurredOn,
        string? reference = null, string? narration = null)
    {
        if (accountId == Guid.Empty)
            throw new DomainException(
                "An account movement must belong to an account.");

        if (amount.IsNegative)
            throw new DomainException(
                "An account movement is always positive; the kind says "
                + "which way it goes.");

        if (amount.IsZero)
            throw new DomainException(
                "A movement of nothing moves nothing.");

        return new AccountTransaction
        {
            AccountId = accountId,
            Kind = kind,
            Amount = amount,
            OccurredOn = occurredOn,
            Reference = reference,
            Narration = narration
        };
    }
}
