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

        // The key is the identity: there is exactly one value per
        // setting on a workstation, so a surrogate would only add a way
        // for two rows to claim the same key.
        b.HasKey(x => x.Key);

        b.Property(x => x.Key).HasMaxLength(100);
        b.Property(x => x.Value).HasMaxLength(500);
    }
}
