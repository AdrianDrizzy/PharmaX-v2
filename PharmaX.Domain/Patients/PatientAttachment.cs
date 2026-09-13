using PharmaX.Domain.Common;
using PharmaX.Domain.Exceptions;

namespace PharmaX.Domain.Patients;

/// <summary>
/// What a scan is of. The kind is chosen by the person filing it and
/// drives nothing but how the list reads, which is the point: a
/// pharmacist looking for the paper script should not have to open six
/// identity documents to find it.
/// </summary>
public enum AttachmentKind
{
    Prescription,
    Identity,
    MedicalAidCard,
    Authorisation,
    Other
}

/// <summary>
/// A scanned or photographed document filed against a patient - most
/// often the paper prescription the dispensing was worked from.
/// </summary>
/// <remarks>
/// The image itself is not here. This row records where it is and what
/// it is; the bytes live under the pharmacy's own attachment folder and
/// are written by the layer that can touch a disk. Keeping them out of
/// the database was a deliberate choice: a till database that a
/// pharmacist waits on every morning should not carry several gigabytes
/// of photographs. The trade is that the folder has to be in the
/// pharmacy's backup, which the setup screen says in as many words.
/// </remarks>
public sealed class PatientAttachment : AuditableEntity
{
    public Guid PatientId { get; private set; }

    /// The dispensing this was filed against, where it was filed from a
    /// script rather than from the patient's file.
    public Guid? ScriptId { get; private set; }

    public AttachmentKind Kind { get; private set; }

    /// What the operator called it, or the original file name.
    public string Description { get; private set; } = "";

    /// <summary>
    /// Where the image is, relative to the attachment root.
    /// </summary>
    /// <remarks>
    /// Relative on purpose. An absolute path would pin the pharmacy to
    /// the drive letter the first workstation happened to use, and this
    /// row syncs to the host where that path means nothing.
    /// </remarks>
    public string RelativePath { get; private set; } = "";

    public string ContentType { get; private set; } = "";
    public long SizeBytes { get; private set; }
    public DateTime CapturedAtUtc { get; private set; }

    /// Who filed it. A clinical record needs to say who put it there.
    public Guid CapturedByUserId { get; private set; }

    private PatientAttachment() { }

    public static PatientAttachment File(Guid patientId, AttachmentKind kind,
        string description, string relativePath, string contentType,
        long sizeBytes, DateTime capturedAtUtc, Guid capturedByUserId,
        Guid? scriptId = null)
    {
        if (patientId == Guid.Empty)
            throw new DomainException("An attachment needs a patient.");

        if (string.IsNullOrWhiteSpace(relativePath))
            throw new DomainException("An attachment needs a file.");

        if (sizeBytes <= 0)
            throw new DomainException(
                "An empty file is not worth filing against a patient.");

        return new PatientAttachment
        {
            PatientId = patientId,
            ScriptId = scriptId,
            Kind = kind,
            Description = string.IsNullOrWhiteSpace(description)
                ? kind.ToString()
                : description.Trim(),
            RelativePath = relativePath.Replace('\\', '/'),
            ContentType = contentType,
            SizeBytes = sizeBytes,
            CapturedAtUtc = capturedAtUtc,
            CapturedByUserId = capturedByUserId
        };
    }

    public void Describe(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainException("A description cannot be blank.");

        Description = description.Trim();
    }

    /// <summary>
    /// Ties a scan filed from the patient's file to a script after the
    /// fact, which is how it usually happens: the paper is scanned at
    /// the counter and matched to the dispensing a moment later.
    /// </summary>
    public void AttachToScript(Guid scriptId)
    {
        if (scriptId == Guid.Empty)
            throw new DomainException("That is not a script.");

        ScriptId = scriptId;
    }
}
