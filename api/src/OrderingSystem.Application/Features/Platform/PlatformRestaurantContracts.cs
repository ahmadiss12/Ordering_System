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

/// <summary>
/// A restaurant the platform is taking on, and the person who will run it.
/// </summary>
/// <param name="Slug">
/// What appears in a customer's link. Left out, it is derived from the name — which is what an
/// admin typing a restaurant's name usually wants, and saves them inventing a URL. Given, it is
/// used as typed, because a restaurant with an awkward name should be able to have a tidy link.
/// </param>
/// <param name="OwnerEmail">
/// Who is being handed the restaurant. Invited exactly as any other member of staff is: an
/// address that already has an account keeps it, an unknown one gets an account with no usable
/// password and an emailed link to choose one.
///
/// <para>
/// It is required rather than optional because a restaurant with no owner is a restaurant nobody
/// can configure — the same state the last-owner rule exists to prevent, and it would be odd to
/// forbid arriving at it by removal while allowing it at birth.
/// </para>
/// </param>
public sealed record CreateRestaurantRequest(
    string Name,
    string? Slug,
    string Phone,
    decimal CommissionPercent,
    string OwnerEmail,
    string OwnerFullName,
    string? OwnerPhone);

/// <param name="InvitationEmailed">
/// Whether the owner was actually told. Same three outcomes as an ordinary staff invitation, and
/// reported for the same reason: the restaurant exists either way, and an admin who was told an
/// email went out will not chase one that did not.
/// </param>
public sealed record CreatedRestaurantResponse(
    PlatformRestaurantResponse Restaurant,
    bool InvitationEmailed);
