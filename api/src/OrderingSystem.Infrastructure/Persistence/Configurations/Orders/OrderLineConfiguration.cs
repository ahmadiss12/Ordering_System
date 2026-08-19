using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderingSystem.Domain.Orders;

namespace OrderingSystem.Infrastructure.Persistence.Configurations.Orders;

internal sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        builder.ToTable("OrderLines");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.ItemNameSnapshot).HasMaxLength(Lengths.EntityName).IsRequired();
        builder.Property(l => l.Note).HasMaxLength(Lengths.Note);

        builder.Property(l => l.UnitPriceUsd)
            .HasPrecision(Precision.MoneyPrecision, Precision.MoneyScale);
        builder.Property(l => l.LineTotalUsd)
            .HasPrecision(Precision.MoneyPrecision, Precision.MoneyScale);

        builder.HasOne(l => l.Order)
            .WithMany(o => o.Lines)
            .HasForeignKey(l => l.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // Nullable, for reporting only. SetNull rather than Restrict so that if an item ever is
        // hard-deleted, the line survives on its snapshot instead of blocking the delete.
        builder.HasOne(l => l.MenuItem)
            .WithMany()
            .HasForeignKey(l => l.MenuItemId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(l => l.OrderId);
        builder.HasIndex(l => l.MenuItemId);

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_OrderLines_QuantityPositive", "[Quantity] >= 1"));
    }
}
