using PharmaX.Domain.Common;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Primitives;

namespace PharmaX.Domain.Patients;

/// <summary>
/// A running balance the pharmacy carries for a patient or a company.
/// A credit limit that can be exceeded silently is not a credit limit, so
/// Charge refuses rather than warning.
/// </summary>
public sealed class Account : AuditableEntity
{
    public string AccountNumber { get; private set; } = "";
    public string Name { get; private set; } = "";
    public Guid? PatientId { get; private set; }
    public Money Balance { get; private set; } = Money.Zero();
    public Money CreditLimit { get; private set; } = Money.Zero();
    public bool IsOnHold { get; private set; }
    public string? HoldReason { get; private set; }
    public int TermsDays { get; private set; } = 30;

    public Money Available => CreditLimit - Balance;

    private Account() { }

    public static Account Open(string accountNumber, string name,
        Money creditLimit, Guid? patientId = null, int termsDays = 30) =>
        new()
        {
            AccountNumber = accountNumber,
            Name = name,
            CreditLimit = creditLimit,
            PatientId = patientId,
            TermsDays = termsDays
        };

    public void Charge(Money amount)
    {
        if (IsOnHold)
            throw new DomainException(
                $"Account {AccountNumber} is on hold: {HoldReason}");

        var proposed = Balance + amount;

        if (proposed.Amount > CreditLimit.Amount)
            throw new DomainException(
                $"Charging {amount} would take account {AccountNumber} " +
                $"past its credit limit of {CreditLimit}.");

        Balance = proposed;
    }

    public void Credit(Money amount) => Balance -= amount;

    public void ChangeCreditLimit(Money limit)
    {
        if (limit.IsNegative)
            throw new DomainException("A credit limit cannot be negative.");

        CreditLimit = limit;
    }

    public void PlaceOnHold(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("Placing an account on hold needs a reason.");

        IsOnHold = true;
        HoldReason = reason;
    }

    public void ReleaseHold()
    {
        IsOnHold = false;
        HoldReason = null;
    }
}
