using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderingSystem.Domain.Carts;

namespace OrderingSystem.Infrastructure.Persistence.Configurations.Carts;

internal sealed class CartLineConfiguration : IEntityTypeConfiguration<CartLine>
{
    public void Configure(EntityTypeBuilder<CartLine> builder)
    {
        builder.ToTable("CartLines");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Note).HasMaxLength(Lengths.Note);

        builder.HasOne(l => l.Cart)
            .WithMany(c => c.Lines)
            .HasForeignKey(l => l.CartId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict, and the second path to this table, which is why it cannot also cascade -
        // SQL Server rejects multiple cascade paths to one table.
        builder.HasOne(l => l.MenuItem)
            .WithMany()
            .HasForeignKey(l => l.MenuItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => l.CartId);
        builder.HasIndex(l => l.MenuItemId);

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_CartLines_QuantityPositive", "[Quantity] >= 1"));
    }
}
