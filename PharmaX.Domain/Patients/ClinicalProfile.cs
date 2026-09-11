using PharmaX.Domain.Common;

namespace PharmaX.Domain.Patients;

/// <summary>
/// What dispensing needs to know before it hands something over: allergies,
/// chronic conditions and current medication. Separate from Patient so that
/// a till screen showing a name never loads a medical history.
/// </summary>
public sealed class ClinicalProfile : AuditableEntity
{
    private readonly List<Allergy> _allergies = new();
    private readonly List<string> _chronicConditions = new();

    public Guid PatientId { get; private set; }
    public decimal? WeightKg { get; private set; }
    public bool IsPregnant { get; private set; }
    public bool IsBreastfeeding { get; private set; }
    public string? Notes { get; private set; }

    public IReadOnlyList<Allergy> Allergies => _allergies;
    public IReadOnlyList<string> ChronicConditions => _chronicConditions;

    private ClinicalProfile() { }

    public static ClinicalProfile For(Guid patientId) =>
        new() { PatientId = patientId };

    public void RecordAllergy(string substance, string severity,
                              string? reaction = null) =>
        _allergies.Add(Allergy.Create(Id, substance, severity, reaction));

    public void RecordChronicCondition(string condition)
    {
        if (!_chronicConditions.Contains(condition, StringComparer.OrdinalIgnoreCase))
            _chronicConditions.Add(condition);
    }

    /// <summary>
    /// Replaces the recorded allergies wholesale. An edit screen shows
    /// the list and sends back what it should now be, and reconciling
    /// additions against removals in the aggregate would be guesswork.
    /// </summary>
    public void ReplaceAllergies(
        IEnumerable<(string Substance, string Severity, string? Reaction)> allergies)
    {
        ArgumentNullException.ThrowIfNull(allergies);

        _allergies.Clear();

        foreach (var (substance, severity, reaction) in allergies)
        {
            if (string.IsNullOrWhiteSpace(substance)) continue;

            RecordAllergy(substance.Trim(), severity, reaction);
        }
    }

    public void ReplaceChronicConditions(IEnumerable<string> conditions)
    {
        ArgumentNullException.ThrowIfNull(conditions);

        _chronicConditions.Clear();

        foreach (var condition in conditions
                     .Where(c => !string.IsNullOrWhiteSpace(c)))
            RecordChronicCondition(condition.Trim());
    }

    public void UpdateNotes(string? notes) => Notes = notes;

    public void UpdateVitals(decimal? weightKg, bool isPregnant,
                             bool isBreastfeeding)
    {
        WeightKg = weightKg;
        IsPregnant = isPregnant;
        IsBreastfeeding = isBreastfeeding;
    }

    public bool IsAllergicTo(string substance) =>
        _allergies.Exists(a =>
            a.Substance.Contains(substance, StringComparison.OrdinalIgnoreCase)
            || substance.Contains(a.Substance, StringComparison.OrdinalIgnoreCase));
}

public sealed class Allergy : Entity
{
    public Guid ClinicalProfileId { get; private set; }
    public string Substance { get; private set; } = "";

    /// Mild, Moderate or Severe. Drives whether the warning can be
    /// acknowledged by an assistant or needs a pharmacist.
    public string Severity { get; private set; } = "Moderate";

    public string? Reaction { get; private set; }

    private Allergy() { }

    public static Allergy Create(Guid clinicalProfileId, string substance,
                                 string severity, string? reaction) =>
        new()
        {
            ClinicalProfileId = clinicalProfileId,
            Substance = substance,
            Severity = severity,
            Reaction = reaction
        };

    public bool IsSevere =>
        string.Equals(Severity, "Severe", StringComparison.OrdinalIgnoreCase);
}
