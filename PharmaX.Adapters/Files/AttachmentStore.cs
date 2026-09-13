using PharmaX.Application.Abstractions;

namespace PharmaX.Adapters.Files;

/// <summary>
/// Scanned documents on the workstation's disk, one folder per patient.
/// </summary>
/// <remarks>
/// Sits beside the branding folder under ProgramData rather than in the
/// user's profile, because a till is used by whoever is on shift and a
/// scan filed by the morning pharmacist has to be there for the
/// afternoon one.
/// </remarks>
public sealed class AttachmentStore : IAttachmentStore
{
    private readonly string _root;

    public AttachmentStore()
        : this(Path.Combine(
            Environment.ExpandEnvironmentVariables("%PROGRAMDATA%"),
            "PharmaX", "attachments"))
    {
    }

    public AttachmentStore(string root) => _root = root;

    public async Task<StoredFile> SaveAsync(Guid patientId,
        string sourcePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        if (!File.Exists(sourcePath))
            throw new FileNotFoundException(
                "That file is no longer there.", sourcePath);

        // A folder per patient, named by identifier rather than by the
        // patient's name: names repeat, are edited, and have no business
        // being readable from a directory listing.
        var folder = patientId.ToString("N");
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();

        // Its own name, so two scans of the same prescription filed a
        // minute apart do not overwrite one another.
        var name = $"{Guid.NewGuid():N}{extension}";
        var relative = $"{folder}/{name}";

        var destination = Resolve(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        // Copied through a stream rather than File.Copy so that a slow
        // scan over a network share can be cancelled.
        await using (var source = new FileStream(sourcePath, FileMode.Open,
                         FileAccess.Read, FileShare.Read))
        await using (var target = new FileStream(destination,
                         FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await source.CopyToAsync(target, ct).ConfigureAwait(false);
        }

        return new StoredFile(relative, ContentTypeOf(extension),
                              new FileInfo(destination).Length);
    }

    public string Resolve(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var cleaned = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(_root, cleaned));

        // A stored path comes from our own database, but it also
        // arrives from the host, and a row carrying "..\..\windows" must
        // not be able to read or delete outside the cabinet.
        var rooted = Path.GetFullPath(_root) + Path.DirectorySeparatorChar;

        if (!full.StartsWith(rooted, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "That attachment path leads outside the attachment folder.");

        return full;
    }

    public bool Exists(string relativePath)
    {
        try
        {
            return File.Exists(Resolve(relativePath));
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public void Delete(string relativePath)
    {
        try
        {
            var path = Resolve(relativePath);
            if (File.Exists(path)) File.Delete(path);
        }
#pragma warning disable CA1031 // Deliberately broad: see the interface.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    private static string ContentTypeOf(string extension) => extension switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".bmp" => "image/bmp",
        ".gif" => "image/gif",
        ".tif" or ".tiff" => "image/tiff",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream"
    };
}
