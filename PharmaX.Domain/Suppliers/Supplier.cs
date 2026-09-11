using PharmaX.Domain.Common;
using PharmaX.Domain.Exceptions;

namespace PharmaX.Domain.Suppliers;

public sealed class Supplier : AuditableEntity
{
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string? ContactName { get; private set; }
    public string? Telephone { get; private set; }
    public string? Email { get; private set; }
    public string? AccountNumber { get; private set; }
    public int TermsDays { get; private set; } = 30;
    public int LeadTimeDays { get; private set; } = 2;
    public bool IsActive { get; private set; } = true;

    private Supplier() { }

    public static Supplier Create(string code, string name,
                                  int termsDays = 30, int leadTimeDays = 2)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("A supplier requires a code.");

        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("A supplier requires a name.");

        return new Supplier
        {
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            TermsDays = termsDays,
            LeadTimeDays = leadTimeDays
        };
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("A supplier requires a name.");

        Name = name.Trim();
    }

    /// The account the pharmacy holds with the supplier, quoted on
    /// every order. Optional: a cash supplier has none.
    public void SetAccountNumber(string? accountNumber) =>
        AccountNumber = accountNumber?.Trim();

    public void Reactivate() => IsActive = true;

    public void UpdateContact(string? contactName, string? telephone,
                              string? email)
    {
        ContactName = contactName;
        Telephone = telephone;
        Email = email;
    }

    public void SetTerms(int termsDays, int leadTimeDays)
    {
        if (termsDays < 0 || leadTimeDays < 0)
            throw new DomainException("Terms and lead time cannot be negative.");

        TermsDays = termsDays;
        LeadTimeDays = leadTimeDays;
    }

    public void Deactivate() => IsActive = false;
}
