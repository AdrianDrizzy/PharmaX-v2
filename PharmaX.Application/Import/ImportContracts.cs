namespace PharmaX.Application.Import;

/// <summary>
/// Loads another system's data into this database: products, stock,
/// patients, prescribers, suppliers and the trading history behind them,
/// each into the table PharmaX keeps it in.
/// </summary>
/// <remarks>
/// Not tied to any one system. Every file it is given is read for its
/// tables, and each table is recognised by its columns - a product code
/// and a name, a receipt number and a line total - not by what the file
/// happens to be called.
/// </remarks>
public interface IDataImporter
{
    /// <param name="paths">
    /// Files to read, or folders standing for every file in them.
    /// </param>
    /// <param name="dryRun">
    /// Everything is read, written and checked inside a transaction that
    /// is then rolled back: the report is real, the database unchanged.
    /// </param>
    Task<ImportResult> ImportAsync(IReadOnlyList<string> paths, bool dryRun,
        IProgress<string>? progress = null, CancellationToken ct = default);
}

/// <summary>What an import did, and whether its totals agree with the files'.</summary>
public sealed record ImportResult(bool DryRun, bool Succeeded, string Text);

/// <summary>The kinds of file an import reads.</summary>
public static class ImportFiles
{
    public static IReadOnlyList<string> Extensions { get; } =
        new[] { ".csv", ".tsv", ".tab", ".txt", ".xlsx", ".xlsm", ".json" };

    /// For an open-file dialog.
    public static string DialogFilter =>
        "Data files (CSV, text, Excel, JSON)|"
        + string.Join(";", Extensions.Select(e => "*" + e))
        + "|All files (*.*)|*.*";
}
