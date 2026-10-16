using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using PharmaX.Application.Abstractions;

namespace PharmaX.Persistence.Backup;

/// <summary>
/// Backs the database up to %PROGRAMDATA%\PharmaX\backups and keeps the
/// last fourteen copies.
/// </summary>
/// <remarks>
/// <para>
/// A shared MariaDB or MySQL database is dumped with the server's own
/// mariadb-dump or mysqldump, in one transaction so a till trading at the
/// same moment cannot leave it half-written, and zipped: the history taken
/// over from the previous system is a hundred thousand rows, which shrinks tenfold.
/// The login goes to the tool in a temporary option file, never on its
/// command line, where any user listing processes could read it.
/// A standalone SQLite file is copied with VACUUM INTO, which makes a
/// consistent copy while the file is open.
/// </para>
/// <para>
/// To restore a dump: unzip it, then on the main computer
/// <c>mariadb -u root -p &lt; pharmax-....sql</c> with the root password
/// from C:\ProgramData\PharmaX\database-admin.txt. It recreates the
/// database it was taken from.
/// </para>
/// <para>
/// The copies are on the same disk as the database. They protect against
/// a bad upgrade or a mistake, not a dead disk: copy them off the PC.
/// </para>
/// </remarks>
public sealed class DatabaseBackup : IDatabaseBackup
{
    private const int Kept = 14;

    public static string Folder { get; } = Path.Combine(
        Environment.ExpandEnvironmentVariables("%PROGRAMDATA%"), "PharmaX", "backups");

    private readonly PharmaXDbContext _db;
    private readonly PharmaXOptions _options;
    private readonly ILogger<DatabaseBackup> _log;

    public DatabaseBackup(PharmaXDbContext db, PharmaXOptions options,
                          ILogger<DatabaseBackup> log)
    {
        _db = db;
        _options = options;
        _log = log;
    }

    public bool HoldsTheDatabase
    {
        get
        {
            if (!_options.Database.IsShared) return true;

            var server = new MySqlConnectionStringBuilder(_options.Database.SharedConnection).Server;
            return server is "localhost" or "127.0.0.1" or "::1" or "."
                   || string.Equals(server, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
        }
    }

    public async Task<string> BackUpAsync(string reason, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Folder);

        var name = string.Create(CultureInfo.InvariantCulture,
            $"pharmax-{DateTime.Now:yyyyMMdd-HHmmss}-{reason}");

        string written;

        if (_options.Database.IsShared)
        {
            var sql = Path.Combine(Folder, name + ".sql");
            await DumpAsync(sql, ct);

            written = Path.Combine(Folder, name + ".zip");

            using (var zip = ZipFile.Open(written, ZipArchiveMode.Create))
                zip.CreateEntryFromFile(sql, Path.GetFileName(sql), CompressionLevel.Optimal);

            File.Delete(sql);
        }
        else
        {
            written = Path.Combine(Folder, name + ".db");
            await _db.Database.ExecuteSqlRawAsync("VACUUM INTO {0}", new object[] { written }, ct);
        }

        Prune();

        _log.LogInformation("Database backed up to {File} ({Bytes:N0} bytes)",
                            written, new FileInfo(written).Length);
        return written;
    }

    private async Task DumpAsync(string file, CancellationToken ct)
    {
        var login = new MySqlConnectionStringBuilder(_options.Database.SharedConnection);

        var tool = DumpTool() ?? throw new InvalidOperationException(
            "Neither mariadb-dump nor mysqldump is installed on this computer. Back up "
            + "from the pharmacy's main computer, which holds the database.");

        var defaults = Path.Combine(Path.GetTempPath(), $"pharmax-backup-{Guid.NewGuid():N}.cnf");

        await File.WriteAllTextAsync(defaults, string.Join(Environment.NewLine,
            "[client]", $"user={login.UserID}", $"password=\"{login.Password}\"",
            $"host={login.Server}", $"port={login.Port}"), ct);

        try
        {
            var start = new ProcessStartInfo(tool)
            {
                CreateNoWindow = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            // The option file has to come first, or the tool ignores it.
            foreach (var argument in new[]
                     {
                         $"--defaults-extra-file={defaults}", "--single-transaction",
                         "--routines", "--triggers", "--default-character-set=utf8mb4",
                         // Row versions are binary; as hex they survive any
                         // character set the restore runs under.
                         "--hex-blob",
                         $"--result-file={file}", "--databases", login.Database
                     })
                start.ArgumentList.Add(argument);

            using var process = Process.Start(start)
                ?? throw new InvalidOperationException($"{tool} would not start.");

            var errors = await process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    $"The backup stopped ({Path.GetFileName(tool)}): {errors.Trim()}");
        }
        finally
        {
            File.Delete(defaults);
        }
    }

    /// <summary>The newest MariaDB or MySQL dump tool installed, or null.</summary>
    private static string? DumpTool()
    {
        var folders = new List<string>();

        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                 }.Where(Directory.Exists))
        {
            folders.AddRange(Directory.GetDirectories(root, "MariaDB*"));

            var mysql = Path.Combine(root, "MySQL");
            if (Directory.Exists(mysql))
                folders.AddRange(Directory.GetDirectories(mysql, "MySQL Server*"));
        }

        return folders
            .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
            .SelectMany(f => new[]
            {
                Path.Combine(f, "bin", "mariadb-dump.exe"),
                Path.Combine(f, "bin", "mysqldump.exe")
            })
            .FirstOrDefault(File.Exists);
    }

    private static void Prune()
    {
        foreach (var old in new DirectoryInfo(Folder)
                     .GetFiles("pharmax-*")
                     .OrderByDescending(f => f.CreationTimeUtc)
                     .Skip(Kept))
        {
            try
            {
                old.Delete();
            }
            catch (IOException)
            {
                // Open in another program; it goes next time.
            }
        }
    }
}
