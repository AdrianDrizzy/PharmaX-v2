using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaX.Domain.Legacy;

namespace PharmaX.Persistence.Configurations;

/// <summary>
/// The history taken over from the old system. Indexed the ways it is
/// looked up: a patient's scripts, a product's movements, a kind of
/// record between two dates.
/// </summary>
public sealed class LegacyRecordConfiguration
    : IEntityTypeConfiguration<LegacyRecord>
{
    public void Configure(EntityTypeBuilder<LegacyRecord> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("LegacyRecord");
        b.HasKey(x => x.Id);

        b.Property(x => x.Source).HasMaxLength(10).IsRequired();
        b.Property(x => x.Kind).HasMaxLength(20).IsRequired();
        b.Property(x => x.Number).HasMaxLength(40);
        b.Property(x => x.ProductCode).HasMaxLength(30);
        b.Property(x => x.PatientFile).HasMaxLength(30);
        b.Property(x => x.Party).HasMaxLength(150);
        b.Property(x => x.Description).HasMaxLength(300).IsRequired();
        b.Property(x => x.Operator).HasMaxLength(60);
        b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.Property(x => x.Data).IsRequired();

        b.HasIndex(x => new { x.Kind, x.OccurredAt });
        b.HasIndex(x => x.PatientFile);
        b.HasIndex(x => x.ProductCode);
    }
}
