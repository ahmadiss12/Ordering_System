using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderingSystem.Domain.Restaurants;

namespace OrderingSystem.Infrastructure.Persistence.Configurations.Restaurants;

internal sealed class RestaurantHoursConfiguration : IEntityTypeConfiguration<RestaurantHours>
{
    public void Configure(EntityTypeBuilder<RestaurantHours> builder)
    {
        builder.ToTable("RestaurantHours");
        builder.HasKey(h => h.Id);

        builder.Property(h => h.DayOfWeek).HasConversion<int>();

        builder.HasOne(h => h.Restaurant)
            .WithMany(r => r.Hours)
            .HasForeignKey(h => h.RestaurantId)
            .OnDelete(DeleteBehavior.Cascade);

        // Not unique: a kitchen that shuts between lunch and dinner has two rows for one day.
        builder.HasIndex(h => new { h.RestaurantId, h.DayOfWeek });
    }
}
