using OrderingSystem.Domain.Identity;

namespace OrderingSystem.Domain.Platform;

/// <summary>
/// One LBP-per-USD rate, effective from a moment. Append-only: a correction is a new row, never
/// an update, so the rate in force on any past date stays recoverable.
/// </summary>
public class ExchangeRate
{
    public Guid Id { get; set; }

    /// <summary>Lebanese pounds per one US dollar.</summary>
    public decimal RateLbpPerUsd { get; set; }

    public DateTimeOffset EffectiveFrom { get; set; }

    public Guid SetByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public User SetByUser { get; set; } = null!;
}
