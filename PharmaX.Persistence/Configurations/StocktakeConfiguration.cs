using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaX.Domain.Inventory;

namespace PharmaX.Persistence.Configurations;

public sealed class StocktakeConfiguration : IEntityTypeConfiguration<Stocktake>
{
    public void Configure(EntityTypeBuilder<Stocktake> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("Stocktake");
        b.HasKey(x => x.Id);

        b.Property(x => x.StocktakeNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.Scope).HasMaxLength(200).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.VarianceReason).HasMaxLength(500);
        b.Property(x => x.CancelReason).HasMaxLength(500);
        b.Property(x => x.RowVersion).IsConcurrencyToken();

        // All computed from the lines, so there is nothing to store.
        b.Ignore(x => x.CountedLines);
        b.Ignore(x => x.UncountedLines);
        b.Ignore(x => x.Variances);
        b.Ignore(x => x.NetVarianceQuantity);
        b.Ignore(x => x.NetVarianceValue);
        b.Ignore(x => x.ShrinkageValue);

        b.HasMany(x => x.Lines)
         .WithOne()
         .HasForeignKey(l => l.StocktakeId)
         .OnDelete(DeleteBehavior.Cascade);

        b.Navigation(x => x.Lines).HasField("_lines")
         .UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasIndex(x => x.StocktakeNumber).IsUnique();

        // The one query the screen runs on open: is a count already
        // being taken.
        b.HasIndex(x => new { x.BranchId, x.Status });
    }
}

public sealed class StocktakeLineConfiguration
    : IEntityTypeConfiguration<StocktakeLine>
{
    public void Configure(EntityTypeBuilder<StocktakeLine> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("StocktakeLine");
        b.HasKey(x => x.Id);

        b.Property(x => x.Description).HasMaxLength(200).IsRequired();
        b.Property(x => x.BatchNumber).HasMaxLength(50);
        b.Property(x => x.ExpectedQuantity).HasPrecision(18, 3);

        // Nullable on purpose: a zero and an uncounted line are
        // different answers, and a non-nullable column would lose that.
        b.Property(x => x.CountedQuantity).HasPrecision(18, 3);

        b.ComplexProperty(x => x.UnitCost, m =>
        {
            m.Property(p => p.Amount).HasColumnName("UnitCost")
                                     .HasPrecision(18, 2);
            m.Property(p => p.Currency).HasColumnName("Currency")
                                       .HasMaxLength(3);
        });

        b.Ignore(x => x.IsCounted);
        b.Ignore(x => x.Variance);
        b.Ignore(x => x.VarianceValue);

        b.HasIndex(x => new { x.StocktakeId, x.LineNo }).IsUnique();
        b.HasIndex(x => x.ProductId);
    }
}
