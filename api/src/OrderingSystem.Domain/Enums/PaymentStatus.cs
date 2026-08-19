namespace OrderingSystem.Domain.Enums;

public enum PaymentStatus
{
    /// <summary>Cash orders start here and stay until delivery.</summary>
    Pending = 1,

    /// <summary>Online orders must reach this before the order may be placed.</summary>
    Paid = 2,

    Failed = 3,

    /// <summary>A status change only. No money moves in v1.</summary>
    Refunded = 4,
}
