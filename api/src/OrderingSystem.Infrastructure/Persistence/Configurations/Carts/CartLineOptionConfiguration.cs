using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderingSystem.Domain.Carts;

namespace OrderingSystem.Infrastructure.Persistence.Configurations.Carts;

internal sealed class CartLineOptionConfiguration : IEntityTypeConfiguration<CartLineOption>
{
    public void Configure(EntityTypeBuilder<CartLineOption> builder)
    {
        builder.ToTable("CartLineOptions");

        // Composite key: one row per option per line. "Double cheese" is Quantity 2, never two rows.
        builder.HasKey(o => new { o.CartLineId, o.OptionId });

        builder.HasOne(o => o.CartLine)
            .WithMany(l => l.SelectedOptions)
            .HasForeignKey(o => o.CartLineId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(o => o.Option)
            .WithMany()
            .HasForeignKey(o => o.OptionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_CartLineOptions_QuantityPositive", "[Quantity] >= 1"));
    }
}
