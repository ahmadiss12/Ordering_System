using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderingSystem.Domain.Carts;

namespace OrderingSystem.Infrastructure.Persistence.Configurations.Carts;

internal sealed class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.ToTable("Carts");
        builder.HasKey(c => c.Id);

        builder.HasOne(c => c.User)
            .WithMany(u => u.Carts)
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.Restaurant)
            .WithMany(r => r.Carts)
            .HasForeignKey(c => c.RestaurantId)
            .OnDelete(DeleteBehavior.Restrict);

        // One cart per customer per restaurant. Without this the "adding from another restaurant
        // clears your cart" rule could be defeated by a race between two devices.
        builder.HasIndex(c => new { c.UserId, c.RestaurantId }).IsUnique();
    }
}
