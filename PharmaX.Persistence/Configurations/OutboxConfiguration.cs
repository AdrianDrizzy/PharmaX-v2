using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaX.Persistence.Numbering;
using PharmaX.Persistence.Outbox;

namespace PharmaX.Persistence.Configurations;

public sealed class OutboxEntryConfiguration
    : IEntityTypeConfiguration<OutboxEntry>
{
    public void Configure(EntityTypeBuilder<OutboxEntry> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("OutboxEntry");
        b.HasKey(x => x.Id);

        b.Property(x => x.EntityType).HasMaxLength(80).IsRequired();
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.LastError).HasMaxLength(1000);
        b.Property(x => x.PayloadJson).IsRequired();

        // The drainer's only query: oldest pending first. Nothing consumes
        // the outbox in Release 1.0, and this index is what makes that a
        // cheap decision to reverse later.
        b.HasIndex(x => new { x.Status, x.CreatedAtUtc });
    }
}

public sealed class DocumentNumberBlockConfiguration
    : IEntityTypeConfiguration<DocumentNumberBlock>
{
    public void Configure(EntityTypeBuilder<DocumentNumberBlock> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("DocumentNumberBlock");
        b.HasKey(x => x.Id);

        b.Property(x => x.Series).HasMaxLength(10).IsRequired();
        b.Property(x => x.WorkstationCode).HasMaxLength(10).IsRequired();

        // Two blocks for the same series on the same till must not overlap,
        // which is the whole basis of safe offline numbering (NET-021).
        b.HasIndex(x => new { x.Series, x.WorkstationCode, x.RangeFrom })
         .IsUnique();
    }
}
