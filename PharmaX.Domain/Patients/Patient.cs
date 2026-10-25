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

    /// <summary>
    /// Where the patient lives, as they give it at the counter.
    /// </summary>
    /// <remarks>
    /// With the telephone number, what tells two patients of the same
    /// name apart when the list is searched, and what a delivery or a
    /// claim form is addressed to.
    /// </remarks>
    public string? Address { get; private set; }
    public Guid? AccountId { get; private set; }
    public Guid? ClinicalProfileId { get; private set; }
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// The scheme that pays most of this patient's bill, if any.
    /// </summary>
    /// <remarks>
    /// Kept here rather than on the clinical profile: which scheme
    /// somebody belongs to is a billing fact, and the whole point of
    /// separating the profile is that a screen showing a name and a
    /// membership number should not have to load a medical history.
    /// </remarks>
    public Guid? MedicalAidSchemeId { get; private set; }

    public string? MembershipNumber { get; private set; }

    /// <summary>
    /// Which member of the family this is, as the scheme numbers them -
    /// "00" for the principal member, "01" upwards for dependants.
    /// </summary>
    /// <remarks>
    /// A string rather than a number because schemes issue codes with
    /// leading zeroes and the odd letter, and a claim quoting "1" where
    /// the scheme expects "01" comes back rejected.
    /// </remarks>
    public string? DependantCode { get; private set; }

    /// True where there is a scheme to claim from.
    public bool IsOnMedicalAid =>
        MedicalAidSchemeId is not null
        && !string.IsNullOrWhiteSpace(MembershipNumber);

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

    /// <summary>
    /// Records who pays for this patient, or that nobody does.
    /// </summary>
    /// <remarks>
    /// Clearing the scheme clears the membership with it. A membership
    /// number left behind on a patient who has left their scheme is the
    /// kind of thing that gets claimed against months later and
    /// rejected.
    /// </remarks>
    public void SetMedicalAid(Guid? schemeId, string? membershipNumber,
                              string? dependantCode)
    {
        if (schemeId is null)
        {
            MedicalAidSchemeId = null;
            MembershipNumber = null;
            DependantCode = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(membershipNumber))
            throw new DomainException(
                "A medical aid membership needs a membership number.");

        MedicalAidSchemeId = schemeId;
        MembershipNumber = membershipNumber.Trim();
        DependantCode = string.IsNullOrWhiteSpace(dependantCode)
            ? null
            : dependantCode.Trim();
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

    /// Blank clears it; the address is kept as typed otherwise.
    public void SetAddress(string? address) =>
        Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim();

    public void LinkAccount(Guid accountId) => AccountId = accountId;
    public void LinkClinicalProfile(Guid profileId) => ClinicalProfileId = profileId;

    public void Deactivate() => IsActive = false;

    /// Back on the searches: a file deactivated by mistake, or a patient returned.
    public void Reactivate() => IsActive = true;
}
