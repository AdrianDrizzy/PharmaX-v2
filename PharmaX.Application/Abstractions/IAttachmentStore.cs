namespace PharmaX.Application.Abstractions;

/// <summary>
/// Where a stored file ended up, and how big it turned out to be.
/// </summary>
public sealed record StoredFile(string RelativePath, string ContentType,
                                long SizeBytes);

/// <summary>
/// The pharmacy's filing cabinet for scanned documents.
/// </summary>
/// <remarks>
/// An abstraction because the application layer must not know what a
/// drive letter is, and because the store is the one part of an
/// attachment that does not sync: the row travels to the host, the
/// bytes stay where they were written. A workstation that has never
/// seen a given scan resolves the path, finds nothing, and says so.
/// </remarks>
public interface IAttachmentStore
{
    /// <summary>
    /// Copies a file into the cabinet under this patient and returns
    /// where it landed.
    /// </summary>
    Task<StoredFile> SaveAsync(Guid patientId, string sourcePath,
                               CancellationToken ct = default);

    /// <summary>
    /// The full path for a stored file, whether or not it is there.
    /// </summary>
    string Resolve(string relativePath);

    /// True where the bytes are actually on this workstation.
    bool Exists(string relativePath);

    /// <summary>
    /// Removes the bytes. Failure is swallowed: a clinical record that
    /// cannot be unfiled because a file is open is worse than a stray
    /// image on a disk.
    /// </summary>
    void Delete(string relativePath);
}
