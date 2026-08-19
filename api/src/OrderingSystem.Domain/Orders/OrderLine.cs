using OrderingSystem.Domain.Menu;

namespace OrderingSystem.Domain.Orders;

/// <summary>
/// One item as sold. The name and unit price are copies, not lookups: when a restaurant renames a
/// burger or raises its price, this row still shows what the customer bought and paid.
/// </summary>
public class OrderLine
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }

    /// <summary>
    /// Nullable and for reporting only ("how many times did we sell this dish?"). Never read to
    /// display the line — the item may since have been deleted.
    /// </summary>
    public Guid? MenuItemId { get; set; }

    /// <summary>The item's name at the moment of ordering.</summary>
    public string ItemNameSnapshot { get; set; } = string.Empty;

    /// <summary>Base price plus every selected option's delta, per unit.</summary>
    public decimal UnitPriceUsd { get; set; }

    public int Quantity { get; set; }

    /// <summary>UnitPrice × Quantity, stored rather than computed so historical arithmetic cannot drift.</summary>
    public decimal LineTotalUsd { get; set; }

    public string? Note { get; set; }

    public Order Order { get; set; } = null!;
    public MenuItem? MenuItem { get; set; }
    public ICollection<OrderLineOption> SelectedOptions { get; } = [];
}
