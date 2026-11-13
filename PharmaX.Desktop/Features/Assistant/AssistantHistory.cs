using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace PharmaX.Desktop.Features.Assistant;

/// <summary>
/// Today's conversations with Ask PharmaX, each user's own: listed down
/// the side of the window, gone once the day is over.
/// </summary>
/// <remarks>
/// On this computer only, encrypted to this Windows account: answers can
/// name patients and what they take.
/// </remarks>
internal static class AssistantHistory
{
    /// A conversation as the box showed it, and where each picture's whole window is.
    internal sealed record Chat(string Title, DateTime Started, string Rtf, List<DayPicture> Pictures);

    internal sealed record DayPicture(int At, byte[] Png);

    internal sealed record Listed(Guid Id, string Title, DateTime Started);

    private const string Extension = ".chat";

    internal static string Folder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PharmaX", "Assistant");

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string PathOf(Guid user, DateOnly day, Guid chat) =>
        Path.Combine(Folder, user.ToString("N"), $"{Day(day)}_{chat:N}{Extension}");

    /// <summary>
    /// The user's conversations from today, newest first, after every
    /// user's conversations from other days are deleted.
    /// </summary>
    /// <remarks>
    /// shortcut: each chat is decrypted whole to read its title, fine for a
    /// day's handful; keep an index file if a day ever holds hundreds.
    /// </remarks>
    public static IReadOnlyList<Listed> Today(Guid user, DateOnly today)
    {
        ForgetAllBut(today);

        var folder = Path.Combine(Folder, user.ToString("N"));
        if (!Directory.Exists(folder)) return Array.Empty<Listed>();

        var found = new List<Listed>();
        foreach (var file in Directory.EnumerateFiles(folder, $"{Day(today)}_*{Extension}"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (!Guid.TryParseExact(name[(name.IndexOf('_', StringComparison.Ordinal) + 1)..], "N", out var id)) continue;
            if (Read(user, file) is { } chat) found.Add(new Listed(id, chat.Title, chat.Started));
        }

        return found.OrderByDescending(c => c.Started).ToList();
    }

    public static Chat? Load(Guid user, DateOnly day, Guid chat) => Read(user, PathOf(user, day, chat));

    public static void Save(Guid user, DateOnly day, Guid id, Chat chat)
    {
        var path = PathOf(user, day, id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var sealedJson = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(chat),
                                               user.ToByteArray(), DataProtectionScope.CurrentUser);

        // Written beside it first, so a crash part way leaves the last one whole.
        File.WriteAllBytes(path + ".new", sealedJson);
        File.Move(path + ".new", path, overwrite: true);
    }

    public static void Delete(Guid user, DateOnly day, Guid chat) => File.Delete(PathOf(user, day, chat));

    private static Chat? Read(Guid user, string path)
    {
        if (!File.Exists(path)) return null;

        try
        {
            var json = ProtectedData.Unprotect(File.ReadAllBytes(path), user.ToByteArray(), DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<Chat>(json);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException)
        {
            ErrorHandler.Log(ex);
            return null;
        }
    }

    private static void ForgetAllBut(DateOnly today)
    {
        if (!Directory.Exists(Folder)) return;

        foreach (var file in Directory.EnumerateFiles(Folder, "*" + Extension + "*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file).StartsWith(Day(today) + "_", StringComparison.Ordinal)
                && file.EndsWith(Extension, StringComparison.Ordinal)) continue;

            try
            {
                File.Delete(file);
            }
            catch (IOException ex)
            {
                ErrorHandler.Log(ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                ErrorHandler.Log(ex);
            }
        }
    }
}
