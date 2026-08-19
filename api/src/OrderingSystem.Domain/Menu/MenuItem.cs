using OrderingSystem.Domain.Restaurants;

namespace OrderingSystem.Domain.Menu;

/// <summary>
/// One sellable dish. Its price here is the base only — option deltas are added on top at
/// checkout, and the total that results is snapshotted onto the order line rather than
/// recomputed later.
/// </summary>
public class MenuItem
{
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }
    public Guid CategoryId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>USD, decimal(10,2). Never float — see the money rules in ARCHITECTURE.md.</summary>
    public decimal BasePriceUsd { get; set; }

    public string? ImageUrl { get; set; }

    /// <summary>
    /// Sold out for now. The item stays visible and greyed out rather than vanishing, because a
    /// disappearing item reads as a broken menu to a returning customer.
    /// </summary>
    public bool IsAvailable { get; set; } = true;

    public int SortOrder { get; set; }

    /// <summary>
    /// Soft delete. A hard delete would orphan every historical order line that sold this item.
    /// Deleted items are excluded by a global query filter, not by each caller remembering.
    /// </summary>
    public bool IsDeleted { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Restaurant Restaurant { get; set; } = null!;
    public Category Category { get; set; } = null!;
    public ICollection<MenuItemOptionGroup> OptionGroups { get; } = [];
}
