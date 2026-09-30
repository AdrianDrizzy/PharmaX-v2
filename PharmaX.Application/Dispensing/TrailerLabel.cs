using PharmaX.Application.Dispensing.Dtos;

namespace PharmaX.Application.Dispensing;

/// <summary>
/// The trailer label: one more label after a script's own, listing
/// everything dispensed on it, for the bag or the script itself.
/// </summary>
/// <remarks>
/// The item labels each say how to take one medicine. The trailer is the
/// whole script at a glance - whose it is, what went into the bag and how
/// many of each, and who prescribed it - so the bag can be checked at the
/// counter against the script without opening every box.
/// </remarks>
/// <param name="FillNumber">
/// Which issue of the script this is: 1 for the first, 2 for the first
/// repeat, and so on. Printed as "(001)" after the patient.
/// </param>
public sealed record TrailerLabelDto(string ScriptNumber, string PatientName,
    int FillNumber, IReadOnlyList<TrailerItemDto> Items,
    string PrescriberName, DateOnly DispensedOn);

/// <summary>One medicine on the trailer, and how many were dispensed.</summary>
public sealed record TrailerItemDto(string Description, decimal Quantity);

public static class TrailerLabels
{
    /// <summary>
    /// The trailer for an issued dispensing.
    /// </summary>
    public static TrailerLabelDto For(Domain.Dispensing.Dispensing dispensing,
        Domain.Dispensing.Script? script, Domain.Patients.Patient? patient,
        DateOnly dispensedOn)
    {
        ArgumentNullException.ThrowIfNull(dispensing);

        return new TrailerLabelDto(
            ScriptNumber: script?.ScriptNumber ?? "",
            PatientName: patient?.FullName ?? "",
            FillNumber: dispensing.RepeatNumber + 1,
            Items: dispensing.Lines
                .OrderBy(l => l.LineNo)
                .Select(l => new TrailerItemDto(l.Description, l.Quantity))
                .ToList(),
            PrescriberName: script?.PrescriberName ?? "",
            DispensedOn: dispensedOn);
    }
}
