using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderingSystem.Application.Common;
using OrderingSystem.Application.Features.Orders;

namespace OrderingSystem.Api.Controllers;

/// <summary>
/// An order seen from whichever side the caller is on.
/// <para>
/// Authenticated but not staff-scoped, and deliberately so: the same order is one thing to the
/// person who ordered it and another to the kitchen cooking it, and both reach it here. Which of
/// them is asking is worked out from the order, not from the route — putting it in the route
/// would be a second copy of a rule the state machine and the query filters already hold.
/// </para>
/// <para>
/// The customer half also covers a restaurant owner who orders somebody else's food, which is why
/// this is not behind the staff policy.
/// </para>
/// </summary>
[ApiController]
[Route("api/orders")]
[Authorize]
public sealed class MyOrdersController(
    OrderQueryService orders, OrderTransitionService transitions) : ControllerBase
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

    /// <summary>
    /// Moves the order to its next status — accepted, refused, being prepared, on its way,
    /// handed over, or called off.
    /// <para>
    /// One endpoint rather than four named ones: the detail above hands a screen the moves it may
    /// make, and the screen posts one of them straight back. Refusals are specific — 403 when the
    /// move belongs to the other party, 409 when the order has already moved on or somebody else
    /// moved it first, and 400 when a refusal arrives without a reason.
    /// </para>
    /// <para>
    /// It answers with the whole order, so the screen that pressed the button gets the new status,
    /// the refreshed trail and the next set of buttons in one reply.
    /// </para>
    /// </summary>
    [HttpPost("{orderId:guid}/status")]
    [ProducesResponseType<OrderDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderDetailResponse>> ChangeStatus(
        Guid orderId, ChangeOrderStatusRequest request, CancellationToken ct) =>
        Ok(await transitions.ChangeStatusAsync(orderId, request, ct));
}
