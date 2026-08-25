using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderingSystem.Application.Features.Orders;

namespace OrderingSystem.Api.Controllers;

/// <summary>
/// The order lifecycle. Authenticated throughout — who may make a given transition depends on
/// whether the caller is the customer, the restaurant's staff, or a platform admin, and that
/// question cannot be asked of an anonymous caller.
/// </summary>
[ApiController]
[Route("api/orders")]
[Authorize]
public sealed class OrdersController(OrderStatusService orderStatus) : ControllerBase
{
    /// <summary>
    /// Moves one order to its next status. The response carries the legal successors, so a
    /// dashboard can render the buttons that exist rather than reimplementing the transition
    /// table on the client.
    /// </summary>
    [HttpPost("{id:guid}/status")]
    [ProducesResponseType<OrderStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderStatusResponse>> AdvanceStatus(
        Guid id, AdvanceOrderStatusRequest request, CancellationToken ct) =>
        Ok(await orderStatus.AdvanceAsync(id, request, ct));
}
