using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PharmaX.Persistence;

/// <summary>
/// Migrations run at design time, when no host and no container exist, so
/// EF Core needs a way to build a context on its own.
/// </summary>
public sealed class DesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<PharmaXDbContext>
{
    public PharmaXDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PharmaXDbContext>()
            .UseSqlite("Data Source=design.db")
            .Options;

        return new PharmaXDbContext(options);
    }
}

/// <summary>
/// The same service for the host database, which is MariaDB rather than
/// SQLite and therefore needs its own factory: the provider decides what
/// a generated migration says, so a host migration scaffolded against
/// SQLite would emit SQL the host cannot run.
/// </summary>
/// <remarks>
/// The version is pinned rather than detected. Pomelo's
/// ServerVersion.AutoDetect opens a connection, which would make
/// scaffolding a migration require a running MariaDB - and the developer
/// adding a column is usually the one person who has not got one. The
/// connection string is never opened at design time for this reason;
/// it exists only because UseMySql demands one.
/// </remarks>
public sealed class HostDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<HostDbContext>
{
    /// <summary>
    /// The oldest host the generated SQL has to be valid for. Raise this
    /// when the estate's minimum MariaDB version moves, not before: a
    /// higher version here can emit syntax an older host rejects.
    /// </summary>
    public static readonly ServerVersion HostServerVersion =
        new MariaDbServerVersion(new Version(10, 11, 0));

    public HostDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<HostDbContext>()
            .UseMySql("Server=localhost;Database=pharmax_design;User ID=root",
                      HostServerVersion)
            .Options;

        return new HostDbContext(options);
    }
}
