namespace OrderingSystem.Application.Features.Cart;

/// <summary>One option the customer picked, and how many of it.</summary>
public sealed record ChosenOptionRequest(Guid OptionId, int Quantity);

public sealed record AddCartLineRequest(
    Guid MenuItemId,
    int Quantity,
    string? Note,
    IReadOnlyList<ChosenOptionRequest> Options);

/// <summary>
/// Changes a line already in the cart. Options are not editable here — changing them makes it a
/// different line, and the storefront removes and re-adds rather than pretending otherwise.
/// </summary>
public sealed record UpdateCartLineRequest(int Quantity, string? Note);

public sealed record CartLineOptionResponse(
    Guid OptionId, string GroupName, string Name, int Quantity, decimal PriceDeltaUsd);

/// <summary>
/// One line as it stands right now.
/// <para>
/// The prices are read live from the menu, never stored on the cart, so a basket left open for a
/// day shows today's prices rather than yesterday's. <see cref="IsAvailable"/> is what lets the
/// screen say a dish sold out while the customer was deciding.
/// </para>
/// </summary>
public sealed record CartLineResponse(
    Guid Id,
    Guid MenuItemId,
    string Name,
    string? ImageUrl,
    int Quantity,
    string? Note,
    bool IsAvailable,
    decimal UnitPriceUsd,
    decimal LineTotalUsd,
    IReadOnlyList<CartLineOptionResponse> Options);

/// <summary>
/// The basket at one restaurant.
/// <para>
/// <see cref="SubtotalUsd"/> is the lines added up and nothing more. Delivery, the minimum-order
/// check and the promised time arrive with the quote in the next step; this is the number a
/// basket badge shows, not the number anybody is charged.
/// </para>
/// </summary>
public sealed record CartResponse(
    Guid Id,
    Guid RestaurantId,
    string RestaurantName,
    string RestaurantSlug,
    int ItemCount,
    decimal SubtotalUsd,
    bool HasUnavailableItems,
    IReadOnlyList<CartLineResponse> Lines);
