using OrderingSystem.Domain.Restaurants;

namespace OrderingSystem.Domain.Menu;

/// <summary>
/// A flat, ordered grouping inside one restaurant's menu — Burgers, Loaded Fries, Drinks.
/// Deliberately not nestable: no restaurant menu in this market needs a tree, and a tree costs
/// recursive queries on the single most-requested endpoint in the system.
/// </summary>
public class Category
{
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Display position. The menu is never sorted alphabetically — sequence is a choice the restaurant makes.</summary>
    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public Restaurant Restaurant { get; set; } = null!;
    public ICollection<MenuItem> MenuItems { get; } = [];
}
