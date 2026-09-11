using PharmaX.Domain.Patients;

namespace PharmaX.Application.Patients.Dtos;

public sealed record PatientListItemDto(
    Guid Id, string FileNumber, string Surname, string FirstNames,
    string FullName, int? Age, string? Telephone, bool HasAllergies,
    bool IsActive)
{
    public static PatientListItemDto From(Patient p, DateOnly today,
                                          bool hasAllergies)
    {
        ArgumentNullException.ThrowIfNull(p);

        return new PatientListItemDto(
            p.Id, p.FileNumber, p.Surname, p.FirstNames, p.FullName,
            p.AgeOn(today), p.Telephone, hasAllergies, p.IsActive);
    }
}

public sealed record AllergyDto(string Substance, string Severity,
                                string? Reaction)
{
    public static AllergyDto From(Allergy a)
    {
        ArgumentNullException.ThrowIfNull(a);

        return new AllergyDto(a.Substance, a.Severity, a.Reaction);
    }
}

public sealed record PatientDto(
    Guid Id, string FileNumber, string Surname, string FirstNames,
    string? IdNumber, DateOnly? DateOfBirth, int? Age, string? Telephone,
    string? Email, bool IsActive, decimal? WeightKg, bool IsPregnant,
    bool IsBreastfeeding, string? ClinicalNotes,
    IReadOnlyList<AllergyDto> Allergies,
    IReadOnlyList<string> ChronicConditions)
{
    public static PatientDto From(Patient p, ClinicalProfile? profile,
                                  DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(p);

        return new PatientDto(
            p.Id, p.FileNumber, p.Surname, p.FirstNames, p.IdNumber,
            p.DateOfBirth, p.AgeOn(today), p.Telephone, p.Email,
            p.IsActive, profile?.WeightKg, profile?.IsPregnant ?? false,
            profile?.IsBreastfeeding ?? false, profile?.Notes,
            profile?.Allergies.Select(AllergyDto.From).ToList()
                ?? new List<AllergyDto>(),
            profile?.ChronicConditions.ToList() ?? new List<string>());
    }
}

/// <summary>
/// What the edit screen sends back. The file number is absent because
/// the system allocates it: a number the operator types is a number two
/// patients eventually share.
/// </summary>
public sealed record PatientEditDto(
    string Surname, string FirstNames, string? IdNumber,
    DateOnly? DateOfBirth, string? Telephone, string? Email,
    decimal? WeightKg, bool IsPregnant, bool IsBreastfeeding,
    string? ClinicalNotes, IReadOnlyList<AllergyDto> Allergies,
    IReadOnlyList<string> ChronicConditions);
