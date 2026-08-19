using OrderingSystem.Domain.Enums;
using OrderingSystem.Domain.Identity;

namespace OrderingSystem.Domain.Orders;

/// <summary>
/// One status transition, written in the same transaction as the change itself. Append-only.
/// <para>
/// This is what makes the admin timeline possible and average prep time measurable — the
/// duration between Accepted and OutForDelivery is derived from these rows rather than from
/// denormalised timestamps on the order that would need maintaining in step.
/// </para>
/// </summary>
public class OrderEvent
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }

    /// <summary>Null on the first event, where the order came into being at Placed.</summary>
    public OrderStatus? FromStatus { get; set; }

    public OrderStatus ToStatus { get; set; }

    /// <summary>Null when the platform itself made the change rather than a person.</summary>
    public Guid? ChangedByUserId { get; set; }

    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Order Order { get; set; } = null!;
    public User? ChangedByUser { get; set; }
}
