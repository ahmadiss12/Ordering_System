namespace OrderingSystem.Application.Features.Restaurants;

/// <summary>
/// A restaurant as its own staff see it — including the two fields customers never do.
/// </summary>
/// <param name="CommissionPercent">
/// Read-only here. A restaurant needs to know what it is being charged; only a platform admin may
/// change it, because it is the platform's revenue and the restaurant's cost at the same time.
/// </param>
/// <param name="IsActive">
/// Also read-only, and different from <paramref name="IsAcceptingOrders"/>: this one is the
/// platform switching a restaurant off, which nobody inside it should be able to undo.
/// </param>
public sealed record RestaurantSettingsResponse(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string Phone,
    int DefaultPrepMinutes,
    decimal MinOrderUsd,
    bool IsAcceptingOrders,
    bool IsActive,
    decimal CommissionPercent);

/// <summary>
/// What an owner may change about their own restaurant.
///
/// <para>
/// The slug is not here. It is the address of the restaurant's public page, so changing it breaks
/// every link anybody has ever shared — a rename is a support conversation, not a form field.
/// Commission and the active switch are not here either: both belong to the platform.
/// </para>
/// <para>
/// The accepting-orders switch is not here for the opposite reason. It is the one thing on this
/// screen a cook needs at eight on a Friday, so it has an endpoint of its own that staff can
/// reach, rather than being buried in a form only an owner may submit.
/// </para>
/// </summary>
public sealed record UpdateRestaurantSettingsRequest(
    string Name,
    string? Description,
    string Phone,
    int DefaultPrepMinutes,
    decimal MinOrderUsd);

/// <summary>The rush switch. Staff-level, and separate from opening hours on purpose.</summary>
public sealed record SetAcceptingOrdersRequest(bool IsAcceptingOrders);
