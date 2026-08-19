using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderingSystem.Domain.Orders;

namespace OrderingSystem.Infrastructure.Persistence.Configurations.Orders;

internal sealed class OrderNumberSequenceConfiguration : IEntityTypeConfiguration<OrderNumberSequence>
{
    public void Configure(EntityTypeBuilder<OrderNumberSequence> builder)
    {
        builder.ToTable("OrderNumberSequences");

        // The composite key is also the lookup: allocation is a single UPDATE ... OUTPUT keyed on
        // exactly this pair, so it seeks one row and holds a lock for the length of that statement.
        builder.HasKey(s => new { s.RestaurantId, s.BusinessDate });

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_OrderNumberSequences_NextValuePositive", "[NextValue] >= 1"));
    }
}
