namespace OrderingSystem.Domain.Enums;

/// <summary>
/// Fixed list a restaurant must choose from when refusing an order. A free-text note may
/// accompany it but never replaces it — free text alone cannot be reported on, and the
/// rejection-rate report is what makes a struggling restaurant visible.
/// </summary>
public enum RejectionReason
{
    OutOfStock = 1,
    TooBusy = 2,
    ClosingSoon = 3,
    OutsideDeliveryArea = 4,
    CustomerUnreachable = 5,
    Other = 99,
}
