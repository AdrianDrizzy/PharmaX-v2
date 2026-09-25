using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Abstractions;

namespace PharmaX.Persistence;

/// <summary>
/// One row per setting in the workstation's own database.
/// </summary>
public sealed class WorkstationSetting
{
    /// <summary>
    /// Which till this setting belongs to.
    /// </summary>
    /// <remarks>
    /// Part of the key, not decoration. On a standalone workstation the
    /// database holds one machine's settings and this never varies, but
    /// a pharmacy running one shared database keeps every till's rows in
    /// the same table - and without this the receipt printer chosen at
    /// the dispensary would become the printer for both cashiers.
    /// </remarks>
    public string WorkstationCode { get; set; } = "";

    public string Key { get; set; } = "";
    public string? Value { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>
/// The pharmacy's own settings, kept in the same table as the tills'
/// under a scope no till can have.
/// </summary>
/// <remarks>
/// Till numbers are short digits, so "PHARMACY" can never collide with
/// one, and ClaimUnassignedAsync only ever adopts rows with a blank
/// scope, so it cannot take these for a single till. No migration is
/// needed: the key is already (scope, key).
/// </remarks>
public sealed class PharmacySettingStore : IPharmacySettings
{
    public const string Scope = "PHARMACY";

    private readonly PharmaXDbContext _db;
    private readonly IClock _clock;

    public PharmacySettingStore(PharmaXDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<string?> GetAsync(string key,
        CancellationToken ct = default)
    {
        var setting = await _db.WorkstationSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.WorkstationCode == Scope
                                      && s.Key == key, ct);

        return setting?.Value;
    }

    public async Task SetAsync(string key, string? value,
        CancellationToken ct = default)
    {
        var setting = await _db.WorkstationSettings
            .FirstOrDefaultAsync(s => s.WorkstationCode == Scope
                                      && s.Key == key, ct);

        if (setting is null)
        {
            _db.WorkstationSettings.Add(new WorkstationSetting
            {
                WorkstationCode = Scope,
                Key = key,
                Value = value,
                UpdatedAtUtc = _clock.UtcNow
            });
        }
        else
        {
            setting.Value = value;
            setting.UpdatedAtUtc = _clock.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }
}

public sealed class WorkstationSettingStore : IWorkstationSettings
{
    private readonly PharmaXDbContext _db;
    private readonly IClock _clock;
    private readonly string _workstation;

    public WorkstationSettingStore(PharmaXDbContext db, IClock clock,
                                   PharmaXOptions options)
    {
        _db = db;
        _clock = clock;
        _workstation = options.Workstation.TillNumber;
    }

    public async Task<string?> GetAsync(string key,
        CancellationToken ct = default)
    {
        var setting = await _db.WorkstationSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.WorkstationCode == _workstation
                                      && s.Key == key, ct);

        return setting?.Value;
    }

    /// <summary>
    /// Takes ownership of settings written before they were kept per
    /// workstation. Returns how many were claimed.
    /// </summary>
    /// <remarks>
    /// The migration that added the workstation to the key had no way to
    /// know which till it was running on, so it left existing rows with
    /// a blank one. On a workstation being upgraded those rows are this
    /// machine's by definition - it was the only machine using the file
    /// - so it claims them and the printer and logo chosen last week
    /// survive the upgrade. On a shared database there are none to
    /// claim, because every row was written with a till against it.
    /// </remarks>
    public async Task<int> ClaimUnassignedAsync(CancellationToken ct = default)
    {
        var orphans = await _db.WorkstationSettings
            .Where(s => s.WorkstationCode == "")
            .ToListAsync(ct);

        if (orphans.Count == 0) return 0;

        // Anything this workstation has already set for itself wins; the
        // orphan is then just a leftover and is dropped.
        var mine = await _db.WorkstationSettings
            .Where(s => s.WorkstationCode == _workstation)
            .Select(s => s.Key)
            .ToListAsync(ct);

        foreach (var orphan in orphans)
        {
            if (mine.Contains(orphan.Key, StringComparer.Ordinal))
            {
                _db.WorkstationSettings.Remove(orphan);
                continue;
            }

            // The key is being changed, so the row is replaced rather
            // than edited: EF cannot move a row between primary keys.
            _db.WorkstationSettings.Remove(orphan);

            _db.WorkstationSettings.Add(new WorkstationSetting
            {
                WorkstationCode = _workstation,
                Key = orphan.Key,
                Value = orphan.Value,
                UpdatedAtUtc = orphan.UpdatedAtUtc
            });
        }

        await _db.SaveChangesAsync(ct);
        return orphans.Count;
    }

    public async Task SetAsync(string key, string? value,
        CancellationToken ct = default)
    {
        var setting = await _db.WorkstationSettings
            .FirstOrDefaultAsync(s => s.WorkstationCode == _workstation
                                      && s.Key == key, ct);

        if (setting is null)
        {
            _db.WorkstationSettings.Add(new WorkstationSetting
            {
                WorkstationCode = _workstation,
                Key = key,
                Value = value,
                UpdatedAtUtc = _clock.UtcNow
            });
        }
        else
        {
            setting.Value = value;
            setting.UpdatedAtUtc = _clock.UtcNow;
        }

        // Saved here rather than left to a unit of work: a setting is
        // not part of any business transaction, and a printer choice
        // that only takes effect if a sale happens to commit would be
        // baffling.
        await _db.SaveChangesAsync(ct);
    }
}
