using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderingSystem.Domain.Menu;

namespace OrderingSystem.Infrastructure.Persistence.Configurations.Menu;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).HasMaxLength(Lengths.EntityName).IsRequired();

        builder.HasOne(c => c.Restaurant)
            .WithMany(r => r.Categories)
            .HasForeignKey(c => c.RestaurantId)
            .OnDelete(DeleteBehavior.Cascade);

        // The menu screen reads categories for one restaurant in display order.
        builder.HasIndex(c => new { c.RestaurantId, c.SortOrder });
    }
}
