using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaX.Domain.Reference;

namespace PharmaX.Persistence.Configurations;

/// <summary>
/// The four maintained lists share a shape, so they share their mapping
/// too. Each still gets its own table: they are different things that
/// happen to be described the same way, and one table with a "kind"
/// column would put a foreign key from Product to a row that might be a
/// medical aid society.
/// </summary>
internal static class ReferenceMapping
{
    public static void Configure<T>(EntityTypeBuilder<T> b, string table)
        where T : ReferenceEntry
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable(table);
        b.HasKey(x => x.Id);

        b.Property(x => x.Code).HasMaxLength(30).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Detail).HasMaxLength(400);
        b.Property(x => x.RowVersion).IsConcurrencyToken();

        // The code is what staff type, so two entries may not share one.
        b.HasIndex(x => x.Code).IsUnique();
    }
}

public sealed class DepartmentConfiguration
    : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder) =>
        ReferenceMapping.Configure(builder, "Department");
}

public sealed class CategoryConfiguration
    : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder) =>
        ReferenceMapping.Configure(builder, "Category");
}

public sealed class ManufacturerConfiguration
    : IEntityTypeConfiguration<Manufacturer>
{
    public void Configure(EntityTypeBuilder<Manufacturer> builder) =>
        ReferenceMapping.Configure(builder, "Manufacturer");
}

public sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder) =>
        ReferenceMapping.Configure(builder, "Branch");
}

public sealed class MedicalAidSchemeConfiguration
    : IEntityTypeConfiguration<MedicalAidScheme>
{
    public void Configure(EntityTypeBuilder<MedicalAidScheme> builder) =>
        ReferenceMapping.Configure(builder, "MedicalAidScheme");
}

public sealed class TariffConfiguration : IEntityTypeConfiguration<Tariff>
{
    public void Configure(EntityTypeBuilder<Tariff> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("Tariff");
        b.HasKey(x => x.Id);

        b.Property(x => x.RowVersion).IsConcurrencyToken();

        b.ComplexProperty(x => x.Price, m =>
        {
            m.Property(p => p.Amount).HasColumnName("Price")
                                     .HasPrecision(18, 2);
            m.Property(p => p.Currency).HasColumnName("PriceCurrency")
                                       .HasMaxLength(3);
        });

        b.ComplexProperty(x => x.DispensingFee, m =>
        {
            m.Property(p => p.Amount).HasColumnName("DispensingFee")
                                     .HasPrecision(18, 2);
            m.Property(p => p.Currency).HasColumnName("FeeCurrency")
                                       .HasMaxLength(3);
        });

        // Priced by society and product, which is how every lookup
        // reaches it.
        b.HasIndex(x => new { x.SchemeId, x.ProductId });
    }
}

public sealed class PrescriberConfiguration
    : IEntityTypeConfiguration<Prescriber>
{
    public void Configure(EntityTypeBuilder<Prescriber> builder) =>
        ReferenceMapping.Configure(builder, "Prescriber");
}

public sealed class DosageCodeConfiguration
    : IEntityTypeConfiguration<DosageCode>
{
    public void Configure(EntityTypeBuilder<DosageCode> builder) =>
        ReferenceMapping.Configure(builder, "DosageCode");
}
