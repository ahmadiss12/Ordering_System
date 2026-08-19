namespace OrderingSystem.Domain.Enums;

/// <summary>
/// A platform-wide capability. One account may hold several — a restaurant owner
/// also orders as a customer. Restaurant scope comes from <see cref="Restaurants.RestaurantStaff"/>,
/// not from this enum.
/// </summary>
public enum RoleType
{
    Customer = 1,
    RestaurantStaff = 2,
    RestaurantOwner = 3,
    PlatformAdmin = 4,
}
