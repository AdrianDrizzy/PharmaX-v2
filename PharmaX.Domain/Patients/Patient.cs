using PharmaX.Domain.Common;
using PharmaX.Domain.Exceptions;

namespace PharmaX.Domain.Patients;

/// <summary>
/// A person the pharmacy dispenses to. Holds identity and contact details;
/// the clinical picture that drives dispensing warnings lives on
/// ClinicalProfile, so that a screen showing a name and a phone number does
/// not have to load a medical history it has no right to.
/// </summary>
public sealed class Patient : AuditableEntity
{
    public string FileNumber { get; private set; } = "";
    public string Surname { get; private set; } = "";
    public string FirstNames { get; private set; } = "";
    public string? IdNumber { get; private set; }
    public DateOnly? DateOfBirth { get; private set; }
    public string? Telephone { get; private set; }
    public string? Email { get; private set; }
    public Guid? AccountId { get; private set; }
    public Guid? ClinicalProfileId { get; private set; }
    public bool IsActive { get; private set; } = true;

    public string FullName => $"{FirstNames} {Surname}".Trim();

    private Patient() { }

    public static Patient Create(string fileNumber, string surname,
        string firstNames, DateOnly? dateOfBirth = null,
        string? idNumber = null)
    {
        if (string.IsNullOrWhiteSpace(surname))
            throw new DomainException("A patient requires a surname.");

        return new Patient
        {
            FileNumber = fileNumber,
            Surname = surname.Trim(),
            FirstNames = firstNames.Trim(),
            DateOfBirth = dateOfBirth,
            IdNumber = idNumber
        };
    }

    public int? AgeOn(DateOnly today)
    {
        if (DateOfBirth is null) return null;

        var age = today.Year - DateOfBirth.Value.Year;
        if (DateOfBirth.Value > today.AddYears(-age)) age--;
        return age;
    }

    /// <summary>
    /// Corrects the identity details. The file number is not among them:
    /// it is on every script and every account line the patient has, so
    /// it is allocated once and never edited.
    /// </summary>
    public void Amend(string surname, string firstNames, string? idNumber,
                      DateOnly? dateOfBirth)
    {
        if (string.IsNullOrWhiteSpace(surname))
            throw new DomainException("A patient requires a surname.");

        Surname = surname.Trim();
        FirstNames = firstNames.Trim();
        IdNumber = idNumber;
        DateOfBirth = dateOfBirth;
    }

    public void UpdateContact(string? telephone, string? email)
    {
        Telephone = telephone;
        Email = email;
    }

    public void LinkAccount(Guid accountId) => AccountId = accountId;
    public void LinkClinicalProfile(Guid profileId) => ClinicalProfileId = profileId;

    public void Deactivate() => IsActive = false;
}
