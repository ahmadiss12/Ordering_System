using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderingSystem.Api.Auth;
using OrderingSystem.Application.Features.Reports;

namespace OrderingSystem.Api.Controllers;

/// <summary>
/// What a restaurant did, over a range of its own calendar days.
///
/// <para>
/// Owner-only, like the rest of the settings area. Revenue and commission are what the business
/// earns and is charged, and a cook working the queue has no call on either. The rejection rate
/// sits alongside them because splitting one report in two so a staff member could see half of it
/// would be a strange thing to build for a screen nobody asked to share.
/// </para>
/// <para>
/// No restaurant id in the route. It comes from the token, as everywhere else.
/// </para>
/// </summary>
[ApiController]
[Route("api/restaurant/reports")]
[Authorize(Policy = AuthorizationPolicies.RestaurantOwner)]
public sealed class RestaurantReportsController(RestaurantReportService reports) : ControllerBase
{
    /// <summary>
    /// Orders, revenue, commission and refusals across a date range.
    /// </summary>
    /// <param name="from">
    /// First day, in the restaurant's own calendar. Omit for a month ending at
    /// <paramref name="to"/>.
    /// </param>
    /// <param name="to">Last day. Omit for today in the restaurant's timezone, not the caller's.</param>
    [HttpGet("summary")]
    [ProducesResponseType<RestaurantReportResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<RestaurantReportResponse>> Summary(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        Ok(await reports.SummaryAsync(new ReportRangeRequest(from, to), ct));
}
