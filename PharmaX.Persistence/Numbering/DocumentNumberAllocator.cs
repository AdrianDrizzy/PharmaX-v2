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

public sealed class DocumentNumberAllocator : IDocumentNumbering
{
    private readonly PharmaXDbContext _db;
    private readonly PharmaXOptions _options;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public DocumentNumberAllocator(PharmaXDbContext db, PharmaXOptions options)
    {
        _db = db;
        _options = options;
    }

    public async Task<string> NextAsync(string series,
                                        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(series);

        await Gate.WaitAsync(ct);
        try
        {
            var till = _options.Workstation.TillNumber;

            var block = await _db.Set<DocumentNumberBlock>()
                .Where(b => b.Series == series && b.WorkstationCode == till)
                .Where(b => b.NextValue <= b.RangeTo)
                .OrderBy(b => b.RangeFrom)
                .FirstOrDefaultAsync(ct)
                ?? throw new InvalidOperationException(
                    $"No document numbers left for {series} on till {till}. " +
                    "Reconnect to the host to obtain a new block.");

            var value = block.NextValue;
            block.NextValue = value + 1;
            await _db.SaveChangesAsync(ct);

            // R-002-014873 : series, till, sequence
            return $"{series[..1]}-{till.PadLeft(3, '0')}-{value:D6}";
        }
        finally
        {
            Gate.Release();
        }
    }

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
