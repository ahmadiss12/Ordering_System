using OrderingSystem.Domain.Identity;
using OrderingSystem.Domain.Restaurants;

namespace OrderingSystem.Domain.Carts;

/// <summary>
/// A customer's in-progress basket at one restaurant. Held server-side so it survives an app
/// restart or a move between phone and laptop.
/// <para>
/// Deliberately stores no prices. Prices are read live from the menu on every view and frozen
/// only at checkout — a cart that carried its own prices would let a customer hold a stale
/// price for as long as they liked.
/// </para>
/// </summary>
public class Cart
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid RestaurantId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public User User { get; set; } = null!;
    public Restaurant Restaurant { get; set; } = null!;
    public ICollection<CartLine> Lines { get; } = [];
}
