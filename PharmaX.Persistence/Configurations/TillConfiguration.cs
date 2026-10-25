using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaX.Domain.Tills;

namespace PharmaX.Persistence.Configurations;

public sealed class TillSessionConfiguration
    : IEntityTypeConfiguration<TillSession>
{
    public void Configure(EntityTypeBuilder<TillSession> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("TillSession");
        b.HasKey(x => x.Id);

        b.Property(x => x.TillNumber).HasMaxLength(10).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.ClosingVarianceReason).HasMaxLength(500);
        b.Property(x => x.RowVersion).IsConcurrencyToken();

        b.ComplexProperty(x => x.OpeningFloat, m =>
        {
            m.Property(p => p.Amount).HasColumnName("OpeningFloat")
                                     .HasPrecision(18, 2);
            m.Property(p => p.Currency).HasColumnName("FloatCurrency")
                                       .HasMaxLength(3);
        });

        // Nullable, so a converted decimal rather than a complex
        // property: EF Core 8 has no nullable complex types.
        b.Property(x => x.ClosingDeclared)
         .HasConversion(new NullableMoneyConverter())
         .HasColumnName("ClosingDeclared")
         .HasPrecision(18, 2);

        b.Ignore(x => x.IsOpen);

        // One open session per workstation is the rule POS-013 depends on.
        b.HasIndex(x => new { x.WorkstationId, x.Status });
        b.HasIndex(x => new { x.BranchId, x.OpenedAtUtc });
    }
}

public sealed class TillDeclarationConfiguration
    : IEntityTypeConfiguration<TillDeclaration>
{
    public void Configure(EntityTypeBuilder<TillDeclaration> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("TillDeclaration");
        b.HasKey(x => x.Id);

        b.Property(x => x.VarianceReason).HasMaxLength(500);
        b.Property(x => x.RowVersion).IsConcurrencyToken();

        b.Ignore(x => x.TotalDeclared);
        b.Ignore(x => x.TotalVariance);

        b.OwnsMany(x => x.Lines, l =>
        {
            l.ToTable("TillDeclarationLine");
            l.WithOwner().HasForeignKey("TillDeclarationId");
            l.Property<int>("Id").ValueGeneratedOnAdd();
            l.HasKey("Id");

            l.Property(p => p.Tender).HasConversion<string>().HasMaxLength(20);

            // A complex property is not available inside an owned
            // collection in EF Core 8, so these two go through the
            // single-column converter.
            l.Property(p => p.Expected)
             .HasConversion(new MoneyConverter())
             .HasColumnName("Expected").HasPrecision(18, 2);

            l.Property(p => p.Declared)
             .HasConversion(new MoneyConverter())
             .HasColumnName("Declared").HasPrecision(18, 2);

            l.Ignore(p => p.Variance);
        });

        b.Navigation(x => x.Lines).HasField("_lines")
         .UsePropertyAccessMode(PropertyAccessMode.Field);

        b.OwnsMany(x => x.Counts, c =>
        {
            c.ToTable("TillDeclarationCount");
            c.WithOwner().HasForeignKey("TillDeclarationId");
            c.Property<int>("Id").ValueGeneratedOnAdd();
            c.HasKey("Id");

            c.Property(p => p.Key).HasMaxLength(20).IsRequired();

            c.Property(p => p.Extended)
             .HasConversion(new MoneyConverter())
             .HasColumnName("Extended").HasPrecision(18, 2);
        });

        b.Navigation(x => x.Counts).HasField("_counts")
         .UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasIndex(x => new { x.TillSessionId, x.DeclaredAtUtc });
    }
}

public sealed class TillPayoutConfiguration
    : IEntityTypeConfiguration<TillPayout>
{
    public void Configure(EntityTypeBuilder<TillPayout> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("TillPayout");
        b.HasKey(x => x.Id);

        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Note).HasMaxLength(TillPayout.NoteLength).IsRequired();
        b.Property(x => x.VoidReason).HasMaxLength(TillPayout.NoteLength);
        b.Property(x => x.RowVersion).IsConcurrencyToken();

        b.ComplexProperty(x => x.Amount, m =>
        {
            m.Property(p => p.Amount).HasColumnName("Amount")
                                     .HasPrecision(18, 2);
            m.Property(p => p.Currency).HasColumnName("Currency")
                                       .HasMaxLength(3);
        });

        b.Ignore(x => x.IsVoided);

        // Read a session at a time, by the cash-up and the Z-read.
        b.HasIndex(x => new { x.TillSessionId, x.PaidAtUtc });
    }
}

public sealed class DenominationConfiguration
    : IEntityTypeConfiguration<Denomination>
{
    public void Configure(EntityTypeBuilder<Denomination> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("Denomination");
        b.HasKey(x => x.Id);

        b.Property(x => x.Key).HasMaxLength(20).IsRequired();
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Property(x => x.Value).HasPrecision(18, 2);

        b.HasIndex(x => new { x.Currency, x.Key }).IsUnique();
    }
}
