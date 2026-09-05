namespace OrderingSystem.Application.Features.Platform;

/// <summary>
/// One restaurant as the platform sees it — which is every restaurant, including the ones no
/// customer can currently find.
/// </summary>
/// <param name="IsActive">
/// The platform's switch. False hides the restaurant from customers entirely; it does not stop
/// its staff finishing what is already cooking.
/// </param>
/// <param name="IsAcceptingOrders">
/// The restaurant's own switch, shown but not settable here. An admin looking at a restaurant
/// nobody can order from should be able to tell whether that is the platform's doing or the
/// kitchen's, and switching the listing back on would not help if the kitchen is paused.
/// </param>
/// <param name="LiveOrderCount">
/// Orders placed and not yet finished. It is on this list because it is what makes the switch a
/// decision rather than a reflex: hiding a restaurant with nine orders cooking is a different act
/// from hiding one with none, and nothing else on the screen would say so.
/// </param>
public sealed record PlatformRestaurantResponse(
    Guid Id,
    string Name,
    string Slug,
    string Phone,
    bool IsActive,
    bool IsAcceptingOrders,
    decimal CommissionPercent,
    int LiveOrderCount,
    DateTimeOffset CreatedAt);

/// <param name="CommissionPercent">
/// Applies to orders placed after it is saved and to no earlier one. Every order carries the rate
/// it was charged at, so this never restates what a restaurant has already been billed.
/// </param>
public sealed record SetCommissionRequest(decimal CommissionPercent);

public sealed record SetListingRequest(bool IsActive);
