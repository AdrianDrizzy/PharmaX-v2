namespace PharmaX.Application.Abstractions;

/// <summary>How a backup is written.</summary>
public enum BackupFormat
{
    /// A zipped SQL dump, or the SQLite file when this computer is standalone.
    SqlZip,

    /// One Excel workbook per table, zipped.
    Excel,

    /// One CSV file per table, zipped. Excel opens these.
    Csv
}

/// <summary>
/// A copy of the whole database, kept on this computer to restore from.
/// </summary>
public interface IDatabaseBackup
{
    /// <summary>
    /// Backs the database up into the backups folder, keeping the most
    /// recent copies, and returns the file written.
    /// </summary>
    /// <param name="reason">Part of the file name: "manual", "nightly", "before-upgrade".</param>
    Task<string> BackUpAsync(string reason, BackupFormat format = BackupFormat.SqlZip,
        CancellationToken ct = default);

    /// <summary>
    /// Replaces this computer's database with a .sql file, or a zip that
    /// contains one. The shared server only.
    /// </summary>
    Task RestoreSqlAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Whether this computer holds the database, and so is the one that
    /// backs it up. A till using the main computer's database is not.
    /// </summary>
    bool HoldsTheDatabase { get; }
}
