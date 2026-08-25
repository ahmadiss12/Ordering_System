using Microsoft.AspNetCore.Authorization;
using OrderingSystem.Domain.Enums;

namespace OrderingSystem.Api.Auth;

/// <summary>
/// The named policies controllers declare. Two layers of protection, not one: a policy decides
/// whether a caller may reach the endpoint at all, and <c>ITenantGuard</c> plus the query filters
/// decide which rows they may touch once inside.
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>Platform staff. Sees across every restaurant.</summary>
    public const string PlatformAdmin = nameof(PlatformAdmin);

    /// <summary>
    /// Anyone acting for a restaurant. Requires the restaurant_id claim as well as the role,
    /// because a role without a restaurant cannot be scoped to anything.
    /// </summary>
    public const string RestaurantStaff = nameof(RestaurantStaff);

    /// <summary>Owner-only actions: staff accounts, delivery zones, fees, prep time.</summary>
    public const string RestaurantOwner = nameof(RestaurantOwner);

    public static AuthorizationOptions AddOrderingPolicies(this AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.AddPolicy(PlatformAdmin, policy =>
            policy.RequireRole(nameof(RoleType.PlatformAdmin)));

        options.AddPolicy(RestaurantStaff, policy => policy
            .RequireRole(nameof(RoleType.RestaurantStaff), nameof(RoleType.RestaurantOwner))
            .RequireClaim(HttpTenantContext.RestaurantIdClaim));

        options.AddPolicy(RestaurantOwner, policy => policy
            .RequireRole(nameof(RoleType.RestaurantOwner))
            .RequireClaim(HttpTenantContext.RestaurantIdClaim));

        return options;
    }
}
