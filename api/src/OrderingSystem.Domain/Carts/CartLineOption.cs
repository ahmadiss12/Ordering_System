using OrderingSystem.Domain.Menu;

namespace OrderingSystem.Domain.Carts;

/// <summary>
/// One option chosen on one cart line. Composite key (CartLineId, OptionId), so the same option
/// cannot be selected twice on a line — quantity is what expresses "double cheese".
/// </summary>
public class CartLineOption
{
    public Guid CartLineId { get; set; }
    public Guid OptionId { get; set; }

    /// <summary>Bounded by <see cref="Option.MaxQuantity"/>, validated server-side at checkout.</summary>
    public int Quantity { get; set; } = 1;

    public CartLine CartLine { get; set; } = null!;
    public Option Option { get; set; } = null!;
}
