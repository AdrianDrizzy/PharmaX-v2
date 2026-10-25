using System.Text.Json;
using System.Text.Json.Serialization;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Dispensing.Dtos;

namespace PharmaX.Application.Dispensing;

/// <summary>
/// One template label an administrator has printed, so it can be printed
/// again, and so a deactivated one is still on the list.
/// </summary>
public sealed record TemplateLabelDto(
    Guid Id,
    string Medicine,
    decimal Quantity,
    string Directions,
    string? Warning,
    DateOnly? Expiry,
    string? BatchNumber,
    decimal? Price,
    DateOnly PackedOn,
    int Copies,
    DateTime PrintedAtUtc,
    LabelOmissions Omit,
    bool? Retired = null)
{
    public DateTime PrintedAt =>
        DateTime.SpecifyKind(PrintedAtUtc, DateTimeKind.Utc).ToLocalTime();

    [JsonIgnore]
    public bool IsActive => Retired != true;

    [JsonIgnore]
    public string Active => IsActive ? "Yes" : "No";

    /// <summary>The sticker, with the lines the administrator left off.</summary>
    public LabelDto Sticker() => new(
        PatientName: "",
        ProductDescription: Medicine,
        Quantity: Quantity,
        Directions: Directions,
        BatchNumber: BatchNumber,
        Expiry: Expiry,
        PharmacistName: "",
        ScriptNumber: "Template",
        DispensedOn: PackedOn,
        Cautions: new[] { Warning ?? "" },
        Price: Price,
        Omit: Omit ?? new LabelOmissions());
}

/// <summary>
/// Template labels printed so far, newest first. The administrator's list,
/// kept with the pharmacy's other shared settings.
/// </summary>
public sealed class TemplateLabelHistory
{
    public const int Most = 200;

    private readonly IPharmacySettings _settings;
    private readonly IClock _clock;

    public TemplateLabelHistory(IPharmacySettings settings, IClock clock)
    {
        _settings = settings;
        _clock = clock;
    }

    public async Task<IReadOnlyList<TemplateLabelDto>> ListAsync(
        CancellationToken ct = default)
    {
        var json = await _settings.GetAsync(PharmacySettingKey.TemplateLabels, ct);

        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<TemplateLabelDto>();

        try
        {
            return JsonSerializer.Deserialize<List<TemplateLabelDto>>(json)
                   ?? new List<TemplateLabelDto>();
        }
        catch (JsonException)
        {
            return Array.Empty<TemplateLabelDto>();
        }
    }

    /// <remarks>
    /// shortcut: two tills saving in the same moment can drop one row.
    /// Template labels are made up by one administrator.
    /// </remarks>
    public async Task RememberAsync(LabelDto sticker, int copies,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sticker);

        var list = (await ListAsync(ct)).ToList();

        list.Insert(0, new TemplateLabelDto(
            Guid.NewGuid(),
            sticker.ProductDescription,
            sticker.Quantity,
            sticker.Directions,
            sticker.Cautions.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)),
            sticker.Expiry,
            sticker.BatchNumber,
            sticker.Price,
            sticker.DispensedOn,
            Math.Max(1, copies),
            _clock.UtcNow,
            sticker.Omit ?? new LabelOmissions()));

        if (list.Count > Most)
            list.RemoveRange(Most, list.Count - Most);

        await SaveAsync(list, ct);
    }

    /// <summary>
    /// Leaves the row on the list and stops it being the one to print again.
    /// </summary>
    public async Task DeactivateAsync(Guid id, CancellationToken ct = default)
    {
        var list = (await ListAsync(ct)).ToList();
        var found = list.FindIndex(row => row.Id == id);

        if (found < 0) return;

        list[found] = list[found] with { Retired = true };
        await SaveAsync(list, ct);
    }

    /// <summary>
    /// Takes the row off the list for good. Nothing else points at a
    /// template label, so nothing is left dangling.
    /// </summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var list = (await ListAsync(ct)).ToList();
        if (list.RemoveAll(row => row.Id == id) == 0) return;
        await SaveAsync(list, ct);
    }

    private Task SaveAsync(IReadOnlyList<TemplateLabelDto> list,
                           CancellationToken ct) =>
        _settings.SetAsync(PharmacySettingKey.TemplateLabels,
            JsonSerializer.Serialize(list), ct);
}
