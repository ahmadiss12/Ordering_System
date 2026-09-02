using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderingSystem.Application.Features.Cart;
using OrderingSystem.Domain.Enums;

namespace OrderingSystem.Api.Controllers;

/// <summary>
/// A customer's basket at one restaurant.
/// <para>
/// Authenticated but not staff-scoped: any signed-in user has baskets, including a restaurant
/// owner ordering somebody else's food. The restaurant is a route parameter rather than a claim
/// for the same reason — this is the customer half of the system.
/// </para>
/// </summary>
[ApiController]
[Route("api/restaurants/{restaurantId:guid}/cart")]
[Authorize]
public sealed class CartController(CartService cart) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<CartResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CartResponse>> Get(Guid restaurantId, CancellationToken ct) =>
        Ok(await cart.GetAsync(restaurantId, ct));

    /// <summary>
    /// What this basket would cost, without committing to it.
    /// <para>
    /// A GET because it changes nothing, and because the storefront asks for it again every time
    /// the customer switches between delivery and pickup or picks a different address.
    /// </para>
    /// </summary>
    [HttpGet("quote")]
    [ProducesResponseType<QuoteResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<QuoteResponse>> Quote(
        Guid restaurantId,
        [FromQuery] FulfillmentType fulfillment,
        [FromQuery] Guid? addressId,
        CancellationToken ct) =>
        Ok(await cart.QuoteAsync(restaurantId, fulfillment, addressId, ct));

    [HttpPost("lines")]
    [ProducesResponseType<CartResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CartResponse>> AddLine(
        Guid restaurantId, AddCartLineRequest request, CancellationToken ct) =>
        Ok(await cart.AddLineAsync(restaurantId, request, ct));

    /// <summary>
    /// Changes how many, or the note. Options are not editable — a different set of options is a
    /// different line, so the storefront removes and re-adds.
    /// </summary>
    [HttpPut("lines/{lineId:guid}")]
    [ProducesResponseType<CartResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CartResponse>> UpdateLine(
        Guid restaurantId, Guid lineId, UpdateCartLineRequest request, CancellationToken ct) =>
        Ok(await cart.UpdateLineAsync(lineId, request, ct));

    [HttpDelete("lines/{lineId:guid}")]
    [ProducesResponseType<CartResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CartResponse>> RemoveLine(
        Guid restaurantId, Guid lineId, CancellationToken ct) =>
        Ok(await cart.RemoveLineAsync(lineId, ct));

    [HttpDelete]
    [ProducesResponseType<CartResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CartResponse>> Clear(Guid restaurantId, CancellationToken ct) =>
        Ok(await cart.ClearAsync(restaurantId, ct));
}
