namespace PharmaX.Application.Legacy;

/// <summary>
/// Loads the data exported from the system PharmaX replaces - RSI
/// DispenseWare and Retailware - into this database.
/// </summary>
public interface ILegacyImporter
{
    /// <param name="folder">The folder the export scripts wrote to.</param>
    /// <param name="dryRun">
    /// Everything is read, written and checked inside a transaction that
    /// is then rolled back: the report is real, the database unchanged.
    /// </param>
    Task<LegacyImportReport> ImportAsync(string folder, bool dryRun,
        IProgress<string>? progress = null, CancellationToken ct = default);
}

/// <summary>What an import did, and whether its totals agree with the export's.</summary>
public sealed record LegacyImportReport(bool DryRun, bool Succeeded, string Text);

/// <summary>The history taken over from the old system, for reading.</summary>
public interface ILegacyHistory
{
    Task<IReadOnlyList<LegacyRecordDto>> SearchAsync(LegacyQuery query,
        CancellationToken ct = default);
}

/// <param name="Kind">Script, Sale, Receipt, Stock, Price, Override, Count, DayEnd, ZRead, or null for all.</param>
/// <param name="Text">Words to find in the description, number, product code or party.</param>
public sealed record LegacyQuery(string? Kind, DateOnly? From, DateOnly? To,
    string? Text, string? PatientFile = null, int Limit = 1000);

public sealed record LegacyRecordDto(string Kind, string? Number,
    DateTime OccurredAt, string? ProductCode, string? PatientFile,
    string? Party, string Description, decimal? Quantity, decimal? Amount,
    string? Operator);
