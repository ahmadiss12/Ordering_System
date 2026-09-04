using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderingSystem.Api.Auth;
using OrderingSystem.Application.Features.Restaurants;

namespace OrderingSystem.Api.Controllers;

/// <summary>
/// When a restaurant is open.
///
/// <para>
/// Readable by any staff member — a cook may well want to check what the screen says the kitchen
/// is doing tomorrow — and writable only by an owner, because these hours decide whether the
/// checkout takes an order at all.
/// </para>
/// </summary>
[ApiController]
[Route("api/restaurant/hours")]
[Authorize(Policy = AuthorizationPolicies.RestaurantStaff)]
public sealed class RestaurantHoursController(OpeningHoursService hours) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<WeeklyHoursResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<WeeklyHoursResponse>> Get(CancellationToken ct) =>
        Ok(await hours.GetAsync(ct));

    /// <summary>
    /// Replaces the whole week.
    /// <para>
    /// Refused when two windows cover the same time, and refused when the week is emptied without
    /// saying so — an empty week shuts the restaurant to customers, which is legitimate and also
    /// what a half-finished edit looks like.
    /// </para>
    /// </summary>
    [HttpPut]
    [Authorize(Policy = AuthorizationPolicies.RestaurantOwner)]
    [ProducesResponseType<WeeklyHoursResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<WeeklyHoursResponse>> Set(
        SetWeeklyHoursRequest request, CancellationToken ct) =>
        Ok(await hours.SetAsync(request, ct));
}
