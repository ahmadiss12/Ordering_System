using OrderingSystem.Domain.Enums;
using OrderingSystem.Domain.Identity;

namespace OrderingSystem.Domain.Restaurants;

/// <summary>
/// Ties a user to one restaurant. Composite key (UserId, RestaurantId), so a person can work at
/// two restaurants with a different role at each. This row is the source of the RestaurantId
/// claim placed in that user's token.
/// </summary>
public class RestaurantStaff
{
    public Guid UserId { get; set; }
    public Guid RestaurantId { get; set; }

    public StaffRoleType StaffRole { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public User User { get; set; } = null!;
    public Restaurant Restaurant { get; set; } = null!;
}
