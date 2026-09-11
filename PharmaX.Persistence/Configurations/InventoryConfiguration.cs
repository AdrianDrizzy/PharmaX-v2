using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaX.Domain.Batches;
using PharmaX.Domain.Inventory;

namespace PharmaX.Persistence.Configurations;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("Product");
        b.HasKey(x => x.Id);

        b.Property(x => x.Code).HasMaxLength(30).IsRequired();
        b.Property(x => x.Description).HasMaxLength(200).IsRequired();
        b.Property(x => x.GenericName).HasMaxLength(200);
        b.Property(x => x.TaxCode).HasMaxLength(5).IsRequired();
        b.Property(x => x.PackSize).HasMaxLength(50);
        b.Property(x => x.VatRatePercent).HasPrecision(9, 4);
        b.Property(x => x.ReorderLevel).HasPrecision(18, 3);
        b.Property(x => x.QuantityOnHand).HasPrecision(18, 3);
        b.Property(x => x.RowVersion).IsConcurrencyToken();

        b.ComplexProperty(x => x.RetailPrice, m =>
        {
            m.Property(p => p.Amount).HasColumnName("RetailPrice")
                                     .HasPrecision(18, 2);
            m.Property(p => p.Currency).HasColumnName("RetailCurrency")
                                       .HasMaxLength(3);
        });

        b.ComplexProperty(x => x.CostPrice, m =>
        {
            m.Property(p => p.Amount).HasColumnName("CostPrice")
                                     .HasPrecision(18, 2);
            m.Property(p => p.Currency).HasColumnName("CostCurrency")
                                       .HasMaxLength(3);
        });

        // A short list of alternative codes for the same box. Stored as a
        // primitive collection rather than a table, because nothing ever
        // queries a barcode row on its own.
        // A read-only IReadOnlyList cannot be a primitive collection in
        // EF Core 8, so the private List behind it is mapped instead and
        // the property is projected from it. The column keeps the
        // property's name; nothing outside this file sees the field.
        b.PrimitiveCollection<List<string>>("_barcodes")
         .HasField("_barcodes")
         .HasColumnName("Barcodes")
         .UsePropertyAccessMode(PropertyAccessMode.Field);

        b.Ignore(x => x.Barcodes);

        b.Ignore(x => x.RequiresPharmacist);
        b.Ignore(x => x.IsBelowReorderLevel);

        b.HasMany(x => x.Prices)
         .WithOne()
         .HasForeignKey(p => p.ProductId)
         .OnDelete(DeleteBehavior.Cascade);

        b.Navigation(x => x.Prices).HasField("_prices")
         .UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasIndex(x => x.Code).IsUnique();
        b.HasIndex(x => x.Description);
    }
}

public sealed class ProductPriceConfiguration
    : IEntityTypeConfiguration<ProductPrice>
{
    public void Configure(EntityTypeBuilder<ProductPrice> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("ProductPrice");
        b.HasKey(x => x.Id);

        b.Property(x => x.Source).HasConversion<string>().HasMaxLength(30);

        b.ComplexProperty(x => x.Amount, m =>
        {
            m.Property(p => p.Amount).HasColumnName("Amount")
                                     .HasPrecision(18, 2);
            m.Property(p => p.Currency).HasColumnName("Currency")
                                       .HasMaxLength(3);
        });

        b.HasIndex(x => new { x.ProductId, x.Source });
    }
}

public sealed class BatchConfiguration : IEntityTypeConfiguration<Batch>
{
    public void Configure(EntityTypeBuilder<Batch> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("Batch");
        b.HasKey(x => x.Id);

        b.Property(x => x.BatchNumber).HasMaxLength(50).IsRequired();
        b.Property(x => x.QuantityOnHand).HasPrecision(18, 3);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.StatusReason).HasMaxLength(250);
        b.Property(x => x.RowVersion).IsConcurrencyToken();

        b.ComplexProperty(x => x.CostPrice, m =>
        {
            m.Property(p => p.Amount).HasColumnName("CostPrice")
                                     .HasPrecision(18, 2);
            m.Property(p => p.Currency).HasColumnName("CostCurrency")
                                       .HasMaxLength(3);
        });

        b.HasIndex(x => new { x.ProductId, x.BatchNumber }).IsUnique();

        // The index FEFO allocation runs on, every time a batch-tracked
        // item is scanned. Ordering by expiry is the hot path at the till.
        b.HasIndex(x => new { x.ProductId, x.Status, x.ExpiryDate });
    }
}

public sealed class StockMovementConfiguration
    : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("StockMovement");
        b.HasKey(x => x.Id);

        b.Property(x => x.MovementType).HasConversion<string>()
                                       .HasMaxLength(20);
        b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.Property(x => x.BalanceAfter).HasPrecision(18, 3);
        b.Property(x => x.ReasonCode).HasMaxLength(30);
        b.Property(x => x.SourceDocumentType).HasMaxLength(30);
        b.Property(x => x.RowVersion).IsConcurrencyToken();

        b.HasIndex(x => new { x.ProductId, x.OccurredAtUtc });
        b.HasIndex(x => new { x.SourceDocumentType, x.SourceDocumentId });
    }
}
