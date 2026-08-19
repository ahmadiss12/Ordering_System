using OrderingSystem.Domain.Restaurants;

namespace OrderingSystem.Domain.Menu;

/// <summary>
/// A reusable question asked about an item — "Size", "Extras", "Choose your sauces". Owned by the
/// restaurant and attached to many items, so "Extras" is defined once rather than cloned per burger.
/// <para>
/// The two selection bounds express every real-world shape without a type column:
/// required radio is (1,1); optional checkboxes are (0, null); "up to 3" is (0,3);
/// "exactly 2 sides" is (2,2).
/// </para>
/// </summary>
public class OptionGroup
{
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Zero means the group is optional. One or more makes it a required choice.</summary>
    public int MinSelect { get; set; }

    /// <summary>Null means unlimited. One makes the group behave as a radio.</summary>
    public int? MaxSelect { get; set; }

    public int SortOrder { get; set; }

    /// <summary>Soft delete, for the same reason menu items have one: order lines reference these.</summary>
    public bool IsDeleted { get; set; }

    public Restaurant Restaurant { get; set; } = null!;
    public ICollection<Option> Options { get; } = [];
    public ICollection<MenuItemOptionGroup> MenuItems { get; } = [];
}
