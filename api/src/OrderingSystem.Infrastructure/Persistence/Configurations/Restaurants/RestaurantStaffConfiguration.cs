using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderingSystem.Domain.Restaurants;

namespace OrderingSystem.Infrastructure.Persistence.Configurations.Restaurants;

internal sealed class RestaurantStaffConfiguration : IEntityTypeConfiguration<RestaurantStaff>
{
    public void Configure(EntityTypeBuilder<RestaurantStaff> builder)
    {
        builder.ToTable("RestaurantStaff");

        // Composite key: one membership per person per restaurant, but the same person may work
        // at two restaurants with a different role at each.
        builder.HasKey(s => new { s.UserId, s.RestaurantId });

        builder.Property(s => s.StaffRole).HasConversion<int>();

        builder.HasOne(s => s.User)
            .WithMany(u => u.StaffMemberships)
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.Restaurant)
            .WithMany(r => r.Staff)
            .HasForeignKey(s => s.RestaurantId)
            .OnDelete(DeleteBehavior.Cascade);

        // Login reads this to decide the restaurant_id claim.
        builder.HasIndex(s => s.RestaurantId);
    }
}
