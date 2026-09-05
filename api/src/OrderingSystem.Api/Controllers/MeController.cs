using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderingSystem.Application.Features.Auth;

namespace OrderingSystem.Api.Controllers;

/// <summary>
/// The signed-in person's own account.
///
/// <para>
/// A separate controller from <see cref="AuthController"/>, which carries
/// <c>[AllowAnonymous]</c> for the whole class — getting in has to be reachable by somebody who
/// is not in yet. An <c>[Authorize]</c> on an action inside it is silently overridden by that,
/// so these would have been anonymous while looking guarded. The analyser refuses to compile
/// that, which is how this landed here rather than one attribute away from a hole.
/// </para>
/// </summary>
[ApiController]
[Route("api/me")]
[Authorize]
public sealed class MeController(AuthService auth) : ControllerBase
{
    /// <summary>
    /// Who the caller is, read from the database rather than decoded from their token — a name
    /// corrected since they signed in is not in the token they are holding.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<ProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ProfileResponse>> Get(CancellationToken ct) =>
        Ok(await auth.MeAsync(ct));

    /// <summary>
    /// Corrects a name or a phone number. Not an email address: it is the login identifier and
    /// the only way back into a locked-out account, so changing it needs the new one proved
    /// before the old one stops working — a verification flow, not a text box.
    /// </summary>
    [HttpPut]
    [ProducesResponseType<ProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ProfileResponse>> Update(
        UpdateProfileRequest request, CancellationToken ct) =>
        Ok(await auth.UpdateProfileAsync(request, ct));

    /// <summary>
    /// Changes a password from inside a session. The current one is required, and every other
    /// session ends — a password changed because a phone went missing has changed nothing if the
    /// phone stays signed in.
    /// </summary>
    [HttpPost("password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        await auth.ChangePasswordAsync(request, ct);
        return NoContent();
    }
}
