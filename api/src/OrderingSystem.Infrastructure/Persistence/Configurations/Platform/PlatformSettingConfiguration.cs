using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderingSystem.Domain.Platform;

namespace OrderingSystem.Infrastructure.Persistence.Configurations.Platform;

internal sealed class PlatformSettingConfiguration : IEntityTypeConfiguration<PlatformSetting>
{
    public void Configure(EntityTypeBuilder<PlatformSetting> builder)
    {
        builder.ToTable("PlatformSettings");

        builder.HasKey(s => s.Key);
        builder.Property(s => s.Key).HasMaxLength(Lengths.SettingKey);
        builder.Property(s => s.Value).HasMaxLength(Lengths.SettingValue).IsRequired();
    }
}
