using System.Security.Claims;
using Microsoft.AspNetCore.Http;
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
    public const string RestaurantIdClaim = TenantClaims.RestaurantId;

    /// <summary>Role claim type. Matches the RoleClaimType configured on the bearer handler.</summary>
    public const string RoleClaim = TenantClaims.Role;

    private readonly IHttpContextAccessor _accessor = accessor;

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public Guid? UserId => TenantClaims.UserIdOf(Principal);

    public Guid? RestaurantId => TenantClaims.RestaurantIdOf(Principal);

    public bool IsPlatformAdmin =>
        Principal?.IsInRole(nameof(RoleType.PlatformAdmin)) ?? false;
}
