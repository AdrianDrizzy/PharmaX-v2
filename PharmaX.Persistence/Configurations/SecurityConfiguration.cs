using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaX.Domain.Security;

namespace PharmaX.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("User");
        b.HasKey(x => x.Id);

        b.Property(x => x.Username).HasMaxLength(50).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();

        // The encoded hash, salt and parameters included. Long enough for
        // a future algorithm with wider parameters than today's.
        b.Property(x => x.Credential).HasMaxLength(500).IsRequired();

        b.Property(x => x.RegistrationNumber).HasMaxLength(30);
        b.Property(x => x.DiscountLimitPercent).HasPrecision(9, 4);
        b.Property(x => x.RowVersion).IsConcurrencyToken();

        // A read-only IReadOnlyList cannot be a primitive collection in
        // EF Core 8, so the private List behind it is mapped instead and
        // the property is projected from it. The column keeps the
        // property's name; nothing outside this file sees the field.
        b.PrimitiveCollection<List<string>>("_roles")
         .HasField("_roles")
         .HasColumnName("Roles")
         .UsePropertyAccessMode(PropertyAccessMode.Field);

        b.Ignore(x => x.Roles);

        b.HasIndex(x => x.Username).IsUnique();
    }
}

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("Role");
        b.HasKey(x => x.Id);

        b.Property(x => x.Name).HasMaxLength(60).IsRequired();
        b.Property(x => x.Description).HasMaxLength(250);

        // A read-only IReadOnlyList cannot be a primitive collection in
        // EF Core 8, so the private List behind it is mapped instead and
        // the property is projected from it. The column keeps the
        // property's name; nothing outside this file sees the field.
        b.PrimitiveCollection<List<string>>("_permissionCodes")
         .HasField("_permissionCodes")
         .HasColumnName("PermissionCodes")
         .UsePropertyAccessMode(PropertyAccessMode.Field);

        b.Ignore(x => x.PermissionCodes);

        b.HasIndex(x => x.Name).IsUnique();
    }
}

public sealed class PermissionConfiguration
    : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("Permission");
        b.HasKey(x => x.Id);

        b.Property(x => x.Code).HasMaxLength(80).IsRequired();
        b.Property(x => x.Description).HasMaxLength(250);

        b.HasIndex(x => x.Code).IsUnique();
    }
}

public sealed class AuditEntryConfiguration
    : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("AuditEntry");
        b.HasKey(x => x.Id);

        b.Property(x => x.Workstation).HasMaxLength(30).IsRequired();
        b.Property(x => x.Action).HasMaxLength(80).IsRequired();
        b.Property(x => x.EntityType).HasMaxLength(80).IsRequired();
        b.Property(x => x.Outcome).HasMaxLength(20).IsRequired();
        b.Property(x => x.Reason).HasMaxLength(500);

        // Append-only (SEC-031). Nothing in the system revises history, so
        // the whole entity is mapped read-once and never updated.
        b.Property(x => x.BeforeJson);
        b.Property(x => x.AfterJson);

        b.HasIndex(x => x.OccurredAtUtc);
        b.HasIndex(x => new { x.EntityType, x.EntityId });
        b.HasIndex(x => new { x.UserId, x.OccurredAtUtc });

        // The index behind "show me every refusal this month" (SEC-035).
        b.HasIndex(x => new { x.Outcome, x.OccurredAtUtc });
    }
}
