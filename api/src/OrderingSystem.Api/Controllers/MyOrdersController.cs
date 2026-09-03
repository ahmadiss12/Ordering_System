using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderingSystem.Application.Common;
using OrderingSystem.Application.Features.Orders;

namespace OrderingSystem.Api.Controllers;

/// <summary>
/// A customer's own orders.
/// <para>
/// Authenticated but not staff-scoped: anybody signed in has an order history, including a
/// restaurant owner who orders somebody else's food.
/// </para>
/// </summary>
[ApiController]
[Route("api/orders")]
[Authorize]
public sealed class MyOrdersController(OrderQueryService orders) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<OrderSummaryResponse>>> Mine(
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Ok(await orders.MineAsync(page, pageSize, ct));

    /// <summary>
    /// One order in full. Reachable by its customer and by staff at the restaurant that is
    /// cooking it; anybody else gets a 404 rather than a 403, so an order number cannot be
    /// confirmed by guessing.
    /// </summary>
    [HttpGet("{orderId:guid}")]
    [ProducesResponseType<OrderDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDetailResponse>> ById(Guid orderId, CancellationToken ct) =>
        Ok(await orders.ByIdAsync(orderId, ct));
}
