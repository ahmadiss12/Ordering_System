using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Domain.Enums;

namespace OrderingSystem.Api.Auth;

/// <summary>
/// Resolves who is asking from the current request's validated JWT claims.
/// <para>
/// Every value here comes from a token the server signed and has already verified — never from a
/// header, a query string or a body, because all three are attacker-controlled. This type is the
/// input to every global query filter, so that distinction is the whole security model.
/// </para>
/// </summary>
public sealed class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    /// <summary>Claim carrying the restaurant a staff member belongs to.</summary>
    public const string RestaurantIdClaim = "restaurant_id";

    /// <summary>Role claim type. Matches the RoleClaimType configured on the bearer handler.</summary>
    public const string RoleClaim = "role";

    private readonly IHttpContextAccessor _accessor = accessor;

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public Guid? UserId => ReadGuid(JwtRegisteredClaimNames.Sub);

    public Guid? RestaurantId => ReadGuid(RestaurantIdClaim);

    public bool IsPlatformAdmin =>
        Principal?.IsInRole(nameof(RoleType.PlatformAdmin)) ?? false;

    private Guid? ReadGuid(string claimType)
    {
        var raw = Principal?.FindFirstValue(claimType);
        return Guid.TryParse(raw, out var value) ? value : null;
    }
}
