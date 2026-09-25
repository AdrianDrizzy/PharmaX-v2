using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Abstractions;

namespace PharmaX.Persistence.Numbering;

public sealed class DocumentNumberBlock
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Series { get; init; } = "";
    public string WorkstationCode { get; init; } = "";
    public long RangeFrom { get; init; }
    public long RangeTo { get; init; }
    public long NextValue { get; set; }
}

/// <summary>
/// Hands out document numbers from this workstation's blocks.
/// </summary>
/// <remarks>
/// <para>
/// Each number is claimed with a single conditional update - "move this
/// block on from 42 to 43, if it is still at 42" - and only a claim that
/// changed a row is used. Two tills that share a till number, two copies
/// of PharmaX on one machine, or two screens on one till can all ask at
/// the same moment: one of them wins each number and the other goes round
/// again for the next. The read-then-save this replaces relied on an
/// in-process lock, which a second process never sees.
/// </para>
/// <para>
/// The claim runs inside whatever transaction the caller has open, so a
/// document that fails to save gives its number back rather than leaving
/// a gap, and it touches nothing else: the previous version saved the
/// whole context to move the counter, committing the caller's half-built
/// document along with it.
/// </para>
/// </remarks>
public sealed class DocumentNumberAllocator : IDocumentNumbering
{
    private const int Attempts = 20;

    private readonly PharmaXDbContext _db;
    private readonly PharmaXOptions _options;

    public DocumentNumberAllocator(PharmaXDbContext db, PharmaXOptions options)
    {
        _db = db;
        _options = options;
    }

    public async Task<string> NextAsync(string series,
                                        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(series);

        var prefix = DocumentSeries.PrefixOf(series);
        var till = _options.Workstation.TillNumber;

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var block = await _db.Set<DocumentNumberBlock>()
                .AsNoTracking()
                .Where(b => b.Series == series && b.WorkstationCode == till)
                .Where(b => b.NextValue <= b.RangeTo)
                .OrderBy(b => b.RangeFrom)
                .Select(b => new { b.Id, b.NextValue })
                .FirstOrDefaultAsync(ct)
                ?? throw new InvalidOperationException(
                    $"No document numbers left for {series} on till {till}. " +
                    "Reconnect to the host to obtain a new block.");

            var value = block.NextValue;

            var claimed = await _db.Set<DocumentNumberBlock>()
                .Where(b => b.Id == block.Id && b.NextValue == value)
                .ExecuteUpdateAsync(set => set.SetProperty(
                    b => b.NextValue, value + 1), ct);

            // Somebody else took this one between the read and the claim.
            if (claimed == 1) return Format(prefix, till, value);
        }

        throw new InvalidOperationException(
            $"Could not allocate a {series} number after {Attempts} tries: "
            + "too many tills are asking at once. Try again.");
    }

    /// <summary>
    /// R-002-014873 : the series' own prefix, the till, the sequence.
    /// </summary>
    public static string Format(string prefix, string till, long value) =>
        $"{prefix}-{till.PadLeft(3, '0')}-{value:D6}";

    public async Task<int> RemainingAsync(string series,
                                          CancellationToken ct = default)
    {
        var till = _options.Workstation.TillNumber;

        return (int)await _db.Set<DocumentNumberBlock>()
            .Where(b => b.Series == series && b.WorkstationCode == till)
            .Where(b => b.NextValue <= b.RangeTo)
            .SumAsync(b => b.RangeTo - b.NextValue + 1, ct);
    }
}
