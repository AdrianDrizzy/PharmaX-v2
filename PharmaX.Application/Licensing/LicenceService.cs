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
            lastSeen = Parse(await _settings.GetAsync(LastSeenKey, ct));
            payload = await StoredCodeAsync(ct);

            if (payload is not null)
            {
                // The signed date decides, never a row anyone could edit.
                validUntil = payload.ValidUntil;
            }
            else
            {
                // No code: a trial, so setting up is never blocked.
                var trialEnd = Parse(await _settings.GetAsync(ValidUntilKey, ct));
                if (trialEnd is null)
                {
                    trialEnd = today.AddDays(TrialDays);
                    await _settings.SetAsync(ValidUntilKey, Format(trialEnd), ct);
                }

                // shortcut: deleting this row restarts the trial; bound it by the
                // database's first sale if that ever proves to be abused.
                var longest = today.AddDays(TrialDays);
                validUntil = trialEnd > longest ? longest : trialEnd;
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

        // An older code never takes days away.
        if (await StoredCodeAsync(ct) is not { } stored || payload.ValidUntil >= stored.ValidUntil)
            await _settings.SetAsync(CodeKey, code, ct);

        // A vendor's code outranks a computer whose clock once ran ahead.
        await _settings.SetAsync(LastSeenKey, Format(_clock.Today), ct);

        var snapshot = await RefreshAsync(ct);
        return new(true, $"Membership active {LicenceState.Runs(snapshot.Plan, snapshot.ValidUntil)} "
                         + $"({snapshot.Plan}).");
    }

    /// The stored code, if it is genuine and made for this pharmacy.
    private async Task<LicencePayload?> StoredCodeAsync(CancellationToken ct)
    {
        var code = await _settings.GetAsync(CodeKey, ct);
        return string.IsNullOrEmpty(code) || _verify(code) is not { } payload
               || !Guid.TryParse(await _settings.GetAsync(PharmacyIdKey, ct), out var mine)
               || payload.PharmacyId != mine
            ? null
            : payload;
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
