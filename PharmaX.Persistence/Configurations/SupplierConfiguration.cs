using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaX.Domain.Suppliers;

namespace PharmaX.Persistence.Configurations;

public sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("Supplier");
        b.HasKey(x => x.Id);

        b.Property(x => x.Code).HasMaxLength(30).IsRequired();
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.ContactName).HasMaxLength(120);
        b.Property(x => x.Telephone).HasMaxLength(30);
        b.Property(x => x.Email).HasMaxLength(150);
        b.Property(x => x.AccountNumber).HasMaxLength(50);
        b.Property(x => x.RowVersion).IsConcurrencyToken();

        b.HasIndex(x => x.Code).IsUnique();
        b.HasIndex(x => x.Name);
    }
}

public sealed class PurchaseOrderConfiguration
    : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("PurchaseOrder");
        b.HasKey(x => x.Id);

        b.Property(x => x.OrderNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.RowVersion).IsConcurrencyToken();

        // Computed from the lines, so there is nothing to store.
        b.Ignore(x => x.Total);
        b.Ignore(x => x.IsFullyReceived);

        b.HasMany(x => x.Lines)
         .WithOne()
         .HasForeignKey(l => l.PurchaseOrderId)
         .OnDelete(DeleteBehavior.Cascade);

        b.Navigation(x => x.Lines).HasField("_lines")
         .UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasIndex(x => x.OrderNumber).IsUnique();

        // The receiving screen's only query: what is this supplier still
        // expected to deliver.
        b.HasIndex(x => new { x.SupplierId, x.Status });
    }
}

public sealed class PurchaseOrderLineConfiguration
    : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("PurchaseOrderLine");
        b.HasKey(x => x.Id);

        b.Property(x => x.Description).HasMaxLength(200).IsRequired();
        b.Property(x => x.QuantityOrdered).HasPrecision(18, 3);
        b.Property(x => x.QuantityReceived).HasPrecision(18, 3);

        b.ComplexProperty(x => x.UnitCost, m =>
        {
            m.Property(p => p.Amount).HasColumnName("UnitCost")
                                     .HasPrecision(18, 2);
            m.Property(p => p.Currency).HasColumnName("Currency")
                                       .HasMaxLength(3);
        });

        b.Ignore(x => x.LineTotal);
        b.Ignore(x => x.Outstanding);
        b.Ignore(x => x.IsFullyReceived);

        b.HasIndex(x => new { x.PurchaseOrderId, x.LineNo }).IsUnique();
    }
}

public sealed class GoodsReceiptConfiguration
    : IEntityTypeConfiguration<GoodsReceipt>
{
    public void Configure(EntityTypeBuilder<GoodsReceipt> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("GoodsReceipt");
        b.HasKey(x => x.Id);

        b.Property(x => x.ReceiptNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.SupplierInvoiceNumber).HasMaxLength(50);
        b.Property(x => x.RowVersion).IsConcurrencyToken();

        // Nullable, so a converted decimal rather than a complex
        // property: EF Core 8 has no nullable complex types.
        b.Property(x => x.InvoiceTotal)
         .HasConversion(new NullableMoneyConverter())
         .HasColumnName("InvoiceTotal")
         .HasPrecision(18, 2);

        b.Ignore(x => x.Total);
        b.Ignore(x => x.InvoiceVariance);

        b.HasMany(x => x.Lines)
         .WithOne()
         .HasForeignKey(l => l.GoodsReceiptId)
         .OnDelete(DeleteBehavior.Cascade);

        b.Navigation(x => x.Lines).HasField("_lines")
         .UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasIndex(x => x.ReceiptNumber).IsUnique();
        b.HasIndex(x => new { x.SupplierId, x.ReceivedOn });

        // "Which receipt did this supplier invoice come in on" is the
        // query the bookkeeper runs, and it runs it by invoice number.
        b.HasIndex(x => x.SupplierInvoiceNumber);
    }
}

public sealed class GoodsReceiptLineConfiguration
    : IEntityTypeConfiguration<GoodsReceiptLine>
{
    public void Configure(EntityTypeBuilder<GoodsReceiptLine> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("GoodsReceiptLine");
        b.HasKey(x => x.Id);

        b.Property(x => x.Description).HasMaxLength(200).IsRequired();
        b.Property(x => x.BatchNumber).HasMaxLength(50);
        b.Property(x => x.Quantity).HasPrecision(18, 3);

        b.ComplexProperty(x => x.UnitCost, m =>
        {
            m.Property(p => p.Amount).HasColumnName("UnitCost")
                                     .HasPrecision(18, 2);
            m.Property(p => p.Currency).HasColumnName("Currency")
                                       .HasMaxLength(3);
        });

        b.Ignore(x => x.LineTotal);

        b.HasIndex(x => new { x.GoodsReceiptId, x.LineNo }).IsUnique();
        b.HasIndex(x => x.ProductId);
    }
}
