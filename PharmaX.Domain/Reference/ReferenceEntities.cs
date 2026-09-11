using PharmaX.Domain.Common;
using PharmaX.Domain.Exceptions;

namespace PharmaX.Domain.Reference;

/// <summary>
/// The behaviour every maintained list in PharmaX shares: a code the
/// staff type, a name they read, and a way of retiring an entry without
/// deleting it.
/// </summary>
/// <remarks>
/// Deactivated rather than deleted, for the same reason a product is:
/// a department removed outright takes every historical sale that
/// referenced it off the reports that have already been printed.
/// </remarks>
public abstract class ReferenceEntry : AggregateRoot
{
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";

    /// <summary>
    /// The one extra fact each list carries: a manufacturer's agent, a
    /// branch's address, a scheme's administrator. One field rather than
    /// a different shape per list, because a maintenance screen that
    /// holds three of these should not be three screens.
    /// </summary>
    public string? Detail { get; private set; }

    public bool IsActive { get; private set; } = true;

    protected static T Init<T>(T entry, string code, string name,
        string? detail) where T : ReferenceEntry
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("A code is required.");

        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("A name is required.");

        entry.Code = code.Trim().ToUpperInvariant();
        entry.Name = name.Trim();
        entry.Detail = Blank(detail);
        return entry;
    }

    public void Rename(string name, string? detail)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("A name is required.");

        Name = name.Trim();
        Detail = Blank(detail);
    }

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// What part of the shop an item belongs to - dispensary, front shop,
/// cosmetics. Sales analysis and stock reports already slice by it.
/// </summary>
public sealed class Department : ReferenceEntry
{
    private Department() { }

    public static Department Create(string code, string name,
        string? detail = null) =>
        Init(new Department(), code, name, detail);
}

/// <summary>
/// Who makes the product. Kept against the product so that a recall
/// notice naming a manufacturer can be answered with a list.
/// </summary>
public sealed class Manufacturer : ReferenceEntry
{
    private Manufacturer() { }

    public static Manufacturer Create(string code, string name,
        string? detail = null) =>
        Init(new Manufacturer(), code, name, detail);
}

/// <summary>
/// A trading location. Every sale already carries a branch identifier;
/// this is what gives that identifier a name a person can read.
/// </summary>
public sealed class Branch : ReferenceEntry
{
    private Branch() { }

    public static Branch Create(string code, string name,
        string? detail = null) =>
        Init(new Branch(), code, name, detail);
}

/// <summary>
/// A medical aid society. Tariffs price against one of these, which is
/// what makes a rated dispensing line rated.
/// </summary>
public sealed class MedicalAidScheme : ReferenceEntry
{
    private MedicalAidScheme() { }

    public static MedicalAidScheme Create(string code, string name,
        string? detail = null) =>
        Init(new MedicalAidScheme(), code, name, detail);
}
