using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderingSystem.Api.Auth;
using OrderingSystem.Application.Features.Restaurants;

namespace OrderingSystem.Api.Controllers;

/// <summary>
/// Where a restaurant delivers and what it charges.
///
/// <para>
/// Readable by any staff member — a cook taking a phone call about delivery has a right to know
/// the answer — and writable only by an owner, because a fee is what a customer pays.
/// </para>
/// </summary>
[ApiController]
[Route("api/restaurant/zones")]
[Authorize(Policy = AuthorizationPolicies.RestaurantStaff)]
public sealed class RestaurantZonesController(RestaurantZonesService zones) : ControllerBase
{
    /// <summary>Every zone the platform has, with this restaurant's terms where it has any.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<RestaurantZoneResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RestaurantZoneResponse>>> List(CancellationToken ct) =>
        Ok(await zones.ListAsync(ct));

    /// <summary>
    /// Sets the terms for one zone, creating them if this restaurant had none.
    /// <para>
    /// Switching a zone off keeps its fee and travel time rather than removing the row, so turning
    /// it back on later is one press. Changing either number affects the next order and no earlier
    /// one — every order carries its own copy of what it was charged.
    /// </para>
    /// </summary>
    [HttpPut("{zoneId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.RestaurantOwner)]
    [ProducesResponseType<RestaurantZoneResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RestaurantZoneResponse>> Set(
        Guid zoneId, SetRestaurantZoneRequest request, CancellationToken ct) =>
        Ok(await zones.SetAsync(zoneId, request, ct));
}
