using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaX.Domain.Finance;

namespace PharmaX.Persistence.Configurations;

public sealed class LedgerAccountConfiguration
    : IEntityTypeConfiguration<LedgerAccount>
{
    public void Configure(EntityTypeBuilder<LedgerAccount> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("LedgerAccount");
        b.HasKey(x => x.Id);

        b.Property(x => x.Code).HasMaxLength(20).IsRequired();
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();

        b.Property(x => x.AccountType).HasConversion<string>()
                                      .HasMaxLength(20);

        b.Ignore(x => x.IsDebitPositive);

        // The code is the account, in the chart and in every journal
        // line, so two accounts cannot share one.
        b.HasIndex(x => x.Code).IsUnique();
    }
}

public sealed class JournalBatchConfiguration
    : IEntityTypeConfiguration<JournalBatch>
{
    public void Configure(EntityTypeBuilder<JournalBatch> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("JournalBatch");
        b.HasKey(x => x.Id);

        b.Property(x => x.BatchNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.Source).HasMaxLength(30).IsRequired();
        b.Property(x => x.Narration).HasMaxLength(300);
        b.Property(x => x.RowVersion).IsConcurrencyToken();

        // Totals are sums over the lines. Storing them would give the
        // books a second opinion about whether they balance.
        b.Ignore(x => x.TotalDebits);
        b.Ignore(x => x.TotalCredits);
        b.Ignore(x => x.IsBalanced);

        b.HasMany(x => x.Lines)
         .WithOne()
         .HasForeignKey(l => l.JournalBatchId)
         .OnDelete(DeleteBehavior.Cascade);

        b.Navigation(x => x.Lines).HasField("_lines")
         .UsePropertyAccessMode(PropertyAccessMode.Field);

        // The trial balance reads by date, and the journal list reads by
        // where a batch came from.
        b.HasIndex(x => x.PostingDate);
        b.HasIndex(x => new { x.Source, x.PostingDate });

        b.HasIndex(x => x.BatchNumber).IsUnique();
    }
}

public sealed class JournalLineConfiguration
    : IEntityTypeConfiguration<JournalLine>
{
    public void Configure(EntityTypeBuilder<JournalLine> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("JournalLine");
        b.HasKey(x => x.Id);

        b.Property(x => x.AccountCode).HasMaxLength(20).IsRequired();
        b.Property(x => x.Narration).HasMaxLength(300);

        b.ComplexProperty(x => x.Amount, m =>
        {
            m.Property(p => p.Amount).HasColumnName("Amount")
                                     .HasPrecision(18, 2);
            m.Property(p => p.Currency).HasColumnName("AmountCurrency")
                                       .HasMaxLength(3);
        });

        // Every trial balance groups by this, over every line there is.
        b.HasIndex(x => x.AccountCode);
    }
}
