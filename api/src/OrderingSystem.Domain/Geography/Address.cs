using OrderingSystem.Domain.Identity;

namespace OrderingSystem.Domain.Geography;

/// <summary>
/// A saved customer address. Lebanon has no dependable street addressing, so the useful fields
/// are the zone (which drives fee and coverage) and the landmark (which is what a driver
/// actually navigates by). Coordinates are a bonus, never a requirement.
/// </summary>
public class Address
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ZoneId { get; set; }

    /// <summary>The customer's own name for it — "Home", "Office".</summary>
    public string Label { get; set; } = string.Empty;

    public string Line1 { get; set; } = string.Empty;
    public string? Building { get; set; }
    public string? Floor { get; set; }

    /// <summary>"Above the pharmacy, opposite the church." In practice the most useful field here.</summary>
    public string? Landmark { get; set; }

    public decimal? Lat { get; set; }
    public decimal? Lng { get; set; }

    /// <summary>At most one per user; enforced by a filtered unique index.</summary>
    public bool IsDefault { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Soft delete. Orders reference the address they were delivered to, so removing the row
    /// would erase where a past order went.
    /// </summary>
    public bool IsDeleted { get; set; }

    public User User { get; set; } = null!;
    public DeliveryZone Zone { get; set; } = null!;
}
