namespace OrderingSystem.Domain.Menu;

/// <summary>One choice inside a group — "Large", "Extra cheese", "No pickles".</summary>
public class Option
{
    public Guid Id { get; set; }
    public Guid OptionGroupId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Added to the item's base price. Zero is normal ("no pickles"), and negative is allowed
    /// so a removal can genuinely discount the line.
    /// </summary>
    public decimal PriceDeltaUsd { get; set; }

    /// <summary>
    /// How many times this one option may be taken on a single line — 2 permits double cheese.
    /// One means on or off.
    /// </summary>
    public int MaxQuantity { get; set; } = 1;

    public bool IsAvailable { get; set; } = true;

    public int SortOrder { get; set; }

    /// <summary>Soft delete. Order lines keep a nullable reference back to this row.</summary>
    public bool IsDeleted { get; set; }

    public OptionGroup OptionGroup { get; set; } = null!;
}
