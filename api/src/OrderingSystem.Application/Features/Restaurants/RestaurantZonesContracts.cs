namespace OrderingSystem.Application.Features.Restaurants;

/// <summary>
/// One platform zone, and this restaurant's terms for it.
///
/// <para>
/// Every zone the platform has is listed, served or not — a restaurant cannot pick a zone it does
/// not know exists, and a screen showing only the ones already configured would make adding the
/// first one impossible to find.
/// </para>
/// </summary>
/// <param name="IsServed">
/// Whether orders to this zone are accepted right now. False covers two situations that look the
/// same to a customer: terms were never set, and terms were set and then suspended.
/// </param>
/// <param name="DeliveryFeeUsd">
/// Null when terms have never been set for this zone. A suspended zone keeps its numbers, which is
/// what makes turning it back on for a week one press rather than a re-entry.
/// </param>
public sealed record RestaurantZoneResponse(
    Guid ZoneId,
    string ZoneName,
    bool IsServed,
    decimal? DeliveryFeeUsd,
    int? EstimatedMinutes);

/// <summary>
/// A restaurant's terms for one zone.
///
/// <para>
/// One zone at a time, unlike opening hours, and for a reason rather than by accident: hours are a
/// set with relationships inside it — two windows can clash — so they are only meaningful whole.
/// Zones are independent. Serving Hamra says nothing about Achrafieh, nothing can conflict, and
/// sending ten rows to change one would be a worse contract, not a safer one.
/// </para>
/// </summary>
/// <param name="EstimatedMinutes">
/// Travel time only. What a customer is promised is this plus the restaurant's prep time, which is
/// why it is here and not on the restaurant.
/// </param>
public sealed record SetRestaurantZoneRequest(
    bool IsServed,
    decimal DeliveryFeeUsd,
    int EstimatedMinutes);
