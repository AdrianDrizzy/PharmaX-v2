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
