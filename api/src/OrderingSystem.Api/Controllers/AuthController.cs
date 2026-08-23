using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderingSystem.Application.Features.Auth;

namespace OrderingSystem.Api.Controllers;

/// <summary>
/// Everything a client needs to obtain, keep and surrender a session. All anonymous: a caller
/// holding a valid token has no reason to be here.
/// </summary>
[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public sealed class AuthController(AuthService auth) : ControllerBase
{
    [HttpPost("register")]
    [ProducesResponseType<AuthTokensResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthTokensResponse>> Register(
        RegisterRequest request, CancellationToken ct) =>
        Ok(await auth.RegisterAsync(request, ct));

    [HttpPost("login")]
    [ProducesResponseType<AuthTokensResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthTokensResponse>> Login(
        LoginRequest request, CancellationToken ct) =>
        Ok(await auth.LoginAsync(request, ct));

    /// <summary>
    /// Exchanges a refresh token for a new pair. The old one is spent by this call — presenting it
    /// again signs out every session from that login.
    /// </summary>
    [HttpPost("refresh")]
    [ProducesResponseType<AuthTokensResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthTokensResponse>> Refresh(
        RefreshRequest request, CancellationToken ct) =>
        Ok(await auth.RefreshAsync(request, ct));

    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken ct)
    {
        await auth.LogoutAsync(request, ct);
        return NoContent();
    }

    /// <summary>
    /// Always answers 202, whether or not the address is registered. Answering differently would
    /// turn this into a way to discover who has an account.
    /// </summary>
    [HttpPost("forgot-password")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        await auth.ForgotPasswordAsync(request, ct);
        return Accepted();
    }

    [HttpPost("reset-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        await auth.ResetPasswordAsync(request, ct);
        return NoContent();
    }
}
