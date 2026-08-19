using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderingSystem.Domain.Menu;

namespace OrderingSystem.Infrastructure.Persistence.Configurations.Menu;

internal sealed class MenuItemOptionGroupConfiguration : IEntityTypeConfiguration<MenuItemOptionGroup>
{
    public void Configure(EntityTypeBuilder<MenuItemOptionGroup> builder)
    {
        builder.ToTable("MenuItemOptionGroups");
        builder.HasKey(x => new { x.MenuItemId, x.OptionGroupId });

        builder.HasOne(x => x.MenuItem)
            .WithMany(i => i.OptionGroups)
            .HasForeignKey(x => x.MenuItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.OptionGroup)
            .WithMany(g => g.MenuItems)
            .HasForeignKey(x => x.OptionGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        // The computed EffectiveMinSelect / EffectiveMaxSelect read through the OptionGroup
        // navigation, so they are not columns and must not be mapped.
        builder.Ignore(x => x.EffectiveMinSelect);
        builder.Ignore(x => x.EffectiveMaxSelect);

        builder.HasIndex(x => x.OptionGroupId);

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_MenuItemOptionGroups_SelectRange",
            "[MaxSelectOverride] IS NULL OR [MinSelectOverride] IS NULL "
            + "OR [MinSelectOverride] <= [MaxSelectOverride]"));
    }
}
