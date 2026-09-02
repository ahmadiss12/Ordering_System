using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderingSystem.Application.Features.Orders;

namespace OrderingSystem.Api.Controllers;

/// <summary>
/// Placing an order. Reading and moving one arrives in the next step.
/// </summary>
[ApiController]
[Route("api/restaurants/{restaurantId:guid}/orders")]
[Authorize]
public sealed class OrdersController(CheckoutService checkout) : ControllerBase
{
    /// <summary>
    /// Places the caller's basket at this restaurant as an order.
    /// <para>
    /// Refuses in several ways, each with its own status: 404 when the restaurant or address is
    /// not there, 400 when the request itself is wrong, and 409 for everything about the state of
    /// the world — closed, paused, empty basket, sold out, below the minimum, or a total that
    /// moved since the customer agreed to it.
    /// </para>
    /// </summary>
    [HttpPost]
    [ProducesResponseType<PlacedOrderResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PlacedOrderResponse>> Checkout(
        Guid restaurantId, CheckoutRequest request, CancellationToken ct)
    {
        var placed = await checkout.CheckoutAsync(restaurantId, request, ct);

        // Location points at the order itself, which step 5 makes readable.
        return Created($"/api/orders/{placed.Id}", placed);
    }
}
