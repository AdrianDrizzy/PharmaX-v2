using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaX.Domain.Patients;

namespace PharmaX.Persistence.Configurations;

public sealed class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("Patient");
        b.HasKey(x => x.Id);

        b.Property(x => x.FileNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.Surname).HasMaxLength(100).IsRequired();
        b.Property(x => x.FirstNames).HasMaxLength(150).IsRequired();
        b.Property(x => x.IdNumber).HasMaxLength(20);
        b.Property(x => x.Telephone).HasMaxLength(30);
        b.Property(x => x.Email).HasMaxLength(150);
        b.Property(x => x.RowVersion).IsConcurrencyToken();

        b.Ignore(x => x.FullName);

        b.HasIndex(x => x.FileNumber).IsUnique();
        b.HasIndex(x => new { x.Surname, x.FirstNames });

        // Sparse in practice, and the one lookup that has to be exact.
        b.HasIndex(x => x.IdNumber);
    }
}

public sealed class ClinicalProfileConfiguration
    : IEntityTypeConfiguration<ClinicalProfile>
{
    public void Configure(EntityTypeBuilder<ClinicalProfile> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("ClinicalProfile");
        b.HasKey(x => x.Id);

        b.Property(x => x.WeightKg).HasPrecision(9, 3);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.RowVersion).IsConcurrencyToken();

        // A read-only IReadOnlyList cannot be a primitive collection in
        // EF Core 8, so the private List behind it is mapped instead and
        // the property is projected from it. The column keeps the
        // property's name; nothing outside this file sees the field.
        b.PrimitiveCollection<List<string>>("_chronicConditions")
         .HasField("_chronicConditions")
         .HasColumnName("ChronicConditions")
         .UsePropertyAccessMode(PropertyAccessMode.Field);

        b.Ignore(x => x.ChronicConditions);

        b.HasMany(x => x.Allergies)
         .WithOne()
         .HasForeignKey(a => a.ClinicalProfileId)
         .OnDelete(DeleteBehavior.Cascade);

        b.Navigation(x => x.Allergies).HasField("_allergies")
         .UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasIndex(x => x.PatientId).IsUnique();
    }
}

public sealed class AllergyConfiguration : IEntityTypeConfiguration<Allergy>
{
    public void Configure(EntityTypeBuilder<Allergy> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("Allergy");
        b.HasKey(x => x.Id);

        b.Property(x => x.Substance).HasMaxLength(150).IsRequired();
        b.Property(x => x.Severity).HasMaxLength(20).IsRequired();
        b.Property(x => x.Reaction).HasMaxLength(500);

        b.Ignore(x => x.IsSevere);

        b.HasIndex(x => x.ClinicalProfileId);
    }
}

public sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("Account");
        b.HasKey(x => x.Id);

        b.Property(x => x.AccountNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.HoldReason).HasMaxLength(250);
        b.Property(x => x.RowVersion).IsConcurrencyToken();

        b.ComplexProperty(x => x.Balance, m =>
        {
            m.Property(p => p.Amount).HasColumnName("Balance")
                                     .HasPrecision(18, 2);
            m.Property(p => p.Currency).HasColumnName("BalanceCurrency")
                                       .HasMaxLength(3);
        });

        b.ComplexProperty(x => x.CreditLimit, m =>
        {
            m.Property(p => p.Amount).HasColumnName("CreditLimit")
                                     .HasPrecision(18, 2);
            m.Property(p => p.Currency).HasColumnName("LimitCurrency")
                                       .HasMaxLength(3);
        });

        b.Ignore(x => x.Available);

        b.HasIndex(x => x.AccountNumber).IsUnique();
    }
}

/// <summary>
/// A scanned document filed against a patient. The image is not here -
/// only where it is and what it is.
/// </summary>
public sealed class PatientAttachmentConfiguration
    : IEntityTypeConfiguration<PatientAttachment>
{
    public void Configure(EntityTypeBuilder<PatientAttachment> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("PatientAttachment");
        b.HasKey(x => x.Id);

        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Description).HasMaxLength(200).IsRequired();
        b.Property(x => x.RelativePath).HasMaxLength(300).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        b.Property(x => x.RowVersion).IsConcurrencyToken();

        // The patient's file is read by patient and shown newest first,
        // which is the only way this table is ever queried.
        b.HasIndex(x => new { x.PatientId, x.CapturedAtUtc });

        // Finding the scans behind one dispensing, for a reprint or a
        // query about what was actually presented at the counter.
        b.HasIndex(x => x.ScriptId);
    }
}
