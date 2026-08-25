using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderingSystem.Application.Features.Catalogue;

namespace OrderingSystem.Api.Controllers;

/// <summary>
/// The public storefront. Anonymous on purpose: a customer browses menus before deciding whether
/// to have an account at all, and requiring a token to see a menu would cost the marketplace its
/// front door.
/// </summary>
[ApiController]
[Route("api/restaurants")]
[AllowAnonymous]
public sealed class RestaurantsController(CatalogueService catalogue) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<RestaurantSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RestaurantSummaryResponse>>> List(
        CancellationToken ct) =>
        Ok(await catalogue.ListRestaurantsAsync(ct));

    /// <summary>Addressed by slug rather than id — the slug is what appears in a storefront link.</summary>
    [HttpGet("{slug}")]
    [ProducesResponseType<RestaurantDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RestaurantDetailResponse>> Get(
        string slug, CancellationToken ct) =>
        Ok(await catalogue.GetRestaurantAsync(slug, ct));

    [HttpGet("{slug}/menu")]
    [ProducesResponseType<MenuResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MenuResponse>> Menu(
        string slug, CancellationToken ct) =>
        Ok(await catalogue.GetMenuAsync(slug, ct));
}
