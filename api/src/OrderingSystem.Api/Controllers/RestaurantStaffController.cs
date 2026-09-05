using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderingSystem.Api.Auth;
using OrderingSystem.Application.Features.Restaurants;

namespace OrderingSystem.Api.Controllers;

/// <summary>
/// A restaurant's staff list.
///
/// <para>
/// Owner-only throughout, including reading it. A staff list is a list of the people who can see
/// every customer's address and phone number, so who is on it is not something the rest of the
/// staff need to be able to enumerate.
/// </para>
/// <para>
/// The restaurant is never in the route. It comes from the token, so no caller can aim any of
/// these at another restaurant by editing a URL.
/// </para>
/// </summary>
[ApiController]
[Route("api/restaurant/staff")]
[Authorize(Policy = AuthorizationPolicies.RestaurantOwner)]
public sealed class RestaurantStaffController(RestaurantStaffService staff) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<StaffMemberResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<StaffMemberResponse>>> List(CancellationToken ct) =>
        Ok(await staff.ListAsync(ct));

    /// <summary>
    /// Adds somebody to the staff, by email address.
    /// <para>
    /// An address that already has an account here is reused rather than duplicated, so the
    /// person keeps the order history they placed as a customer. An address with no account gets
    /// one, and an emailed link to choose a password.
    /// </para>
    /// <para>
    /// 201 even when the email could not be sent. The row is committed by then, so a failure
    /// status would tell an owner nothing happened while somebody had just been granted the
    /// restaurant's entire order book. The response says whether the link went out instead.
    /// </para>
    /// </summary>
    [HttpPost]
    [ProducesResponseType<InvitedStaffResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InvitedStaffResponse>> Invite(
        InviteStaffRequest request, CancellationToken ct)
    {
        var invited = await staff.InviteAsync(request, ct);
        return CreatedAtAction(nameof(List), new { }, invited);
    }

    /// <summary>
    /// Promotes or demotes somebody. Refused on your own account, and refused on the last owner.
    /// </summary>
    [HttpPut("{userId:guid}/role")]
    [ProducesResponseType<StaffMemberResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StaffMemberResponse>> SetRole(
        Guid userId, SetStaffRoleRequest request, CancellationToken ct) =>
        Ok(await staff.SetRoleAsync(userId, request, ct));

    /// <summary>
    /// Takes somebody off the staff and ends their sessions. Their account and their own orders
    /// are untouched.
    /// </summary>
    [HttpDelete("{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Remove(Guid userId, CancellationToken ct)
    {
        await staff.RemoveAsync(userId, ct);
        return NoContent();
    }
}
