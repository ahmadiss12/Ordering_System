using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace OrderingSystem.Api.Auth;

/// <summary>
/// Reads who is asking out of a validated principal.
///
/// <para>
/// Split out of <see cref="HttpTenantContext"/> because there are now two ways in. A controller
/// arrives with an <c>HttpContext</c>; a SignalR connection arrives with a <c>Hub.Context.User</c>
/// and no dependable <c>HttpContext</c> behind it. Both must read the same claims by the same
/// names, or a connection could end up in a group its bearer is not entitled to — so the names
/// and the parsing live here once rather than being written out twice.
/// </para>
/// </summary>
internal static class TenantClaims
{
    /// <summary>Claim carrying the restaurant a staff member belongs to.</summary>
    public const string RestaurantId = "restaurant_id";

    /// <summary>Role claim type. Matches the RoleClaimType configured on the bearer handler.</summary>
    public const string Role = "role";

    public static Guid? UserIdOf(ClaimsPrincipal? principal) =>
        GuidOf(principal, JwtRegisteredClaimNames.Sub);

    public static Guid? RestaurantIdOf(ClaimsPrincipal? principal) =>
        GuidOf(principal, RestaurantId);

    private static Guid? GuidOf(ClaimsPrincipal? principal, string claimType) =>
        Guid.TryParse(principal?.FindFirstValue(claimType), out var value) ? value : null;
}
