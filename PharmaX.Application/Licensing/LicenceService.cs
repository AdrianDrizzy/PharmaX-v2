using System.Globalization;
using System.Text.Json;
using PharmaX.Application.Abstractions;

namespace PharmaX.Application.Licensing;

public sealed record ActivationResult(bool Ok, string Message);

public interface ILicenceService
{
    /// <summary>Reads the membership and puts it into force.</summary>
    Task<LicenceSnapshot> RefreshAsync(CancellationToken ct = default);

    Task<Guid> GetPharmacyIdAsync(CancellationToken ct = default);

    Task<ActivationResult> ActivateAsync(string code, CancellationToken ct = default);
}

/// <summary>
/// This computer's last-known membership, so a till that cannot reach the
/// main computer keeps the membership it last saw instead of going read-only.
/// </summary>
public interface ILicenceCache
{
    (DateOnly? ValidUntil, DateOnly? LastSeen)? Read();

    void Write(DateOnly? validUntil, DateOnly lastSeen);
}

public sealed class LicenceService : ILicenceService
{
    private const string CodeKey = "licence.code";
    private const string PharmacyIdKey = "licence.pharmacy-id";
    private const string ValidUntilKey = "licence.valid-until";
    private const string LastSeenKey = "licence.last-seen";
    private const int TrialDays = 14;

    private readonly IPharmacySettings _settings;
    private readonly IClock _clock;
    private readonly ILicenceGate _gate;
    private readonly ILicenceCache _cache;
    private readonly Func<string, LicencePayload?> _verify;

    public LicenceService(IPharmacySettings settings, IClock clock, ILicenceGate gate,
                          ILicenceCache cache, Func<string, LicencePayload?>? verify = null)
    {
        _settings = settings;
        _clock = clock;
        _gate = gate;
        _cache = cache;
        _verify = verify ?? LicenceCode.Verify;
    }

    public async Task<LicenceSnapshot> RefreshAsync(CancellationToken ct = default)
    {
        var today = _clock.Today;
        DateOnly? validUntil, lastSeen;
        LicencePayload? payload;
        var offline = false;

        try
        {
            validUntil = Parse(await _settings.GetAsync(ValidUntilKey, ct));
            lastSeen = Parse(await _settings.GetAsync(LastSeenKey, ct));
            var code = await _settings.GetAsync(CodeKey, ct);
            payload = string.IsNullOrEmpty(code) ? null : _verify(code);

            if (validUntil is null)
            {
                // First start: a trial, so setting up is never blocked.
                validUntil = today.AddDays(TrialDays);
                await _settings.SetAsync(ValidUntilKey, Format(validUntil), ct);
            }

            if (lastSeen is null || today > lastSeen)
                await _settings.SetAsync(LastSeenKey, Format(today), ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // The main computer is out of reach: go by what this computer last saw.
            if (_cache.Read() is not { } cached) return _gate.Current;
            (validUntil, lastSeen) = cached;
            payload = null;
            offline = true;
        }

        var snapshot = new LicenceSnapshot(
            LicenceState.Evaluate(validUntil, today, lastSeen),
            validUntil, payload?.Plan ?? (offline ? _gate.Current.Plan : null),
            IsTrial: offline ? _gate.Current.IsTrial : payload is null);

        _cache.Write(validUntil, lastSeen is { } seen && seen > today ? seen : today);
        _gate.Update(snapshot);
        return snapshot;
    }

    public async Task<Guid> GetPharmacyIdAsync(CancellationToken ct = default)
    {
        if (Guid.TryParse(await _settings.GetAsync(PharmacyIdKey, ct), out var id))
            return id;

        id = Guid.NewGuid();
        await _settings.SetAsync(PharmacyIdKey, id.ToString(), ct);
        return id;
    }

    public async Task<ActivationResult> ActivateAsync(string code,
                                                      CancellationToken ct = default)
    {
        if (_verify(code ?? "") is not { } payload)
            return new(false, "That is not a valid PharmaX code. Paste the whole "
                              + "code exactly as you received it.");

        if (payload.PharmacyId != await GetPharmacyIdAsync(ct))
            return new(false, "This code was made for a different pharmacy.");

        if (payload.ValidUntil < _clock.Today)
            return new(false, "This code has already expired (it ran until "
                              + $"{LicenceState.Day(payload.ValidUntil)}).");

        var current = Parse(await _settings.GetAsync(ValidUntilKey, ct));
        var currentCode = await _settings.GetAsync(CodeKey, ct);

        // An older code never takes days away. A trial's end is not a membership.
        if (string.IsNullOrEmpty(currentCode) || current is null
            || payload.ValidUntil >= current)
        {
            await _settings.SetAsync(CodeKey, code, ct);
            await _settings.SetAsync(ValidUntilKey, Format(payload.ValidUntil), ct);
        }

        var snapshot = await RefreshAsync(ct);
        return new(true, $"Membership active until {LicenceState.Day(snapshot.ValidUntil)} "
                         + $"({snapshot.Plan}).");
    }

    private static DateOnly? Parse(string? text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var d) ? d : null;

    private static string? Format(DateOnly? day) =>
        day?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>The cache as a small file in C:\ProgramData\PharmaX.</summary>
public sealed class FileLicenceCache : ILicenceCache
{
    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "PharmaX", "licence.json");

    private sealed record Entry(DateOnly? ValidUntil, DateOnly? LastSeen);

    public (DateOnly? ValidUntil, DateOnly? LastSeen)? Read()
    {
        try
        {
            var e = JsonSerializer.Deserialize<Entry>(File.ReadAllText(Path));
            return e is null ? null : (e.ValidUntil, e.LastSeen);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or JsonException)
        {
            return null;
        }
    }

    public void Write(DateOnly? validUntil, DateOnly lastSeen)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(new Entry(validUntil, lastSeen)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only a convenience for going offline; the database is the record.
        }
    }
}
