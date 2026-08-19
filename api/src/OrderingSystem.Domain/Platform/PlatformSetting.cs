namespace OrderingSystem.Domain.Platform;

/// <summary>
/// Platform-wide configuration a platform admin can change without a deploy — the default
/// commission offered to a new restaurant, and how long an unanswered order may sit before it is
/// flagged. Keyed by name and stored as text, parsed by the caller.
/// <para>
/// Kept out of appsettings because these are business values an admin edits at runtime, and out
/// of their own typed columns because there are only a handful and they change shape rarely.
/// The exchange rate is the exception: it needs history, so it gets a real table.
/// </para>
/// </summary>
public class PlatformSetting
{
    /// <summary>Primary key. See <see cref="PlatformSettingKeys"/> for the known names.</summary>
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? UpdatedByUserId { get; set; }
}

/// <summary>The setting names the application actually reads, so they are not scattered as literals.</summary>
public static class PlatformSettingKeys
{
    /// <summary>Commission percent proposed for a newly created restaurant. Decimal, e.g. "15.00".</summary>
    public const string DefaultCommissionPercent = "default_commission_percent";

    /// <summary>Minutes an order may sit in Placed before it is flagged for admin attention. Integer.</summary>
    public const string StaleOrderThresholdMinutes = "stale_order_threshold_minutes";
}
