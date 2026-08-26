using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderingSystem.Application.Common;
using OrderingSystem.Application.Features.Catalog;

namespace OrderingSystem.Api.Controllers;

/// <summary>
/// The marketplace's front door. Anonymous by design — a customer must be able to see what is on
/// offer before deciding whether to create an account.
/// </summary>
[ApiController]
[Route("api/restaurants")]
[AllowAnonymous]
public sealed class RestaurantsController(CatalogService catalog) : ControllerBase
{
    /// <summary>
    /// Browse restaurants. Supplying a zone narrows the list to those who deliver there and fills
    /// in that zone's fee and time on each row.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<RestaurantSummary>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<RestaurantSummary>>> List(
        [FromQuery] Guid? zoneId, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Ok(await catalog.ListRestaurantsAsync(zoneId, page, pageSize, ct));

    /// <summary>Addressed by slug rather than id, because the slug is what appears in a shareable link.</summary>
    [HttpGet("{slug}")]
    [ProducesResponseType<RestaurantDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RestaurantDetail>> Get(string slug, CancellationToken ct) =>
        Ok(await catalog.GetRestaurantAsync(slug, ct));

    /// <summary>The whole menu in one call — see CatalogService for why it is not paged.</summary>
    [HttpGet("{slug}/menu")]
    [ProducesResponseType<RestaurantMenu>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RestaurantMenu>> Menu(string slug, CancellationToken ct) =>
        Ok(await catalog.GetMenuAsync(slug, ct));
}
