using OrderingSystem.Domain.Enums;

namespace OrderingSystem.Application.Features.Cart;

/// <summary>
/// What a basket would cost, before anybody commits to it.
///
/// <para>
/// Every figure here is worked out on the server. The storefront shows these numbers and sends
/// none of them back — a client that could name a price could name any price.
/// </para>
/// </summary>
public sealed record QuoteResponse(
    Guid RestaurantId,
    FulfillmentType Fulfillment,
    int ItemCount,
    decimal SubtotalUsd,
    decimal DeliveryFeeUsd,
    decimal TaxUsd,
    decimal DiscountUsd,
    decimal TotalUsd,
    /// <summary>Whole pounds at today's rate, or null when no rate has been set.</summary>
    decimal? TotalLbp,
    int PromisedMinutesMin,
    int PromisedMinutesMax,
    decimal MinOrderUsd,
    bool MeetsMinimum,
    /// <summary>How much more food is needed to reach the minimum. Zero once it is met.</summary>
    decimal ShortfallUsd,
    /// <summary>The basket holds something that cannot currently be ordered, and is not priced.</summary>
    bool HasUnavailableItems,
    /// <summary>Named so the screen can say where it is going. Null for pickup.</summary>
    string? DeliveryZoneName);
