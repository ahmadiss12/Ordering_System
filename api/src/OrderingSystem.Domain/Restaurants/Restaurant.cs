using OrderingSystem.Domain.Carts;
using OrderingSystem.Domain.Geography;
using OrderingSystem.Domain.Menu;
using OrderingSystem.Domain.Orders;

namespace OrderingSystem.Domain.Restaurants;

/// <summary>
/// The tenant. Almost every other table carries this row's id, and the API refuses any request
/// whose token points at a different one — see ADR-07.
/// </summary>
public class Restaurant
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Unique, URL-safe. What appears in a storefront link rather than the id.</summary>
    public string Slug { get; set; } = string.Empty;

    public string? Description { get; set; }
    public string? LogoUrl { get; set; }
    public string? CoverUrl { get; set; }
    public string Phone { get; set; } = string.Empty;

    /// <summary>Set by a platform admin. False hides the restaurant entirely.</summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// The restaurant's own switch, for a rush or an unplanned closure. Separate from
    /// <see cref="IsActive"/> so a busy kitchen never needs a platform admin, and separate from
    /// opening hours so it can override them in either direction.
    /// </summary>
    public bool IsAcceptingOrders { get; set; } = true;

    /// <summary>
    /// Current rate, applied to new orders only. Each order snapshots its own copy, so changing
    /// this never rewrites historical settlement.
    /// </summary>
    public decimal CommissionPercent { get; set; }

    /// <summary>Checked against the subtotal, excluding delivery fee.</summary>
    public decimal MinOrderUsd { get; set; }

    /// <summary>Kitchen time. Delivery time comes from the zone and is added on top.</summary>
    public int DefaultPrepMinutes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<RestaurantStaff> Staff { get; } = [];
    public ICollection<RestaurantHours> Hours { get; } = [];
    public ICollection<RestaurantZone> Zones { get; } = [];
    public ICollection<Category> Categories { get; } = [];
    public ICollection<MenuItem> MenuItems { get; } = [];
    public ICollection<OptionGroup> OptionGroups { get; } = [];
    public ICollection<Cart> Carts { get; } = [];
    public ICollection<Order> Orders { get; } = [];
}
