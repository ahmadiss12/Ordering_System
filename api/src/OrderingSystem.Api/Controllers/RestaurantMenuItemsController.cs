using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderingSystem.Api.Auth;
using OrderingSystem.Application.Features.Menu;

namespace OrderingSystem.Api.Controllers;

[ApiController]
[Route("api/restaurant/menu-items")]
[Authorize(Policy = AuthorizationPolicies.RestaurantStaff)]
public sealed class RestaurantMenuItemsController(MenuAdminService menu) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<MenuItemResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<MenuItemResponse>> Create(CreateMenuItemRequest request, CancellationToken ct)
    {
        var created = await menu.CreateMenuItemAsync(request, ct);
        return CreatedAtAction(nameof(Create), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<MenuItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<MenuItemResponse>> Update(
        Guid id, UpdateMenuItemRequest request, CancellationToken ct) =>
        Ok(await menu.UpdateMenuItemAsync(id, request, ct));

    /// <summary>Pressed mid-service when the kitchen runs out, so it takes one field, not the item.</summary>
    [HttpPatch("{id:guid}/availability")]
    [ProducesResponseType<MenuItemResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MenuItemResponse>> SetAvailability(
        Guid id, SetAvailabilityRequest request, CancellationToken ct) =>
        Ok(await menu.SetAvailabilityAsync(id, request, ct));

    /// <summary>Soft delete. Order lines point at this row and must keep resolving.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await menu.DeleteMenuItemAsync(id, ct);
        return NoContent();
    }

    /// <summary>
    /// Uploads a photo. The file is decoded, resized, stripped of metadata and re-encoded before
    /// anything is written — what is stored is never what arrived.
    /// </summary>
    [HttpPost("{id:guid}/image")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    [ProducesResponseType<MenuItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<MenuItemResponse>> UploadImage(
        Guid id, IFormFile file, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(file);

        await using var content = file.OpenReadStream();
        return Ok(await menu.SetImageAsync(id, content, ct));
    }

    [HttpDelete("{id:guid}/image")]
    [ProducesResponseType<MenuItemResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MenuItemResponse>> RemoveImage(Guid id, CancellationToken ct) =>
        Ok(await menu.RemoveImageAsync(id, ct));

    [HttpPut("{id:guid}/option-groups")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> AttachOptionGroup(
        Guid id, AttachOptionGroupRequest request, CancellationToken ct)
    {
        await menu.AttachOptionGroupAsync(id, request, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}/option-groups/{optionGroupId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DetachOptionGroup(Guid id, Guid optionGroupId, CancellationToken ct)
    {
        await menu.DetachOptionGroupAsync(id, optionGroupId, ct);
        return NoContent();
    }
}
