using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaX.Domain.Dispensing;

namespace PharmaX.Persistence.Configurations;

public sealed class ScriptConfiguration : IEntityTypeConfiguration<Script>
{
    public void Configure(EntityTypeBuilder<Script> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("Script");
        b.HasKey(x => x.Id);

        b.Property(x => x.ScriptNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.PrescriberName).HasMaxLength(150).IsRequired();
        b.Property(x => x.PrescriberNumber).HasMaxLength(30);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.RowVersion).IsConcurrencyToken();

        b.Ignore(x => x.RepeatsRemaining);
        b.Ignore(x => x.IsEditable);

        b.HasMany(x => x.Items)
         .WithOne()
         .HasForeignKey(i => i.ScriptId)
         .OnDelete(DeleteBehavior.Cascade);

        b.Navigation(x => x.Items).HasField("_items")
         .UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasIndex(x => x.ScriptNumber).IsUnique();
        b.HasIndex(x => new { x.PatientId, x.PrescribedOn });
    }
}

public sealed class ScriptItemConfiguration
    : IEntityTypeConfiguration<ScriptItem>
{
    public void Configure(EntityTypeBuilder<ScriptItem> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("ScriptItem");
        b.HasKey(x => x.Id);

        b.Property(x => x.Description).HasMaxLength(200).IsRequired();
        b.Property(x => x.Directions).HasMaxLength(500).IsRequired();
        b.Property(x => x.Quantity).HasPrecision(18, 3);

        b.HasIndex(x => new { x.ScriptId, x.LineNo }).IsUnique();
    }
}

public sealed class DispensingConfiguration
    : IEntityTypeConfiguration<Domain.Dispensing.Dispensing>
{
    public void Configure(
        EntityTypeBuilder<Domain.Dispensing.Dispensing> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("Dispensing");
        b.HasKey(x => x.Id);

        b.Property(x => x.DocumentNumber).HasMaxLength(30);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.ReversalReason).HasMaxLength(300);
        b.Property(x => x.RowVersion).IsConcurrencyToken();

        b.Ignore(x => x.TariffTotal);
        b.Ignore(x => x.ContributionTotal);
        b.Ignore(x => x.CoPaymentTotal);
        b.Ignore(x => x.PendingWarnings);
        b.Ignore(x => x.AwaitsCollection);

        b.HasMany(x => x.Lines)
         .WithOne()
         .HasForeignKey(l => l.DispensingId)
         .OnDelete(DeleteBehavior.Cascade);

        b.Navigation(x => x.Lines).HasField("_lines")
         .UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasMany(x => x.Warnings)
         .WithOne()
         .HasForeignKey(w => w.DispensingId)
         .OnDelete(DeleteBehavior.Cascade);

        b.Navigation(x => x.Warnings).HasField("_warnings")
         .UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasIndex(x => x.ScriptId);
        b.HasIndex(x => new { x.PatientId, x.IssuedAtUtc });

        // The collection queue's own query: issued, unpaid, oldest
        // first. Every till re-runs it on a timer, so it is worth an
        // index rather than a scan of every dispensing the pharmacy has
        // ever made.
        b.HasIndex(x => new { x.Status, x.SaleId, x.IssuedAtUtc });
    }
}

public sealed class DispensingLineConfiguration
    : IEntityTypeConfiguration<DispensingLine>
{
    public void Configure(EntityTypeBuilder<DispensingLine> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("DispensingLine");
        b.HasKey(x => x.Id);

        b.Property(x => x.Description).HasMaxLength(200).IsRequired();
        b.Property(x => x.Directions).HasMaxLength(500).IsRequired();
        b.Property(x => x.BatchNumber).HasMaxLength(50);
        b.Property(x => x.Quantity).HasPrecision(18, 3);

        b.ComplexProperty(x => x.TariffAmount, m =>
        {
            m.Property(p => p.Amount).HasColumnName("Tariff")
                                     .HasPrecision(18, 2);
            m.Property(p => p.Currency).HasColumnName("TariffCurrency")
                                       .HasMaxLength(3);
        });

        b.ComplexProperty(x => x.SchemeContribution, m =>
        {
            m.Property(p => p.Amount).HasColumnName("Contribution")
                                     .HasPrecision(18, 2);
            m.Property(p => p.Currency)
                .HasColumnName("ContributionCurrency").HasMaxLength(3);
        });

        b.Ignore(x => x.CoPayment);
        b.Ignore(x => x.BatchSummary);

        // Every batch the line draws on. Owned by the line and saved with
        // it: an allocation has no life of its own.
        b.HasMany(x => x.Batches)
         .WithOne()
         .HasForeignKey(a => a.DispensingLineId)
         .OnDelete(DeleteBehavior.Cascade);

        b.Navigation(x => x.Batches).HasField("_batches")
         .UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasIndex(x => new { x.DispensingId, x.LineNo }).IsUnique();
    }
}

public sealed class DispensingLineBatchConfiguration
    : IEntityTypeConfiguration<DispensingLineBatch>
{
    public void Configure(EntityTypeBuilder<DispensingLineBatch> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("DispensingLineBatch");
        b.HasKey(x => x.Id);

        b.Property(x => x.BatchNumber).HasMaxLength(50).IsRequired();
        b.Property(x => x.Quantity).HasPrecision(18, 3);

        b.HasIndex(x => x.DispensingLineId);
        b.HasIndex(x => x.BatchId);
    }
}

public sealed class ClinicalWarningConfiguration
    : IEntityTypeConfiguration<ClinicalWarning>
{
    public void Configure(EntityTypeBuilder<ClinicalWarning> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("ClinicalWarning");
        b.HasKey(x => x.Id);

        b.Property(x => x.Kind).HasMaxLength(30).IsRequired();
        b.Property(x => x.Severity).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Message).HasMaxLength(500).IsRequired();

        // Kept for the life of the record: why a pharmacist overrode a
        // hard stop is the single most useful line in a clinical audit.
        b.Property(x => x.OverrideReason).HasMaxLength(500);

        b.Ignore(x => x.IsHardStop);

        b.HasIndex(x => x.DispensingId);
    }
}

public sealed class RepeatConfiguration : IEntityTypeConfiguration<Repeat>
{
    public void Configure(EntityTypeBuilder<Repeat> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("Repeat");
        b.HasKey(x => x.Id);

        b.Property(x => x.RowVersion).IsConcurrencyToken();

        b.Ignore(x => x.IsCollected);

        b.HasIndex(x => new { x.ScriptId, x.RepeatNumber }).IsUnique();
        b.HasIndex(x => x.DueOn);
    }
}
