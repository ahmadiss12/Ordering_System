namespace OrderingSystem.Application.Features.Catalogue;

/// <summary>One row of the storefront's restaurant list. Deliberately small — the list view shows a card.</summary>
public sealed record RestaurantSummaryResponse(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string? LogoUrl,
    string? CoverUrl,
    decimal MinOrderUsd,
    int DefaultPrepMinutes,
    bool IsAcceptingOrders);

/// <summary>
/// One restaurant's public page: everything a customer needs before opening the menu, including
/// where it delivers and for how much.
/// </summary>
public sealed record RestaurantDetailResponse(
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
    IReadOnlyList<OpeningWindowResponse> Hours,
    IReadOnlyList<DeliveryZoneFeeResponse> Zones);

/// <summary>
/// One opening window. There may be several for the same weekday, and a <see cref="CloseTime"/>
/// earlier than <see cref="OpenTime"/> means the window runs past midnight — the client must not
/// assume one row per day or that close is always later than open.
/// </summary>
public sealed record OpeningWindowResponse(DayOfWeek DayOfWeek, TimeOnly OpenTime, TimeOnly CloseTime);

/// <summary>
/// A zone this restaurant delivers to, on its own terms. A zone absent from this list is one the
/// restaurant does not deliver to at all.
/// </summary>
public sealed record DeliveryZoneFeeResponse(
    Guid ZoneId,
    string ZoneName,
    decimal DeliveryFeeUsd,
    int EstimatedMinutes);

/// <summary>The whole menu in one response — categories, items, and each item's option groups.</summary>
public sealed record MenuResponse(
    Guid RestaurantId,
    string Slug,
    IReadOnlyList<MenuCategoryResponse> Categories);

public sealed record MenuCategoryResponse(
    Guid Id,
    string Name,
    int SortOrder,
    IReadOnlyList<MenuItemResponse> Items);

/// <summary>
/// One dish. <see cref="IsAvailable"/> false means sold out — the item stays in the response and
/// is greyed out by the client, because an item that vanishes reads as a broken menu to a
/// returning customer.
/// </summary>
public sealed record MenuItemResponse(
    Guid Id,
    string Name,
    string? Description,
    decimal BasePriceUsd,
    string? ImageUrl,
    bool IsAvailable,
    int SortOrder,
    IReadOnlyList<MenuOptionGroupResponse> OptionGroups);

/// <summary>
/// A question asked about this item. <see cref="MinSelect"/> and <see cref="MaxSelect"/> are the
/// bounds that actually apply <em>here</em>, with any per-item override already resolved — the
/// override mechanism never reaches the client, only its result.
/// <para>
/// (1,1) is a required radio; (0,null) is optional checkboxes; (0,3) is "choose up to three";
/// (2,2) is "exactly two". Null max means unlimited.
/// </para>
/// </summary>
public sealed record MenuOptionGroupResponse(
    Guid Id,
    string Name,
    int MinSelect,
    int? MaxSelect,
    int SortOrder,
    IReadOnlyList<MenuOptionResponse> Options);

/// <summary>
/// One choice. <see cref="PriceDeltaUsd"/> may be zero ("no pickles") or negative — a removal is
/// allowed to genuinely discount the line. <see cref="MaxQuantity"/> above one permits double cheese.
/// </summary>
public sealed record MenuOptionResponse(
    Guid Id,
    string Name,
    decimal PriceDeltaUsd,
    int MaxQuantity,
    bool IsAvailable,
    int SortOrder);
