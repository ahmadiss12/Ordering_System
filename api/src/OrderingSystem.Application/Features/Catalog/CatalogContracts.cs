namespace OrderingSystem.Application.Features.Catalog;

/// <summary>One restaurant as it appears in a browse list.</summary>
public sealed record RestaurantSummary(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string? LogoUrl,
    decimal MinOrderUsd,
    int DefaultPrepMinutes,
    bool IsAcceptingOrders,
    bool IsOpenNow,
    /// <summary>Present only when the caller filtered by a zone. Null means "not asked".</summary>
    decimal? DeliveryFeeUsd,
    int? EstimatedMinutes);

public sealed record OpeningWindow(DayOfWeek DayOfWeek, TimeOnly OpenTime, TimeOnly CloseTime);

public sealed record ZoneDelivery(Guid ZoneId, string ZoneName, decimal DeliveryFeeUsd, int EstimatedMinutes);

public sealed record RestaurantDetail(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string? LogoUrl,
    string? CoverUrl,
    string Phone,
    decimal MinOrderUsd,
    int DefaultPrepMinutes,
    bool IsAcceptingOrders,
    bool IsOpenNow,
    IReadOnlyList<OpeningWindow> Hours,
    IReadOnlyList<ZoneDelivery> DeliversTo);

// ---------------------------------------------------------------- menu

public sealed record RestaurantMenu(
    Guid RestaurantId, string Name, string Slug, IReadOnlyList<MenuCategory> Categories);

public sealed record MenuCategory(Guid Id, string Name, int SortOrder, IReadOnlyList<MenuItemSummary> Items);

public sealed record MenuItemSummary(
    Guid Id,
    string Name,
    string? Description,
    decimal BasePriceUsd,
    string? ImageUrl,
    bool IsAvailable,
    int SortOrder,
    /// <summary>Lets the client show an item straight from the list when there is nothing to choose.</summary>
    bool HasOptions);

public sealed record MenuItemDetail(
    Guid Id,
    Guid RestaurantId,
    Guid CategoryId,
    string Name,
    string? Description,
    decimal BasePriceUsd,
    string? ImageUrl,
    bool IsAvailable,
    IReadOnlyList<ItemOptionGroup> OptionGroups);

/// <summary>
/// A question asked about this item. <see cref="MinSelect"/> and <see cref="MaxSelect"/> are the
/// values that apply to <em>this</em> item — the per-item override is resolved here rather than
/// handed to the client alongside the group default for it to work out.
/// </summary>
public sealed record ItemOptionGroup(
    Guid Id,
    string Name,
    int MinSelect,
    int? MaxSelect,
    int SortOrder,
    IReadOnlyList<ItemOption> Options);

public sealed record ItemOption(
    Guid Id, string Name, decimal PriceDeltaUsd, int MaxQuantity, bool IsAvailable, int SortOrder);
