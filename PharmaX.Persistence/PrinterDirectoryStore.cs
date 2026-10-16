using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Abstractions;

namespace PharmaX.Persistence;

/// <summary>
/// Each computer's printers, one row per computer in the settings table
/// under a scope no till can have.
/// </summary>
/// <remarks>
/// The same arrangement as <see cref="PharmacySettingStore"/>: the key is
/// already (scope, key), so no migration is needed. The computer's name is
/// the key and its printers are the value, so a computer that reports
/// again replaces what it said before - a printer unplugged since drops
/// out - and the row's time is when it was last heard from.
/// </remarks>
public sealed class PrinterDirectoryStore : IPrinterDirectory
{
    public const string Scope = "PRINTERS";

    private readonly PharmaXDbContext _db;
    private readonly IClock _clock;

    public PrinterDirectoryStore(PharmaXDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    private sealed record Entry(string Name, string? ShareName);

    public async Task PublishAsync(string computer,
        IReadOnlyList<NetworkPrinter> printers, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(computer);
        ArgumentNullException.ThrowIfNull(printers);

        var key = computer.Trim().ToUpperInvariant();
        var value = JsonSerializer.Serialize(
            printers.Select(p => new Entry(p.Name, p.ShareName)));

        var row = await _db.WorkstationSettings
            .FirstOrDefaultAsync(s => s.WorkstationCode == Scope
                                      && s.Key == key, ct);

        if (row is null)
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
            row.Value = value;
            row.UpdatedAtUtc = _clock.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<NetworkPrinter>> ListAsync(
        CancellationToken ct = default)
    {
        var rows = await _db.WorkstationSettings
            .AsNoTracking()
            .Where(s => s.WorkstationCode == Scope)
            .ToListAsync(ct);

        return rows
            .SelectMany(row => Read(row.Value)
                .Select(e => new NetworkPrinter(row.Key, e.Name, e.ShareName,
                                                row.UpdatedAtUtc)))
            .OrderBy(p => p.Computer, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// One computer's list; nothing where it cannot be read, so one bad
    /// row does not hide every other computer's printers.
    private static IEnumerable<Entry> Read(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Array.Empty<Entry>();

        try
        {
            return JsonSerializer.Deserialize<List<Entry>>(value)?
                       .Where(e => !string.IsNullOrWhiteSpace(e.Name))
                   ?? new List<Entry>();
        }
        catch (JsonException)
        {
            return Array.Empty<Entry>();
        }
    }
}
