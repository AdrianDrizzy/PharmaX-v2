namespace PharmaX.Application.Abstractions;

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
    Task<string> BackUpAsync(string reason, CancellationToken ct = default);

    /// <summary>
    /// Whether this computer holds the database, and so is the one that
    /// backs it up. A till using the main computer's database is not.
    /// </summary>
    bool HoldsTheDatabase { get; }
}
