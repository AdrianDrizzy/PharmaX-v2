using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Abstractions;

namespace PharmaX.Persistence;

/// <summary>
/// One row per setting in the workstation's own database.
/// </summary>
public sealed class WorkstationSetting
{
    public string Key { get; set; } = "";
    public string? Value { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class WorkstationSettingStore : IWorkstationSettings
{
    private readonly PharmaXDbContext _db;
    private readonly IClock _clock;

    public WorkstationSettingStore(PharmaXDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<string?> GetAsync(string key,
        CancellationToken ct = default)
    {
        var setting = await _db.WorkstationSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key, ct);

        return setting?.Value;
    }

    public async Task SetAsync(string key, string? value,
        CancellationToken ct = default)
    {
        var setting = await _db.WorkstationSettings
            .FirstOrDefaultAsync(s => s.Key == key, ct);

        if (setting is null)
        {
            _db.WorkstationSettings.Add(new WorkstationSetting
            {
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
