using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using MySqlConnector;

namespace PharmaX.Desktop.Setup;

/// <summary>
/// The database login the installer made on the main computer, for the
/// wizard to fill in and for the tills to be given.
/// </summary>
public sealed record ServerLogin(string Database, string User,
                                 string Password, int Port = 3306);

/// <summary>
/// Turns this machine into the pharmacy's main computer: installs
/// MariaDB, makes PharmaX's login and opens the port to the local network.
/// </summary>
/// <remarks>
/// Run by the installer, elevated, as <c>PharmaX.Desktop.exe
/// --setup-server &lt;msi&gt;</c> when "main computer" is ticked. Here rather
/// than in the installer's Pascal so that the passwords are made by a
/// real random number generator and never pass through a script.
///
/// Safe to run again: a machine that already has its login, or already
/// has a database server somebody else installed, is left alone and the
/// wizard asks for the details as it always has.
/// </remarks>
public static class ServerSetup
{
    public const string ServiceName = "MariaDB";
    private const int Port = 3306;
    private const string FirewallRule = "PharmaX database (MariaDB)";

    private static string InstallDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "MariaDB");

    /// The PharmaX login. Readable by users on purpose: the wizard reads
    /// it, and the same password goes into every till's settings anyway.
    public static string LoginPath =>
        Path.Combine(WorkstationProvisioning.Folder, "server.json");

    /// The server's root password, for support. Administrators only.
    public static string AdminPath =>
        Path.Combine(WorkstationProvisioning.Folder, "database-admin.txt");

    private static string LogPath =>
        Path.Combine(WorkstationProvisioning.Folder, "logs",
                     "server-setup.log");

    public static async Task<int> RunAsync(string msiPath)
    {
        try
        {
            if (Read() is not null)
            {
                Log("Already set up; nothing to do.");
                return 0;
            }

            if (PortAnswers())
            {
                Log("Something is already listening on 3306; left alone.");
                Tell("A database server is already installed on this "
                     + "computer, so PharmaX has not installed another. "
                     + "Enter its details when PharmaX asks.");
                return 0;
            }

            var root = NewPassword();
            var login = new ServerLogin("PharmaX", "pharmax", NewPassword());

            InstallMariaDb(msiPath, root);
            await CreateLoginAsync(root, login);
            OpenFirewall();
            Save(login, root);

            Log("Main computer set up.");
            return 0;
        }
        catch (Exception ex)
        {
            Log(ex.ToString());
            Tell("PharmaX could not set up the database on this computer:\n\n"
                 + ex.Message + "\n\nYou can still install MySQL or MariaDB "
                 + "yourself and enter its details when PharmaX asks. The "
                 + $"details are in {LogPath}.");
            return 1;
        }
    }

    /// <summary>
    /// The login the installer made, or null where it made none.
    /// </summary>
    public static ServerLogin? Read()
    {
        try
        {
            return File.Exists(LoginPath)
                ? JsonSerializer.Deserialize<ServerLogin>(
                    File.ReadAllText(LoginPath))
                : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException
                                       or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// A password somebody can read off one screen and type at a till:
    /// four groups of four, no letters that look like digits. 80 bits.
    /// </summary>
    public static string NewPassword()
    {
        const string alphabet = "abcdefghjkmnpqrstuvwxyz23456789";

        return string.Join('-', Enumerable.Range(0, 4)
            .Select(_ => RandomNumberGenerator.GetString(alphabet, 4)));
    }

    private static bool PortAnswers()
    {
        try
        {
            using var client = new TcpClient();
            return client.ConnectAsync("127.0.0.1", Port)
                         .Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            return false;
        }
    }

    private static void InstallMariaDb(string msiPath, string root)
    {
        if (!File.Exists(msiPath))
            throw new FileNotFoundException(
                "The MariaDB installer was not found.", msiPath);

        Log($"Installing MariaDB from {msiPath}.");

        // UTF8 makes utf8mb4 the server default, which is what the
        // database is created with anyway.
        var code = Execute("msiexec.exe",
            $"/i \"{msiPath}\" /qn /norestart "
            + $"INSTALLDIR=\"{InstallDir}\" SERVICENAME={ServiceName} "
            + $"PORT={Port} PASSWORD={root} UTF8=1");

        // 3010: installed, and Windows would like a restart some time.
        if (code is not (0 or 3010))
            throw new InvalidOperationException(
                $"The MariaDB installer stopped with code {code}.");
    }

    private static async Task CreateLoginAsync(string root, ServerLogin login)
    {
        await using var connection = new MySqlConnection(
            RootConnection(root, Port, timeout: 30));

        // The service the installer has just started can take a few
        // seconds to accept connections.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await connection.OpenAsync();
                break;
            }
            catch (MySqlException) when (attempt < 15)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }

        await GrantAsync(connection, login);

        Log($"Created database {login.Database} and login {login.User}.");
    }

    /// <summary>
    /// The server's root account on this computer, as a connection string.
    /// </summary>
    /// <remarks>
    /// User variables allowed because <c>'user'@'%'</c> would otherwise be
    /// read as a parameter the command never defined.
    /// </remarks>
    public static string RootConnection(string password, int port,
                                        int timeout = 5) =>
        new MySqlConnectionStringBuilder
        {
            Server = "127.0.0.1",
            Port = (uint)port,
            UserID = "root",
            Password = password,
            ConnectionTimeout = (uint)timeout,
            AllowUserVariables = true,
            Pooling = false
        }.ConnectionString;

    /// <summary>
    /// Makes the pharmacy's database and PharmaX's login on it, or gives a
    /// login that is already there the password asked for.
    /// </summary>
    /// <remarks>
    /// Granted at both '%' and 'localhost': a till connects as the first,
    /// and the main computer itself as the second. Any password the server
    /// will take - a short one somebody can type at every till is the
    /// pharmacy's choice to make. It goes in as a parameter, so a quote in
    /// it is only a quote; the names cannot, so they are refused unless
    /// they are plain.
    /// </remarks>
    public static async Task GrantAsync(MySqlConnection root,
        ServerLogin login, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(login);

        if (!IsPlainName(login.Database) || !IsPlainName(login.User))
            throw new ArgumentException(
                "A database or user name may only contain letters, digits "
                + "and underscores.", nameof(login));

        var sql =
            $"CREATE DATABASE IF NOT EXISTS `{login.Database}` "
            + "CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";

        foreach (var host in new[] { "%", "localhost" })
            sql +=
                $"CREATE USER IF NOT EXISTS '{login.User}'@'{host}' "
                + "IDENTIFIED BY @password;"
                + $"ALTER USER '{login.User}'@'{host}' IDENTIFIED BY @password;"
                + $"GRANT ALL PRIVILEGES ON `{login.Database}`.* "
                + $"TO '{login.User}'@'{host}';";

        await using var command = root.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@password", login.Password);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// The root password the installer made, where this user may read it;
    /// null otherwise.
    /// </summary>
    public static string? ReadRootPassword()
    {
        try
        {
            return File.Exists(AdminPath)
                ? File.ReadLines(AdminPath)
                      .Where(l => l.StartsWith("root: ", StringComparison.Ordinal))
                      .Select(l => l["root: ".Length..].Trim())
                      .FirstOrDefault()
                : null;
        }
        catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static bool IsPlainName(string name) =>
        name.Length is > 0 and <= 64
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    /// <summary>
    /// Opens the port to this network only. Every profile, because a
    /// fresh Windows often calls the shop's network Public, and a rule
    /// limited to Private would leave the tills locked out.
    /// </summary>
    private static void OpenFirewall()
    {
        Execute("netsh.exe",
            $"advfirewall firewall delete rule name=\"{FirewallRule}\"");

        var code = Execute("netsh.exe",
            $"advfirewall firewall add rule name=\"{FirewallRule}\" "
            + $"dir=in action=allow protocol=TCP localport={Port} "
            + "remoteip=localsubnet profile=any");

        if (code != 0)
            throw new InvalidOperationException(
                "Windows Firewall would not open port 3306.");

        Log("Opened port 3306 to the local subnet.");
    }

    private static void Save(ServerLogin login, string root)
    {
        Directory.CreateDirectory(WorkstationProvisioning.Folder);
        File.WriteAllText(LoginPath, JsonSerializer.Serialize(login));

        File.WriteAllText(AdminPath,
            "MariaDB root password for this computer. Keep it safe.\r\n"
            + $"root: {root}\r\n");

        // Administrators and SYSTEM only, not inherited from the folder
        // the users can write to.
        var code = Execute("icacls.exe",
            $"\"{AdminPath}\" /inheritance:r "
            + "/grant:r *S-1-5-32-544:F /grant:r *S-1-5-18:F");

        if (code != 0)
            throw new InvalidOperationException(
                "Could not restrict the database administrator file.");
    }

    private static int Execute(string file, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(file, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException($"Could not start {file}.");

        process.WaitForExit();
        return process.ExitCode;
    }

    private static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}\r\n");
        }
        catch (IOException)
        {
            // A log that cannot be written must not stop the setup.
        }
    }

    private static void Tell(string message) =>
        MessageBox.Show(message, "PharmaX", MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
}
