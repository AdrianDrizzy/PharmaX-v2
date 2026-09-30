using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaX.Domain.Sales;

namespace PharmaX.Persistence.Configurations;

public sealed class QuotationConfiguration
    : IEntityTypeConfiguration<Quotation>
{
    public void Configure(EntityTypeBuilder<Quotation> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("Quotation");
        b.HasKey(x => x.Id);

        b.Property(x => x.QuotationNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.CustomerName).HasMaxLength(200).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.RowVersion).IsConcurrencyToken();

        // Computed from the lines, like a sale's total.
        b.Ignore(x => x.Total);

        b.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey("QuotationId")
            .OnDelete(DeleteBehavior.Cascade);

        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        // A quotation number is the customer's reference for it.
        b.HasIndex(x => x.QuotationNumber).IsUnique();
    }
}

public sealed class QuotationLineConfiguration
    : IEntityTypeConfiguration<QuotationLine>
{
    public void Configure(EntityTypeBuilder<QuotationLine> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("QuotationLine");
        b.HasKey(x => x.Id);

        b.Property(x => x.Description).HasMaxLength(200).IsRequired();
        b.Property(x => x.TaxCode).HasMaxLength(5).IsRequired();
        b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.Property(x => x.UnitsInPack).HasPrecision(18, 3);

        b.ComplexProperty(x => x.UnitPrice, m =>
        {
            m.Property(p => p.Amount).HasColumnName("UnitPrice")
                                     .HasPrecision(18, 2);
            m.Property(p => p.Currency).HasColumnName("UnitPriceCurrency")
                                       .HasMaxLength(3);
        });

        b.Ignore(x => x.LineTotal);

        b.HasIndex(x => x.ProductId);
    }
}
