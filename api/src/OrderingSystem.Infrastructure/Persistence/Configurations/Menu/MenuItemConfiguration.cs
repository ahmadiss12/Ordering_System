using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderingSystem.Domain.Menu;

namespace OrderingSystem.Infrastructure.Persistence.Configurations.Menu;

internal sealed class MenuItemConfiguration : IEntityTypeConfiguration<MenuItem>
{
    public void Configure(EntityTypeBuilder<MenuItem> builder)
    {
        builder.ToTable("MenuItems");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Name).HasMaxLength(Lengths.EntityName).IsRequired();
        builder.Property(i => i.Description).HasMaxLength(Lengths.Description);
        builder.Property(i => i.ImageUrl).HasMaxLength(Lengths.Url);

        builder.Property(i => i.BasePriceUsd)
            .HasPrecision(Precision.MoneyPrecision, Precision.MoneyScale);

        // Restrict on Restaurant: a restaurant with menu items should not be deletable, and the
        // failure should be visible rather than silently taking the menu with it.
        builder.HasOne(i => i.Restaurant)
            .WithMany(r => r.MenuItems)
            .HasForeignKey(i => i.RestaurantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Category)
            .WithMany(c => c.MenuItems)
            .HasForeignKey(i => i.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // The single most-requested read in the system: one restaurant's menu, by category,
        // in display order.
        builder.HasIndex(i => new { i.RestaurantId, i.CategoryId, i.SortOrder });
    }
}
