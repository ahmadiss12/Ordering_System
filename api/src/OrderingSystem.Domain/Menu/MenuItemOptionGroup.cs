namespace OrderingSystem.Domain.Menu;

/// <summary>
/// Attaches a shared group to one item. Composite key (MenuItemId, OptionGroupId).
/// <para>
/// The two override columns are why a group can be shared at all. Limits normally live on the
/// group, which would force every item using "Extras" to accept the same bounds; a burger
/// allowing three sauces and a platter allowing five would otherwise require two duplicate
/// groups. Null inherits the group's value, a number applies to this item alone.
/// </para>
/// </summary>
public class MenuItemOptionGroup
{
    public Guid MenuItemId { get; set; }
    public Guid OptionGroupId { get; set; }

    /// <summary>Position of this group on this item's detail screen.</summary>
    public int SortOrder { get; set; }

    /// <summary>Null inherits <see cref="OptionGroup.MinSelect"/>.</summary>
    public int? MinSelectOverride { get; set; }

    /// <summary>
    /// Null inherits <see cref="OptionGroup.MaxSelect"/>. Note that an inherited null and an
    /// overriding null are the same value, so removing a cap for one item means clearing the
    /// override, not setting it to null.
    /// </summary>
    public int? MaxSelectOverride { get; set; }

    public MenuItem MenuItem { get; set; } = null!;
    public OptionGroup OptionGroup { get; set; } = null!;

    /// <summary>Bounds actually applied to this item, after overrides.</summary>
    public int EffectiveMinSelect => MinSelectOverride ?? OptionGroup.MinSelect;

    /// <summary>Bounds actually applied to this item, after overrides. Null means unlimited.</summary>
    public int? EffectiveMaxSelect => MaxSelectOverride ?? OptionGroup.MaxSelect;
}
