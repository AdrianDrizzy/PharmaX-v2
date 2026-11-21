using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Abstractions;

namespace PharmaX.Persistence;

/// <summary>
/// The shared database server's clock and time zone, asked once at
/// sign-in so a till with a wrong clock is caught.
/// </summary>
public sealed class ServerClock : IServerClock
{
    private readonly IDbContextFactory<HostDbContext> _contexts;

    public ServerClock(IDbContextFactory<HostDbContext> contexts) => _contexts = contexts;

    public async Task<ServerTime?> ReadAsync(CancellationToken ct = default)
    {
        await using var db = await _contexts.CreateDbContextAsync(ct);
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT UTC_TIMESTAMP(6), TIMESTAMPDIFF(MINUTE, UTC_TIMESTAMP(), NOW())";

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        var utc = DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc);
        var minutes = Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture);
        return new ServerTime(utc, TimeSpan.FromMinutes(minutes));
    }
}
