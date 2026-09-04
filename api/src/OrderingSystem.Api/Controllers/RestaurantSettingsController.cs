using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderingSystem.Api.Auth;
using OrderingSystem.Application.Features.Restaurants;

namespace OrderingSystem.Api.Controllers;

/// <summary>
/// A restaurant's own settings.
///
/// <para>
/// Two policies on one controller, which is the point of it. Reading and the rush switch are open
/// to any staff member, because a cook at eight on a Friday needs to pause orders and will not
/// have an owner standing next to them. Everything else — the name, the phone, the prep time, the
/// minimum order — is the owner's, because those are what customers are shown and charged.
/// </para>
/// <para>
/// No restaurant id appears in any route. It comes from the token, so there is no id for a caller
/// to change.
/// </para>
/// </summary>
[ApiController]
[Route("api/restaurant/settings")]
[Authorize(Policy = AuthorizationPolicies.RestaurantStaff)]
public sealed class RestaurantSettingsController(RestaurantSettingsService settings) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<RestaurantSettingsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RestaurantSettingsResponse>> Get(CancellationToken ct) =>
        Ok(await settings.GetAsync(ct));

    /// <summary>
    /// Changes the restaurant's profile and service settings. Owner-only.
    /// <para>
    /// Editing the minimum order or prep time changes what the next order is judged against and
    /// nothing that has already been placed — every order carries its own copy.
    /// </para>
    /// </summary>
    [HttpPut]
    [Authorize(Policy = AuthorizationPolicies.RestaurantOwner)]
    [ProducesResponseType<RestaurantSettingsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<RestaurantSettingsResponse>> Update(
        UpdateRestaurantSettingsRequest request, CancellationToken ct) =>
        Ok(await settings.UpdateAsync(request, ct));

    /// <summary>
    /// Pauses or resumes new orders. Any staff member, deliberately.
    /// <para>
    /// Separate from opening hours: the hours say when the kitchen intends to be open, this says
    /// whether it can cope right now. A rush, a broken fryer, a delivery that has not arrived.
    /// </para>
    /// </summary>
    [HttpPatch("accepting-orders")]
    [ProducesResponseType<RestaurantSettingsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RestaurantSettingsResponse>> SetAcceptingOrders(
        SetAcceptingOrdersRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await settings.SetAcceptingOrdersAsync(request.IsAcceptingOrders, ct));
    }
}
