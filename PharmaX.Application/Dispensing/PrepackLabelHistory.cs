using System.Text.Json;
using System.Text.Json.Serialization;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Dispensing.Dtos;

namespace PharmaX.Application.Dispensing;

/// <summary>
/// One pre-pack sticker the dispensary has printed, so it can be printed
/// again without being typed in a second time.
/// </summary>
public sealed record PrepackLabelDto(
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
    bool? Retired = null)
{
    /// The bench clock, not UTC, which is what the list shows.
    public DateTime PrintedAt =>
        DateTime.SpecifyKind(PrintedAtUtc, DateTimeKind.Utc).ToLocalTime();

    /// <summary>Still on the list to print again. A deactivated one stays as history.</summary>
    [JsonIgnore]
    public bool IsActive => Retired != true;

    [JsonIgnore]
    public string Active => IsActive ? "Yes" : "No";
}

/// <summary>
/// The pre-pack stickers printed so far, newest first, kept with the
/// pharmacy's other shared settings.
/// </summary>
public sealed class PrepackLabelHistory
{
    /// Older than this drops off the list. A bench does not look up a
    /// sticker from years ago by scrolling.
    public const int Most = 200;

    private readonly IPharmacySettings _settings;
    private readonly IClock _clock;

    public PrepackLabelHistory(IPharmacySettings settings, IClock clock)
    {
        _settings = settings;
        _clock = clock;
    }

    public async Task<IReadOnlyList<PrepackLabelDto>> ListAsync(
        CancellationToken ct = default)
    {
        var json = await _settings.GetAsync(PharmacySettingKey.PrepackLabels, ct);

        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<PrepackLabelDto>();

        try
        {
            return JsonSerializer.Deserialize<List<PrepackLabelDto>>(json)
                   ?? new List<PrepackLabelDto>();
        }
        catch (JsonException)
        {
            // A value that is not the list is treated as no list, so the
            // Labels screen still opens.
            return Array.Empty<PrepackLabelDto>();
        }
    }

    /// <summary>
    /// Adds the sticker just created. The list is rewritten whole.
    /// </summary>
    /// <remarks>
    /// shortcut: two tills saving in the same moment can drop one row.
    /// Pre-packs are made up at one bench.
    /// </remarks>
    public async Task RememberAsync(LabelDto sticker, int copies,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sticker);

        var list = (await ListAsync(ct)).ToList();

        list.Insert(0, new PrepackLabelDto(
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
            _clock.UtcNow));

        if (list.Count > Most)
            list.RemoveRange(Most, list.Count - Most);

        await SaveAsync(list, ct);
    }

    /// <summary>
    /// Takes a sticker off the list that is printed again. The row stays,
    /// so the bench can still see that it was made.
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
    /// Takes the sticker off the list for good: one made in error. The
    /// stock movement it went with, if any, is not touched.
    /// </summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var list = (await ListAsync(ct)).ToList();
        if (list.RemoveAll(row => row.Id == id) == 0) return;
        await SaveAsync(list, ct);
    }

    private Task SaveAsync(IReadOnlyList<PrepackLabelDto> list,
                           CancellationToken ct) =>
        _settings.SetAsync(PharmacySettingKey.PrepackLabels,
            JsonSerializer.Serialize(list), ct);
}

/// <summary>
/// The medicine line on a pre-pack sticker. A chosen supplier follows
/// the medicine in brackets: "Paracetamol 500mg (Afromed)".
/// </summary>
public static class PrepackMedicine
{
    public static string Line(string medicine, string? supplier)
    {
        ArgumentNullException.ThrowIfNull(medicine);

        var name = medicine.Trim();
        if (name.Length == 0 || string.IsNullOrWhiteSpace(supplier))
            return name;

        return $"{name} ({supplier.Trim()})";
    }

    /// <summary>
    /// A saved line that ends with " (supplier)" comes back as the medicine
    /// and that supplier. A name this pharmacy still lists keeps the
    /// spelling on the list. A line with no such ending stays as stored.
    /// </summary>
    public static (string Medicine, string? Supplier) Read(
        string saved, IEnumerable<string> suppliers)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(suppliers);

        var text = saved.Trim();
        var open = text.LastIndexOf(" (", StringComparison.Ordinal);

        if (open <= 0 || !text.EndsWith(')'))
            return (text, null);

        var tail = text[(open + 2)..^1].Trim();
        var medicine = text[..open].TrimEnd();

        if (medicine.Length == 0 || tail.Length == 0
            || tail.Contains('(') || tail.Contains(')'))
            return (text, null);

        var known = suppliers.FirstOrDefault(s =>
            string.Equals(s?.Trim(), tail, StringComparison.OrdinalIgnoreCase));

        // shortcut: a medicine whose own name ends in brackets is read as a
        // supplier, when typed names are stored apart from the line.
        return (medicine, (known ?? tail).Trim());
    }
}
