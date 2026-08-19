using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderingSystem.Domain.Enums;
using OrderingSystem.Domain.Orders;

namespace OrderingSystem.Infrastructure.Persistence.Configurations.Orders;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.OrderNumber).HasMaxLength(Lengths.OrderNumber).IsRequired();
        builder.Property(o => o.CustomerNote).HasMaxLength(Lengths.Note);
        builder.Property(o => o.RejectionNote).HasMaxLength(Lengths.Note);

        builder.Property(o => o.FulfillmentType).HasConversion<int>();
        builder.Property(o => o.Status).HasConversion<int>();
        builder.Property(o => o.PaymentMethod).HasConversion<int>();
        builder.Property(o => o.PaymentStatus).HasConversion<int>();
        builder.Property(o => o.RejectionReason).HasConversion<int>();

        // --- delivery address, snapshotted at placement ---
        builder.Property(o => o.DeliveryZoneName).HasMaxLength(Lengths.EntityName);
        builder.Property(o => o.DeliveryLine1).HasMaxLength(Lengths.AddressLine);
        builder.Property(o => o.DeliveryBuilding).HasMaxLength(Lengths.AddressPart);
        builder.Property(o => o.DeliveryFloor).HasMaxLength(Lengths.AddressPart);
        builder.Property(o => o.DeliveryLandmark).HasMaxLength(Lengths.AddressLine);
        builder.Property(o => o.DeliveryLat)
            .HasPrecision(Precision.CoordinatePrecision, Precision.CoordinateScale);
        builder.Property(o => o.DeliveryLng)
            .HasPrecision(Precision.CoordinatePrecision, Precision.CoordinateScale);

        // --- money ---
        foreach (var money in new[] { nameof(Order.SubtotalUsd), nameof(Order.DeliveryFeeUsd),
                 nameof(Order.TaxUsd), nameof(Order.DiscountUsd), nameof(Order.TotalUsd),
                 nameof(Order.CommissionUsd) })
        {
            builder.Property<decimal>(money)
                .HasPrecision(Precision.MoneyPrecision, Precision.MoneyScale);
        }

        builder.Property(o => o.CommissionPercent)
            .HasPrecision(Precision.PercentPrecision, Precision.PercentScale);
        builder.Property(o => o.ExchangeRateLbp)
            .HasPrecision(Precision.ExchangeRatePrecision, Precision.ExchangeRateScale);

        // --- concurrency ---
        // Two staff pressing Accept on two tablets: the second SaveChanges throws
        // DbUpdateConcurrencyException instead of both appearing to succeed.
        builder.Property(o => o.RowVersion).IsRowVersion();

        // --- relationships ---
        builder.HasOne(o => o.Customer)
            .WithMany()
            .HasForeignKey(o => o.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.Restaurant)
            .WithMany(r => r.Orders)
            .HasForeignKey(o => o.RestaurantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Nullable and Restrict: the order carries its own address snapshot, so this link exists
        // for reporting only and must never take an order with it.
        builder.HasOne(o => o.Address)
            .WithMany()
            .HasForeignKey(o => o.AddressId)
            .OnDelete(DeleteBehavior.Restrict);

        // --- indexes ---
        builder.HasIndex(o => o.OrderNumber).IsUnique();

        // The whole point of the idempotency key. A double-tap on a poor connection hits this
        // index and returns the original order instead of creating a second one.
        builder.HasIndex(o => o.IdempotencyKey).IsUnique();

        // The restaurant dashboard's live queue.
        builder.HasIndex(o => new { o.RestaurantId, o.Status, o.PlacedAt });

        // A customer's order history, newest first.
        builder.HasIndex(o => new { o.CustomerId, o.PlacedAt });

        // The background scan for orders nobody has answered.
        builder.HasIndex(o => new { o.Status, o.PlacedAt });

        // --- invariants the database enforces on its own ---
        // The integer literals are the enum values pinned in Domain/Enums, which a Domain test
        // guards against renumbering.
        builder.ToTable(t =>
        {
            t.HasCheckConstraint(
                "CK_Orders_RejectionReasonRequired",
                $"[Status] <> {(int)OrderStatus.Rejected} OR [RejectionReason] IS NOT NULL");

            t.HasCheckConstraint(
                "CK_Orders_DeliveryNeedsAddress",
                $"[FulfillmentType] <> {(int)FulfillmentType.Delivery} OR [DeliveryLine1] IS NOT NULL");

            t.HasCheckConstraint(
                "CK_Orders_TotalsNonNegative",
                "[SubtotalUsd] >= 0 AND [DeliveryFeeUsd] >= 0 AND [TaxUsd] >= 0 "
                + "AND [DiscountUsd] >= 0 AND [TotalUsd] >= 0 AND [CommissionUsd] >= 0");
        });
    }
}
