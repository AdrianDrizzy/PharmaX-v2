using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaX.Domain.Sales;

namespace PharmaX.Persistence.Configurations;

public sealed class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("Sale");
        b.HasKey(x => x.Id);

        // Optional at the database, required in practice: see the
        // remarks on Sale.DocumentNumber. NULL for a draft is what lets
        // one till hold more than one unfinished sale.
        b.Property(x => x.DocumentNumber).HasMaxLength(30)
         .IsRequired(false);
        b.Property(x => x.OccurredAtUtc).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.RowVersion).IsConcurrencyToken();

        // The money on a Sale is computed from its lines, so there is
        // nothing to store: a persisted total is a second source of truth
        // that can disagree with the lines it was derived from.
        b.Ignore(x => x.Subtotal);
        b.Ignore(x => x.DiscountTotal);
        b.Ignore(x => x.Total);
        b.Ignore(x => x.Tendered);
        b.Ignore(x => x.AmountPaid);
        b.Ignore(x => x.Change);

        // The one money figure on a sale that is not derived from its
        // lines or payments: see Sale.ChangeGiven.
        b.ComplexProperty(x => x.ChangeGiven, m =>
        {
            m.Property(p => p.Amount).HasColumnName("ChangeGiven")
                                     .HasPrecision(18, 2);
            m.Property(p => p.Currency).HasColumnName("ChangeCurrency")
                                       .HasMaxLength(3);
        });
        b.Ignore(x => x.IsRated);
        b.Ignore(x => x.IsCollection);
        b.Ignore(x => x.IsReceiptHidden);
        b.Property(x => x.ReceiptHiddenReason).HasMaxLength(200);

        b.HasMany(x => x.Lines)
         .WithOne()
         .HasForeignKey(l => l.SaleId)
         .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(x => x.Payments)
         .WithOne()
         .HasForeignKey(p => p.SaleId)
         .OnDelete(DeleteBehavior.Cascade);

        // The aggregate owns its collections through private fields; EF
        // must go through those rather than the read-only properties.
        b.Navigation(x => x.Lines).HasField("_lines")
         .UsePropertyAccessMode(PropertyAccessMode.Field);

        b.Navigation(x => x.Payments).HasField("_payments")
         .UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasIndex(x => new { x.BranchId, x.OccurredAtUtc });

        // NET-021 made physical: two workstations cannot produce the same
        // receipt number, and if a bug ever tried, the database refuses.
        b.HasIndex(x => new { x.WorkstationId, x.DocumentNumber }).IsUnique();

        // One sale per dispensing, enforced by the database rather than
        // by two cashiers watching the same collection queue being
        // careful. Nullable, and both providers treat NULLs as distinct,
        // so the over-the-counter sales that carry no dispensing are
        // unaffected.
        b.HasIndex(x => x.DispensingId).IsUnique();

        // Held baskets are listed by status at the till.
        b.HasIndex(x => new { x.Status, x.OccurredAtUtc });
        b.HasIndex(x => x.QuotationId);
    }
}

public sealed class SaleLineConfiguration : IEntityTypeConfiguration<SaleLine>
{
    public void Configure(EntityTypeBuilder<SaleLine> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("SaleLine");
        b.HasKey(x => x.Id);

        b.Property(x => x.Description).HasMaxLength(200).IsRequired();
        b.Property(x => x.TaxCode).HasMaxLength(5).IsRequired();
        b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.Property(x => x.VatRatePercent).HasPrecision(9, 4);
        b.Property(x => x.PriceSource).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.VoidReason).HasMaxLength(250);
        b.Property(x => x.BatchNumber).HasMaxLength(50);

        // Money is a value object: two columns, one property.
        b.ComplexProperty(x => x.UnitPrice, m =>
        {
            m.Property(p => p.Amount).HasColumnName("UnitPrice")
                                     .HasPrecision(18, 2);
            m.Property(p => p.Currency).HasColumnName("UnitPriceCurrency")
                                       .HasMaxLength(3);
        });

        b.ComplexProperty(x => x.DiscountAmount, m =>
        {
            m.Property(p => p.Amount).HasColumnName("DiscountAmount")
                                     .HasPrecision(18, 2);
            m.Property(p => p.Currency).HasColumnName("DiscountCurrency")
                                       .HasMaxLength(3);
        });

        b.Ignore(x => x.GrossBeforeDiscount);
        b.Ignore(x => x.LineTotal);
        b.Ignore(x => x.Vat);
        b.Ignore(x => x.IsRated);

        b.HasIndex(x => new { x.SaleId, x.LineNo }).IsUnique();
        b.HasIndex(x => x.ProductId);
    }
}

public sealed class SalePaymentConfiguration
    : IEntityTypeConfiguration<SalePayment>
{
    public void Configure(EntityTypeBuilder<SalePayment> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("SalePayment");
        b.HasKey(x => x.Id);

        b.Property(x => x.Tender).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Reference).HasMaxLength(100);

        b.ComplexProperty(x => x.Amount, m =>
        {
            m.Property(p => p.Amount).HasColumnName("Amount")
                                     .HasPrecision(18, 2);
            m.Property(p => p.Currency).HasColumnName("Currency")
                                       .HasMaxLength(3);
        });

        b.HasIndex(x => x.SaleId);
    }
}
