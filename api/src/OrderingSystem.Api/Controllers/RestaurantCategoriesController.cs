using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderingSystem.Api.Auth;
using OrderingSystem.Application.Features.Menu;

namespace OrderingSystem.Api.Controllers;

/// <summary>
/// Menu sections, scoped to the caller's own restaurant.
/// <para>
/// The policy on this class requires both a staff role and a restaurant_id claim, so an
/// unauthenticated or wrongly-scoped caller never reaches an action. Which row they may touch is
/// then decided per request by ITenantGuard — the policy answers "may you be here", the guard
/// answers "may you touch this".
/// </para>
/// </summary>
[ApiController]
[Route("api/restaurant/categories")]
[Authorize(Policy = AuthorizationPolicies.RestaurantStaff)]
public sealed class RestaurantCategoriesController(MenuAdminService menu) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CategoryResponse>>> List(CancellationToken ct) =>
        Ok(await menu.ListCategoriesAsync(ct));

    [HttpPost]
    [ProducesResponseType<CategoryResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CategoryResponse>> Create(CreateCategoryRequest request, CancellationToken ct)
    {
        var created = await menu.CreateCategoryAsync(request, ct);
        return CreatedAtAction(nameof(List), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<CategoryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CategoryResponse>> Update(
        Guid id, UpdateCategoryRequest request, CancellationToken ct) =>
        Ok(await menu.UpdateCategoryAsync(id, request, ct));
}
