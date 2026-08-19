using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderingSystem.Domain.Orders;

namespace OrderingSystem.Infrastructure.Persistence.Configurations.Orders;

internal sealed class OrderEventConfiguration : IEntityTypeConfiguration<OrderEvent>
{
    public void Configure(EntityTypeBuilder<OrderEvent> builder)
    {
        builder.ToTable("OrderEvents");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.FromStatus).HasConversion<int>();
        builder.Property(e => e.ToStatus).HasConversion<int>();
        builder.Property(e => e.Note).HasMaxLength(Lengths.Note);

        builder.HasOne(e => e.Order)
            .WithMany(o => o.Events)
            .HasForeignKey(e => e.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.ChangedByUser)
            .WithMany()
            .HasForeignKey(e => e.ChangedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // The admin timeline, and the source of average-prep-time reporting.
        builder.HasIndex(e => new { e.OrderId, e.CreatedAt });
    }
}
