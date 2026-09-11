using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PharmaX.Application;
using PharmaX.Application.Abstractions;
using PharmaX.Persistence;
using PharmaX.Persistence.Seeding;
using PharmaX.Reporting;
using PharmaX.Sync;

namespace PharmaX.Tests.Integration;

/// <summary>
/// Builds the real container against a real SQLite file, migrates it and
/// seeds it, exactly as Program.cs does at start-up. Nothing here is a
/// fake: the point is to catch the things unit tests cannot, such as a
/// mapping that compiles and then will not save.
/// </summary>
public sealed class PharmaXFixture : IDisposable
{
    private readonly string _databasePath;
    private readonly ServiceProvider _provider;

    public PharmaXFixture()
    {
        _databasePath = Path.Combine(Path.GetTempPath(),
            $"pharmax-test-{Guid.NewGuid():N}.db");

        Options = new PharmaXOptions
        {
            Workstation = new WorkstationOptions
            {
                BranchCode = "1",
                TillNumber = "1",
                BranchId = "11111111-1111-1111-1111-111111111111",
                WorkstationId = "22222222-2222-2222-2222-222222222222"
            },
            Database = new DatabaseOptions
            {
                LocalConnection = $"Data Source={_databasePath}",
                HostConnection = ""      // standalone: no host to reach
            }
        };

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton(Options);
        services.AddPharmaXPersistence(Options);
        services.AddPharmaXApplication();
        services.AddPharmaXReporting();
        services.AddPharmaXSync(Options);

        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PharmaXDbContext>();
        db.Database.Migrate();

        scope.ServiceProvider.GetRequiredService<ReferenceDataSeeder>()
             .SeedAsync().GetAwaiter().GetResult();
    }

    public PharmaXOptions Options { get; }

    /// <summary>
    /// A scope per operation, the way the desktop application resolves a
    /// screen. Sharing one context across a whole test would hide exactly
    /// the tracking problems this project exists to find.
    /// </summary>
    public IServiceScope NewScope() => _provider.CreateScope();

    public static T Resolve<T>(IServiceScope scope) where T : notnull =>
        scope.ServiceProvider.GetRequiredService<T>();

    public void Dispose()
    {
        _provider.Dispose();

        // SQLite holds the file until the pool is cleared, so a delete
        // straight after disposal fails on Windows.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            if (File.Exists(_databasePath)) File.Delete(_databasePath);
        }
        catch (IOException)
        {
            // A leftover temp file is not worth failing a test run over.
        }
    }
}
