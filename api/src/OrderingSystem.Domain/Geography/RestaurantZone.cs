using OrderingSystem.Domain.Restaurants;

namespace OrderingSystem.Domain.Geography;

/// <summary>
/// One restaurant's terms for one zone. Composite key (RestaurantId, ZoneId). Two restaurants
/// may charge different fees into the same zone, and a zone a restaurant has no row for is
/// simply not delivered to.
/// </summary>
public class RestaurantZone
{
    public Guid RestaurantId { get; set; }
    public Guid ZoneId { get; set; }

    public decimal DeliveryFeeUsd { get; set; }

    /// <summary>Travel time only. The customer's estimate is this plus the restaurant's prep time.</summary>
    public int EstimatedMinutes { get; set; }

    /// <summary>Lets a restaurant suspend a zone without losing its configured fee.</summary>
    public bool IsActive { get; set; } = true;

    public Restaurant Restaurant { get; set; } = null!;
    public DeliveryZone Zone { get; set; } = null!;
}
