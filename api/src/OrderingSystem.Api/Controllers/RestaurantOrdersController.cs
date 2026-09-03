using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderingSystem.Api.Auth;
using OrderingSystem.Application.Common;
using OrderingSystem.Application.Features.Orders;
using OrderingSystem.Domain.Enums;

namespace OrderingSystem.Api.Controllers;

/// <summary>
/// The restaurant's own orders — the queue a kitchen works from.
/// </summary>
[ApiController]
[Route("api/restaurant/orders")]
[Authorize(Policy = AuthorizationPolicies.RestaurantStaff)]
public sealed class RestaurantOrdersController(OrderQueryService orders) : ControllerBase
{
    /// <summary>
    /// The queue, newest first.
    /// <para>
    /// Statuses are a filter rather than a fixed set: a kitchen screen asks for the live ones and
    /// a history screen asks for the finished ones, and neither wants the other's rows.
    /// </para>
    /// <para>
    /// They read from opposite ends, too. A kitchen works the order that has waited longest;
    /// somebody looking at yesterday wants the most recent first. With paging, that is not a
    /// preference a client can apply after the fact.
    /// </para>
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<OrderSummaryResponse>>> Queue(
        [FromQuery] OrderStatus[]? status,
        [FromQuery] bool newestFirst,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct) =>
        Ok(await orders.ForRestaurantAsync(status, newestFirst, page, pageSize, ct));
}
