using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderingSystem.Application.Features.Catalog;

namespace OrderingSystem.Api.Controllers;

[ApiController]
[Route("api/menu-items")]
[AllowAnonymous]
public sealed class MenuItemsController(CatalogService catalog) : ControllerBase
{
    /// <summary>
    /// One item with everything needed to draw its detail screen. The selection bounds returned
    /// are the ones that apply to this item, with any per-item override already resolved.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<MenuItemDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MenuItemDetail>> Get(Guid id, CancellationToken ct) =>
        Ok(await catalog.GetMenuItemAsync(id, ct));
}
