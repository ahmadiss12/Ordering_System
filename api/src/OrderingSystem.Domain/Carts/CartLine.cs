using OrderingSystem.Domain.Menu;

namespace OrderingSystem.Domain.Carts;

/// <summary>
/// One item in the basket, with its chosen options. Two lines of the same item with different
/// options stay separate rows — merging them would lose one customer's choices.
/// </summary>
public class CartLine
{
    public Guid Id { get; set; }
    public Guid CartId { get; set; }
    public Guid MenuItemId { get; set; }

    public int Quantity { get; set; }

    /// <summary>Free text from the customer — "no pickles", "well done".</summary>
    public string? Note { get; set; }

    public Cart Cart { get; set; } = null!;
    public MenuItem MenuItem { get; set; } = null!;
    public ICollection<CartLineOption> SelectedOptions { get; } = [];
}
