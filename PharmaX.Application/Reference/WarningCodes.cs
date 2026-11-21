using PharmaX.Domain.Reference;

namespace PharmaX.Application.Reference;

/// <summary>
/// Warning codes expand the same way dosage codes do: the code is what
/// is typed, the name is the sentence on the label.
/// </summary>
public static class WarningCodes
{
    public static IReadOnlyList<(string Code, string Meaning)> Standard { get; } = new[]
    {
        ("DROWSY", "May cause drowsiness. Do not drive or use machines."),
        ("AVOIDALC", "Avoid alcohol while taking this medicine."),
        ("EXT", "For external use only. Do not swallow."),
        ("FINISH", "Finish the course unless your doctor tells you to stop."),
        ("SHAKE", "Shake the bottle well before use."),
        ("FRIDGE", "Keep in the fridge. Do not freeze."),
        ("FOOD", "Take with or after food.")
    };

    public static async Task<IReadOnlyDictionary<string, string>> WarningCatalogueAsync(
        this IReferenceRepository reference, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reference);

        var listed = await reference.ListAsync(ReferenceKind.WarningCode, "",
            includeInactive: false, ct: ct);

        return DosageShorthand.Catalogue(listed.Select(c => (c.Code, c.Name)));
    }
}
