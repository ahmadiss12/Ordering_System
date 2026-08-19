using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderingSystem.Domain.Restaurants;

namespace OrderingSystem.Infrastructure.Persistence.Configurations.Restaurants;

internal sealed class RestaurantConfiguration : IEntityTypeConfiguration<Restaurant>
{
    public void Configure(EntityTypeBuilder<Restaurant> builder)
    {
        builder.ToTable("Restaurants");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name).HasMaxLength(Lengths.RestaurantName).IsRequired();
        builder.Property(r => r.Slug).HasMaxLength(Lengths.Slug).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(Lengths.Description);
        builder.Property(r => r.LogoUrl).HasMaxLength(Lengths.Url);
        builder.Property(r => r.CoverUrl).HasMaxLength(Lengths.Url);
        builder.Property(r => r.Phone).HasMaxLength(Lengths.Phone).IsRequired();

        builder.Property(r => r.CommissionPercent)
            .HasPrecision(Precision.PercentPrecision, Precision.PercentScale);
        builder.Property(r => r.MinOrderUsd)
            .HasPrecision(Precision.MoneyPrecision, Precision.MoneyScale);

        // The slug is what appears in a storefront URL, so it has to resolve to exactly one row.
        builder.HasIndex(r => r.Slug).IsUnique();
    }
}
