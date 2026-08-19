using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderingSystem.Domain.Geography;

namespace OrderingSystem.Infrastructure.Persistence.Configurations.Geography;

internal sealed class RestaurantZoneConfiguration : IEntityTypeConfiguration<RestaurantZone>
{
    public void Configure(EntityTypeBuilder<RestaurantZone> builder)
    {
        builder.ToTable("RestaurantZones");
        builder.HasKey(z => new { z.RestaurantId, z.ZoneId });

        builder.Property(z => z.DeliveryFeeUsd)
            .HasPrecision(Precision.MoneyPrecision, Precision.MoneyScale);

        builder.HasOne(z => z.Restaurant)
            .WithMany(r => r.Zones)
            .HasForeignKey(z => z.RestaurantId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict, not Cascade: deleting a zone that restaurants price into should fail loudly
        // rather than silently removing their delivery configuration.
        builder.HasOne(z => z.Zone)
            .WithMany(z => z.RestaurantZones)
            .HasForeignKey(z => z.ZoneId)
            .OnDelete(DeleteBehavior.Restrict);

        // "Which restaurants deliver to this zone?" is the storefront's first query.
        builder.HasIndex(z => z.ZoneId);
    }
}
