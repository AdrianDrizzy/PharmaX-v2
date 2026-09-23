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

        b.Property(x => x.Code).HasMaxLength(30).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.AccountType).HasConversion<string>()
                                      .HasMaxLength(20);

        b.Ignore(x => x.IsDebitPositive);

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
        b.Property(x => x.Source).HasMaxLength(50).IsRequired();
        b.Property(x => x.Narration).HasMaxLength(500);
        b.Property(x => x.RowVersion).IsConcurrencyToken();

        // Totals are the sum of the lines, never stored: a stored total
        // is one more thing that can disagree with them.
        b.Ignore(x => x.TotalDebits);
        b.Ignore(x => x.TotalCredits);
        b.Ignore(x => x.IsBalanced);

        b.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey(l => l.JournalBatchId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Navigation(x => x.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasIndex(x => x.BatchNumber).IsUnique();

        // Day-end posting asks "has this day already been posted", so
        // the source and date it asks by are indexed together.
        b.HasIndex(x => new { x.Source, x.PostingDate });
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

        b.Property(x => x.AccountCode).HasMaxLength(30).IsRequired();
        b.Property(x => x.Narration).HasMaxLength(500);

        b.ComplexProperty(x => x.Amount, m =>
        {
            m.Property(p => p.Amount).HasColumnName("Amount")
                                     .HasPrecision(18, 2);
            m.Property(p => p.Currency).HasColumnName("Currency")
                                       .HasMaxLength(3);
        });

        b.HasIndex(x => new { x.JournalBatchId, x.LineNo }).IsUnique();
    }
}

public sealed class MedicalAidClaimConfiguration
    : IEntityTypeConfiguration<Domain.Claims.MedicalAidClaim>
{
    public void Configure(
        EntityTypeBuilder<Domain.Claims.MedicalAidClaim> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("MedicalAidClaim");
        b.HasKey(x => x.Id);

        b.Property(x => x.ClaimNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.MembershipNumber).HasMaxLength(40).IsRequired();
        b.Property(x => x.DependantCode).HasMaxLength(10);
        b.Property(x => x.WriteOffReason).HasMaxLength(300);
        b.Property(x => x.RowVersion).IsConcurrencyToken();

        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);

        foreach (var (money, column) in new[]
        {
            (nameof(Domain.Claims.MedicalAidClaim.Claimed), "Claimed"),
            (nameof(Domain.Claims.MedicalAidClaim.Settled), "Settled"),
            (nameof(Domain.Claims.MedicalAidClaim.WrittenOff), "WrittenOff")
        })
        {
            b.ComplexProperty(money, m =>
            {
                m.Property("Amount").HasColumnName(column)
                                    .HasPrecision(18, 2);
                m.Property("Currency").HasColumnName(column + "Currency")
                                      .HasMaxLength(3);
            });
        }

        // Outstanding is Claimed less Settled less WrittenOff. Storing it
        // would give the claim a second opinion about what it is owed.
        b.Ignore(x => x.Outstanding);
        b.Ignore(x => x.IsOpen);

        b.HasIndex(x => x.ClaimNumber).IsUnique();

        // One claim per dispensing. Issuing runs inside a transaction and
        // is idempotent, but this is the guarantee that a retry cannot
        // bill a scheme twice for one script.
        b.HasIndex(x => x.DispensingId).IsUnique();

        // The two queries the claims screen makes: what one scheme still
        // owes, and everything still open, oldest first.
        b.HasIndex(x => new { x.SchemeId, x.Status });
        b.HasIndex(x => new { x.Status, x.ClaimedOn });
    }
}
