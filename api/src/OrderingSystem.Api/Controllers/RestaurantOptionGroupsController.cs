using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderingSystem.Api.Auth;
using OrderingSystem.Application.Features.Menu;

namespace OrderingSystem.Api.Controllers;

/// <summary>
/// Option groups belong to the restaurant, not to an item, which is what lets one "Extras" group
/// serve every burger. Attaching a group to an item lives on the menu-items controller.
/// </summary>
[ApiController]
[Route("api/restaurant/option-groups")]
[Authorize(Policy = AuthorizationPolicies.RestaurantStaff)]
public sealed class RestaurantOptionGroupsController(MenuAdminService menu) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OptionGroupResponse>>> List(CancellationToken ct) =>
        Ok(await menu.ListOptionGroupsAsync(ct));

    [HttpPost]
    [ProducesResponseType<OptionGroupResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<OptionGroupResponse>> Create(
        CreateOptionGroupRequest request, CancellationToken ct)
    {
        var created = await menu.CreateOptionGroupAsync(request, ct);
        return CreatedAtAction(nameof(List), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<OptionGroupResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OptionGroupResponse>> Update(
        Guid id, UpdateOptionGroupRequest request, CancellationToken ct) =>
        Ok(await menu.UpdateOptionGroupAsync(id, request, ct));

    [HttpPost("{id:guid}/options")]
    [ProducesResponseType<OptionResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<OptionResponse>> AddOption(
        Guid id, CreateOptionRequest request, CancellationToken ct)
    {
        var created = await menu.AddOptionAsync(id, request, ct);
        return CreatedAtAction(nameof(List), new { id = created.Id }, created);
    }

    [HttpPut("options/{optionId:guid}")]
    [ProducesResponseType<OptionResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OptionResponse>> UpdateOption(
        Guid optionId, UpdateOptionRequest request, CancellationToken ct) =>
        Ok(await menu.UpdateOptionAsync(optionId, request, ct));
}
