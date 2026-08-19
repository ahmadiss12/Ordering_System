using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderingSystem.Domain.Menu;

namespace OrderingSystem.Infrastructure.Persistence.Configurations.Menu;

internal sealed class OptionGroupConfiguration : IEntityTypeConfiguration<OptionGroup>
{
    public void Configure(EntityTypeBuilder<OptionGroup> builder)
    {
        builder.ToTable("OptionGroups");
        builder.HasKey(g => g.Id);

        builder.Property(g => g.Name).HasMaxLength(Lengths.EntityName).IsRequired();

        builder.HasOne(g => g.Restaurant)
            .WithMany(r => r.OptionGroups)
            .HasForeignKey(g => g.RestaurantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(g => new { g.RestaurantId, g.SortOrder });

        // MinSelect can never exceed MaxSelect. Checked here as well as in the validator, because
        // a validator protects the API and a constraint protects the data from everything else -
        // a migration, a seed script, a manual fix at 2am.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_OptionGroups_SelectRange",
            "[MaxSelect] IS NULL OR [MinSelect] <= [MaxSelect]"));
    }
}
