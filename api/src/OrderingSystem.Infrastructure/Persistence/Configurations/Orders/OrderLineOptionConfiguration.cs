using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderingSystem.Domain.Orders;

namespace OrderingSystem.Infrastructure.Persistence.Configurations.Orders;

internal sealed class OrderLineOptionConfiguration : IEntityTypeConfiguration<OrderLineOption>
{
    public void Configure(EntityTypeBuilder<OrderLineOption> builder)
    {
        builder.ToTable("OrderLineOptions");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.GroupNameSnapshot).HasMaxLength(Lengths.EntityName).IsRequired();
        builder.Property(o => o.OptionNameSnapshot).HasMaxLength(Lengths.EntityName).IsRequired();

        builder.Property(o => o.PriceDeltaUsd)
            .HasPrecision(Precision.MoneyPrecision, Precision.MoneyScale);

        builder.HasOne(o => o.OrderLine)
            .WithMany(l => l.SelectedOptions)
            .HasForeignKey(o => o.OrderLineId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(o => o.Option)
            .WithMany()
            .HasForeignKey(o => o.OptionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(o => o.OrderLineId);

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_OrderLineOptions_QuantityPositive", "[Quantity] >= 1"));
    }
}
