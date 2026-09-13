using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PharmaX.Persistence.Configurations;

public sealed class WorkstationSettingConfiguration
    : IEntityTypeConfiguration<WorkstationSetting>
{
    public void Configure(EntityTypeBuilder<WorkstationSetting> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("WorkstationSetting");

        // Workstation and key together are the identity: exactly one
        // value per setting per till. A surrogate would only add a way
        // for two rows to claim the same pair, and the till has to be in
        // the key or a shared database would give every machine in the
        // pharmacy the same printer.
        b.HasKey(x => new { x.WorkstationCode, x.Key });

        b.Property(x => x.WorkstationCode).HasMaxLength(20);
        b.Property(x => x.Key).HasMaxLength(100);
        b.Property(x => x.Value).HasMaxLength(500);
    }
}
