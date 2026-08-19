using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderingSystem.Domain.Geography;

namespace OrderingSystem.Infrastructure.Persistence.Configurations.Geography;

internal sealed class DeliveryZoneConfiguration : IEntityTypeConfiguration<DeliveryZone>
{
    public void Configure(EntityTypeBuilder<DeliveryZone> builder)
    {
        builder.ToTable("DeliveryZones");
        builder.HasKey(z => z.Id);

        builder.Property(z => z.Name).HasMaxLength(Lengths.EntityName).IsRequired();

        // Platform-level list, so two zones called "Achrafieh" would be a data-entry error.
        builder.HasIndex(z => z.Name).IsUnique();
    }
}
