using PharmaX.Domain.Reference;

namespace PharmaX.Application.Reference;

/// <summary>
/// Which maintained list a screen is working on. One enum rather than
/// four services, because the four lists differ only in what they are
/// called and what the extra column means.
/// </summary>
public enum ReferenceKind
{
    Department,
    Manufacturer,
    Branch,
    MedicalAidScheme
}

/// <summary>
/// The wording each list uses. Kept beside the kind so the screen, the
/// menu and the error messages cannot drift apart.
/// </summary>
public sealed record ReferenceLabels(string Plural, string Singular,
                                     string DetailCaption)
{
    public static ReferenceLabels For(ReferenceKind kind) => kind switch
    {
        ReferenceKind.Department =>
            new("Departments", "department", "Notes"),
        ReferenceKind.Manufacturer =>
            new("Manufacturers", "manufacturer", "Agent or contact"),
        ReferenceKind.Branch =>
            new("Branches", "branch", "Address"),
        ReferenceKind.MedicalAidScheme =>
            new("Medical aid societies", "society", "Administrator"),
        _ => new("Items", "item", "Detail")
    };
}

public sealed record ReferenceItemDto(Guid Id, string Code, string Name,
                                      string? Detail, bool IsActive)
{
    public static ReferenceItemDto From(ReferenceEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new ReferenceItemDto(entry.Id, entry.Code, entry.Name,
                                    entry.Detail, entry.IsActive);
    }
}

/// <summary>
/// What an edit screen sends back. No identifier and no active flag:
/// which row is being edited is the caller's business, and retiring one
/// is a separate decision from renaming it.
/// </summary>
public sealed record ReferenceEditDto(string Code, string Name,
                                      string? Detail);

public sealed record TariffListItemDto(Guid Id, string SchemeCode,
    string SchemeName, string ProductCode, string ProductDescription,
    decimal Price, decimal DispensingFee, decimal Total,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsActive);

public sealed record TariffEditDto(Guid SchemeId, Guid ProductId,
    decimal Price, decimal DispensingFee, DateOnly EffectiveFrom,
    DateOnly? EffectiveTo);
