using OrderingSystem.Domain.Menu;

namespace OrderingSystem.Domain.Orders;

/// <summary>
/// One option as sold. Carries the group name too, so a receipt can still read
/// "Size: Large" after the group has been renamed or deleted.
/// </summary>
public class OrderLineOption
{
    public Guid Id { get; set; }
    public Guid OrderLineId { get; set; }

    /// <summary>Nullable, for reporting only. Same reasoning as OrderLine.MenuItemId.</summary>
    public Guid? OptionId { get; set; }

    public string GroupNameSnapshot { get; set; } = string.Empty;
    public string OptionNameSnapshot { get; set; } = string.Empty;

    /// <summary>Per unit of this option, as it was priced then. May be zero or negative.</summary>
    public decimal PriceDeltaUsd { get; set; }

    public int Quantity { get; set; } = 1;

    public OrderLine OrderLine { get; set; } = null!;
    public Option? Option { get; set; }
}
