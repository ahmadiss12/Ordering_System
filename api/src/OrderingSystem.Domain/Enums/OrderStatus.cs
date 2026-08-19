namespace OrderingSystem.Domain.Enums;

/// <summary>
/// Where an order is in its life. Independent of <see cref="PaymentStatus"/>: an order can be
/// delivered and unpaid (cash pending), or paid and rejected (refund due). Which transitions
/// are legal is declared in one place by the order state machine, not implied by this ordering.
/// </summary>
public enum OrderStatus
{
    Placed = 1,
    Accepted = 2,
    Preparing = 3,

    /// <summary>Pickup orders only. The pickup counterpart of <see cref="OutForDelivery"/>.</summary>
    ReadyForPickup = 4,

    /// <summary>Delivery orders only.</summary>
    OutForDelivery = 5,

    Delivered = 6,

    /// <summary>Refused by the restaurant. Only reachable from <see cref="Placed"/>, and requires a reason.</summary>
    Rejected = 7,

    /// <summary>Withdrawn by the customer. Only reachable from <see cref="Placed"/> or <see cref="Accepted"/>.</summary>
    Cancelled = 8,
}
