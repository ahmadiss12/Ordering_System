using OrderingSystem.Domain.Enums;

namespace OrderingSystem.Application.Features.Orders;

/// <summary>
/// One row in a list, whether that list is a customer's history or a kitchen's queue.
///
/// <para>
/// Both names are carried because both lists want one of them, and which one is obvious from
/// where it is shown. Nobody sees a row they should not: the query filter decides that before
/// this is ever built.
/// </para>
/// </summary>
public sealed record OrderSummaryResponse(
    Guid Id,
    string OrderNumber,
    OrderStatus Status,
    FulfillmentType Fulfillment,
    DateTimeOffset PlacedAt,
    decimal TotalUsd,
    int ItemCount,

    /// <summary>
    /// The window promised when the order was placed, so a queue can say which orders are about
    /// to breach it. Carried on the summary rather than fetched per order: a kitchen screen
    /// refreshes every few seconds, and asking for each order's detail to work out whether it is
    /// late would be one request per row per refresh.
    /// </summary>
    int PromisedMinutesMin,
    int PromisedMinutesMax,

    string RestaurantName,
    string RestaurantSlug,
    string CustomerName,

    /// <summary>
    /// Why the restaurant dropped it, on the row. A history screen scanning yesterday's refusals
    /// is looking for the pattern — three "out of stock" in an hour says something one order at a
    /// time does not — and opening each order to find out would hide exactly that.
    /// </summary>
    RejectionReason? RejectionReason,

    /// <summary>
    /// What this caller could do with this order right now, from the transition table — the same
    /// list the detail carries, on the row, because a kitchen board draws a button on every card
    /// and asking each order for its detail to find out which would be one request per row per
    /// refresh. It is a lookup in a frozen table, not a query, so it costs the database nothing.
    /// </summary>
    IReadOnlyList<OrderStatus> AvailableTransitions);

public sealed record OrderLineOptionResponse(
    string GroupName, string Name, decimal PriceDeltaUsd, int Quantity);

/// <summary>
/// A line as it was sold. Every string and number here is the order's own copy, so it still reads
/// correctly after the menu has moved on.
/// </summary>
public sealed record OrderLineResponse(
    Guid Id,
    string Name,
    int Quantity,
    decimal UnitPriceUsd,
    decimal LineTotalUsd,
    string? Note,
    IReadOnlyList<OrderLineOptionResponse> Options);

/// <summary>One step in the order's life: who moved it, where to, and when.</summary>
public sealed record OrderEventResponse(
    OrderStatus? FromStatus,
    OrderStatus ToStatus,
    string? ChangedBy,
    string? Note,
    DateTimeOffset At);

/// <summary>Where a delivery went, as recorded at the time rather than as it stands today.</summary>
public sealed record DeliveryAddressResponse(
    string? ZoneName, string? Line1, string? Building, string? Floor, string? Landmark);

public sealed record OrderDetailResponse(
    Guid Id,
    string OrderNumber,
    OrderStatus Status,
    FulfillmentType Fulfillment,
    DateTimeOffset PlacedAt,

    string RestaurantName,
    string RestaurantSlug,
    string RestaurantPhone,
    string CustomerName,

    decimal SubtotalUsd,
    decimal DeliveryFeeUsd,
    decimal TaxUsd,
    decimal DiscountUsd,
    decimal TotalUsd,
    /// <summary>At the rate frozen when the order was placed, not today's.</summary>
    decimal? TotalLbp,

    PaymentMethod PaymentMethod,
    PaymentStatus PaymentStatus,

    int PromisedMinutesMin,
    int PromisedMinutesMax,

    string? CustomerNote,
    RejectionReason? RejectionReason,
    string? RejectionNote,

    DeliveryAddressResponse? DeliveryAddress,
    IReadOnlyList<OrderLineResponse> Lines,
    IReadOnlyList<OrderEventResponse> Events,

    /// <summary>
    /// What the caller could do with this order right now, from the transition table. The screen
    /// draws its buttons from this, so a button that would be refused is never rendered.
    /// </summary>
    IReadOnlyList<OrderStatus> AvailableTransitions);
