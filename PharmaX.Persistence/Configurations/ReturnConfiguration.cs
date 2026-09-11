using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaX.Domain.Sales;

namespace PharmaX.Persistence.Configurations;

public sealed class SaleReturnConfiguration
    : IEntityTypeConfiguration<SaleReturn>
{
    public void Configure(EntityTypeBuilder<SaleReturn> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("SaleReturn");
        b.HasKey(x => x.Id);

        b.Property(x => x.DocumentNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.OriginalDocumentNumber).HasMaxLength(30)
                                                 .IsRequired();
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        b.Property(x => x.RowVersion).IsConcurrencyToken();

        // Computed from the lines and the refunds.
        b.Ignore(x => x.Total);
        b.Ignore(x => x.Refunded);
        b.Ignore(x => x.Restockable);
        b.Ignore(x => x.WriteOffValue);

        b.HasMany(x => x.Lines)
         .WithOne()
         .HasForeignKey(l => l.SaleReturnId)
         .OnDelete(DeleteBehavior.Cascade);

        b.Navigation(x => x.Lines).HasField("_lines")
         .UsePropertyAccessMode(PropertyAccessMode.Field);

        // The refunds are SalePayments: a tender is a tender whichever
        // way the money moved, so they share the payment table under a
        // second foreign key.
        b.HasMany(x => x.Refunds)
         .WithOne()
         .HasForeignKey("SaleReturnId")
         .OnDelete(DeleteBehavior.Cascade);

        b.Navigation(x => x.Refunds).HasField("_refunds")
         .UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasIndex(x => x.DocumentNumber).IsUnique();

        // "What has come back against this receipt" is the query the
        // returns screen runs before it offers anything.
        b.HasIndex(x => x.OriginalSaleId);
        b.HasIndex(x => new { x.TillSessionId, x.OccurredAtUtc });
    }
}

public sealed class SaleReturnLineConfiguration
    : IEntityTypeConfiguration<SaleReturnLine>
{
    public void Configure(EntityTypeBuilder<SaleReturnLine> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("SaleReturnLine");
        b.HasKey(x => x.Id);

        b.Property(x => x.Description).HasMaxLength(200).IsRequired();
        b.Property(x => x.TaxCode).HasMaxLength(5).IsRequired();
        b.Property(x => x.LineReason).HasMaxLength(250);
        b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.Property(x => x.VatRatePercent).HasPrecision(9, 4);

        b.ComplexProperty(x => x.UnitPrice, m =>
        {
            m.Property(p => p.Amount).HasColumnName("UnitPrice")
                                     .HasPrecision(18, 2);
            m.Property(p => p.Currency).HasColumnName("Currency")
                                       .HasMaxLength(3);
        });

        b.Ignore(x => x.LineTotal);

        b.HasIndex(x => new { x.SaleReturnId, x.LineNo }).IsUnique();
        b.HasIndex(x => x.OriginalSaleLineId);
    }
}
