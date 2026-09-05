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
    int? EstimatedMinutes,
    /// <summary>
    /// When this kitchen next opens, or null while it is open — and null too for one that has no
    /// hours at all, which is a restaurant on holiday.
    /// </summary>
    NextOpening? NextOpening);

/// <summary>
/// The next time a shut kitchen opens.
/// </summary>
/// <param name="DaysAway">
/// 0 for later today, 1 for tomorrow, counted in the restaurant's own week rather than the
/// reader's. Worked out on this side because only this side knows what day it is where the
/// kitchen is; a browser in another timezone would arrive at a different answer from the same
/// day and time.
/// </param>
public sealed record NextOpening(DayOfWeek Day, TimeOnly Time, int DaysAway);

/// <summary>
/// One window a restaurant is open in, as a customer reads it.
///
/// <para>
/// Named <c>CatalogOpeningWindow</c> rather than <c>OpeningWindow</c> because the hours editor
/// has a record of that name already. OpenAPI schema ids are a flat namespace where C# namespaces
/// are not, so the two collapsed into one schema and the editor's shape won — leaving the
/// generated client describing this one's <c>dayOfWeek</c> as <c>day</c>, a field that is not
/// there. It went unnoticed because nothing had read a restaurant's hours from the public side
/// until the storefront did. ContractNameCollisionTests now fails the build on the next one.
/// </para>
/// </summary>
public sealed record CatalogOpeningWindow(DayOfWeek DayOfWeek, TimeOnly OpenTime, TimeOnly CloseTime);

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
    IReadOnlyList<CatalogOpeningWindow> Hours,
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
