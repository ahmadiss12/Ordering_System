using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderingSystem.Api.Auth;
using OrderingSystem.Application.Features.Platform;

namespace OrderingSystem.Api.Controllers;

/// <summary>
/// The platform's view of its restaurants.
///
/// <para>
/// The other side of the two-level split, and the only place the commission rate and the listing
/// switch can be set. Both are the platform's; neither appears on any request a restaurant can
/// make. A restaurant sees both on its own settings screen, because it is entitled to know what
/// it is charged and whether it is listed — it just cannot change either.
/// </para>
/// <para>
/// The policy here is not the only guard. The service checks the same thing again, because
/// <c>EnsureCanActFor</c> — the check every other write path uses — admits a restaurant acting on
/// itself, and one forgotten attribute would be the whole defence otherwise.
/// </para>
/// </summary>
[ApiController]
[Route("api/platform/restaurants")]
[Authorize(Policy = AuthorizationPolicies.PlatformAdmin)]
public sealed class PlatformRestaurantsController(PlatformRestaurantsService platform) : ControllerBase
{
    /// <summary>
    /// Every restaurant, listed or not. Unlike the public catalog, which shows only the ones a
    /// customer could order from — if this hid the switched-off ones too, nothing anywhere could
    /// switch one back on.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PlatformRestaurantResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PlatformRestaurantResponse>>> List(CancellationToken ct) =>
        Ok(await platform.ListAsync(ct));

    /// <summary>
    /// Takes a restaurant on to the platform and hands it to an owner.
    ///
    /// <para>
    /// It arrives hidden, with no hours, no delivery zones and no menu — the state a restaurant
    /// is in before anybody sets it up, which is the owner's job and not the platform's to guess
    /// at. The owner is emailed a link to choose a password, exactly as any other member of
    /// staff would be.
    /// </para>
    /// <para>
    /// 201 even when the email could not be sent: the restaurant exists by then, and the response
    /// says whether the link went out rather than pretending nothing happened.
    /// </para>
    /// </summary>
    [HttpPost]
    [ProducesResponseType<CreatedRestaurantResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreatedRestaurantResponse>> Create(
        CreateRestaurantRequest request, CancellationToken ct)
    {
        var created = await platform.CreateAsync(request, ct);
        return CreatedAtAction(nameof(List), new { }, created);
    }

    /// <summary>
    /// Sets what the platform charges, from the next order onwards. Orders already placed keep
    /// the rate they were placed under.
    /// </summary>
    [HttpPut("{restaurantId:guid}/commission")]
    [ProducesResponseType<PlatformRestaurantResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PlatformRestaurantResponse>> SetCommission(
        Guid restaurantId, SetCommissionRequest request, CancellationToken ct) =>
        Ok(await platform.SetCommissionAsync(restaurantId, request, ct));

    /// <summary>
    /// Shows or hides a restaurant. Hiding it stops customers finding or ordering from it, and
    /// leaves orders already placed — and the staff working them — alone.
    /// </summary>
    [HttpPut("{restaurantId:guid}/listing")]
    [ProducesResponseType<PlatformRestaurantResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PlatformRestaurantResponse>> SetListing(
        Guid restaurantId, SetListingRequest request, CancellationToken ct) =>
        Ok(await platform.SetListingAsync(restaurantId, request, ct));
}
