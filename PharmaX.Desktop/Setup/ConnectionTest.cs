using MySqlConnector;

namespace PharmaX.Desktop.Setup;

/// <summary>
/// What happened when the wizard tried the database.
/// </summary>
/// <param name="ServerVersion">
/// As the server reports it - "8.0.42-mysql" or "10.11.0-mariadb". Kept
/// because MySQL and MariaDB have diverged enough that the driver emits
/// different SQL for each, so guessing wrong produces statements the
/// server rejects at some later and much less obvious moment.
/// </param>
public sealed record ConnectionTestResult(bool Succeeded, string Message,
    string ServerVersion = "", bool DatabaseExists = false);

/// <summary>
/// Tries the database the operator has typed in, before anything is
/// written.
/// </summary>
/// <remarks>
/// The point of this is the error message. "Cannot connect" sends
/// somebody to a support line; "the server is refusing the password for
/// pharmax" or "MySQL is not listening on that machine" sends them to
/// the thing that is actually wrong. Every failure below is translated.
/// </remarks>
public static class ConnectionTest
{
    /// <summary>
    /// Connects, reports the server version, and says whether the
    /// pharmacy's database is already there.
    /// </summary>
    /// <remarks>
    /// Connects to the server rather than to the database, so that a
    /// first installation - where the database has not been created yet
    /// - reports a reachable server rather than a failure. Creating it
    /// is the next step, and it needs a connection to do it.
    /// </remarks>
    public static async Task<ConnectionTestResult> TryAsync(string host,
        int port, string database, string user, string password,
        CancellationToken ct = default)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = host,
            Port = (uint)port,
            UserID = user,
            Password = password,

            // Short: an operator standing at a new till should be told
            // the machine is unreachable in a couple of seconds, not
            // after the default fifteen.
            ConnectionTimeout = 5
        };

        try
        {
            await using var connection =
                new MySqlConnection(builder.ConnectionString);

            await connection.OpenAsync(ct);

            var version = connection.ServerVersion ?? "";

            await using var command = connection.CreateCommand();

            command.CommandText =
                "SELECT COUNT(*) FROM information_schema.schemata "
                + "WHERE schema_name = @name";

            command.Parameters.AddWithValue("@name", database);

            var found = Convert.ToInt64(
                await command.ExecuteScalarAsync(ct),
                System.Globalization.CultureInfo.InvariantCulture) > 0;

            return new ConnectionTestResult(true,
                found
                    ? $"Connected. Database {database} is there."
                    : $"Connected. Database {database} will be created.",
                Describe(version),
                found);
        }
        catch (MySqlException ex)
        {
            return new ConnectionTestResult(false, Explain(ex, host, user));
        }
        catch (OperationCanceledException)
        {
            return new ConnectionTestResult(false, "Cancelled.");
        }
    }

    /// <summary>
    /// Creates the pharmacy's database if the server has not got it.
    /// </summary>
    /// <remarks>
    /// Only the tables are left to PharmaX itself: the schema comes from
    /// the migrations that run at start-up, which is the one place it is
    /// described. This creates an empty database for them to run into,
    /// so that installing on a fresh MySQL is a thing the wizard can do
    /// rather than a thing somebody has to do in Workbench first.
    /// </remarks>
    public static async Task<ConnectionTestResult> CreateDatabaseAsync(
        string host, int port, string database, string user,
        string password, CancellationToken ct = default)
    {
        // Backtick-quoted and validated rather than parameterised:
        // a database name cannot be a parameter in DDL, so the only
        // safe course is to refuse anything that is not a plain name.
        if (!IsPlainName(database))
            return new ConnectionTestResult(false,
                "A database name may only contain letters, digits and "
                + "underscores.");

        var builder = new MySqlConnectionStringBuilder
        {
            Server = host,
            Port = (uint)port,
            UserID = user,
            Password = password,
            ConnectionTimeout = 5
        };

        try
        {
            await using var connection =
                new MySqlConnection(builder.ConnectionString);

            await connection.OpenAsync(ct);

            await using var command = connection.CreateCommand();

            command.CommandText =
                $"CREATE DATABASE IF NOT EXISTS `{database}` "
                + "CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci";

            await command.ExecuteNonQueryAsync(ct);

            return new ConnectionTestResult(true,
                $"Database {database} is ready.",
                Describe(connection.ServerVersion ?? ""), true);
        }
        catch (MySqlException ex)
        {
            return new ConnectionTestResult(false, Explain(ex, host, user));
        }
    }

    /// <summary>
    /// Turns a driver error into something an operator can act on.
    /// </summary>
    private static string Explain(MySqlException ex, string host,
                                  string user) => ex.ErrorCode switch
    {
        MySqlErrorCode.UnableToConnectToHost =>
            $"Nothing is answering on {host}. Check the machine is on, "
            + "that MySQL or MariaDB is running on it, and that its "
            + "firewall allows the port.",

        MySqlErrorCode.AccessDenied or MySqlErrorCode.PasswordNotAllowed =>
            $"The server refused the password for {user}.",

        MySqlErrorCode.UnknownDatabase =>
            "The server is there but that database is not. PharmaX can "
            + "create it.",

        // A remote account granted only at 'localhost' is the commonest
        // way a till is refused by a server that is otherwise working,
        // and the message the driver gives for it is not obvious.
        MySqlErrorCode.HostNotPrivileged =>
            $"The server will not accept connections from this machine "
            + $"for {user}. The account may be granted at 'localhost' "
            + "only, rather than at '%'.",

        _ => ex.Message
    };

    /// <summary>
    /// Normalises what the server calls itself into what the driver
    /// needs to be told.
    /// </summary>
    private static string Describe(string version)
    {
        if (version.Length == 0) return "";

        // MariaDB reports itself with the word in the version string;
        // MySQL does not.
        return version.Contains("mariadb",
                   StringComparison.OrdinalIgnoreCase)
            ? $"{Numbers(version)}-mariadb"
            : $"{Numbers(version)}-mysql";
    }

    /// The leading "10.11.0" or "8.0.42" out of a longer version string.
    private static string Numbers(string version)
    {
        var end = 0;

        while (end < version.Length
               && (char.IsDigit(version[end]) || version[end] == '.'))
            end++;

        return end == 0 ? version : version[..end].TrimEnd('.');
    }

    private static bool IsPlainName(string name) =>
        name.Length is > 0 and <= 64
        && name.All(c => char.IsLetterOrDigit(c) || c == '_');
}
